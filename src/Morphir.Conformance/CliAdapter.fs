// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

/// <summary>One process that the <c>cli</c> adapter starts: no shell, an argument list and a working directory.</summary>
type CliProcess = {
    FileName: string
    Arguments: string list
    WorkingDirectory: string
}

/// <summary>The result of a process that ran to its end.</summary>
type CliProcessResult = {
    ExitCode: int
    StandardOutput: string
    StandardError: string
}

/// <summary>A document that the adapter writes beside the case documents, for example a project file.</summary>
type CliProjectFile = { Path: string; Text: string }

/// <summary>How the <c>cli</c> adapter drives a <c>morphir</c> executable.</summary>
type CliAdapterOptions = {
    /// <summary>The executable, for example <c>morphir</c>, or <c>dotnet</c> for a framework-dependent tool.</summary>
    FileName: string
    /// <summary>The arguments before <c>compile</c>, for example the path of <c>dotnet-morphir.dll</c>.</summary>
    PrefixArguments: string list
    /// <summary>
    /// More documented <c>compile</c> options, after the case options, for example
    /// <c>--target fsharp --generate-output generated</c>. Relative paths are relative to the case directory.
    /// </summary>
    ExtraArguments: string list
    /// <summary>The directory below which each case gets its own new project directory.</summary>
    WorkRoot: string
    /// <summary>The project file of a <c>project file</c> row, from the case documents in case order.</summary>
    ProjectFile: CompileInput -> Result<CliProjectFile, string>
    /// <summary>Runs one process. Tests give a runner that holds their process gate and sandbox.</summary>
    Run: CliProcess -> CancellationToken -> Task<CliProcessResult>
    /// <summary>Keep each case directory after the observation, for investigation.</summary>
    KeepCaseDirectories: bool
}

/// <summary>
/// The <c>cli</c> adapter (MCK-1): it drives any <c>morphir</c> executable through <c>compile --json-lines</c>, the
/// <c>completed</c> record and the written IR. It uses only the documented command line
/// (<c>docs/content/docs/cli/compile.md</c>): the options <c>--project</c>, <c>--input</c>, <c>--project-file</c>,
/// <c>--ir-version</c> and <c>--json-lines</c>, the <c>project.name</c> and <c>project.exposed_modules</c> keys of
/// <c>morphir.toml</c>, the JSON Lines record format, the report members <c>success</c>, <c>exitCode</c>,
/// <c>sources</c>, <c>output</c> and <c>diagnostics</c>, and the exit codes. It ignores unknown records and members.
/// </summary>
[<RequireQualifiedAccess>]
module CliAdapter =

    let private failure message partial = { Message = message; Partial = partial }

    let private tomlString (text: string) =
        let escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"")

        $"\"{escaped}\""

    /// <summary>The <c>morphir.toml</c> of a case: the package name and the exposed modules.</summary>
    let configuration (input: CompileInput) =
        let exposed =
            input.ExposedModules
            |> List.map tomlString
            |> String.concat ", "

        $"[project]\nname = {tomlString input.Package}\nexposed_modules = [{exposed}]\n"

    /// <summary>MSBuild escaping of an item specification, then XML escaping.</summary>
    let private itemSpec (path: string) =
        let msbuild =
            path
            |> Seq.map (fun c ->
                match c with
                | '%'
                | '$'
                | '@'
                | ';'
                | '\''
                | '*'
                | '?' -> $"%%%02X{int c}"
                | c -> string c
            )
            |> String.concat ""

        Security.SecurityElement.Escape msbuild

    /// <summary>
    /// The default project file: an SDK-style <c>.fsproj</c> whose <c>Compile</c> items are the case documents in
    /// case order. Documents of other languages have no default project file.
    /// </summary>
    let fsharpProjectFile (input: CompileInput) : Result<CliProjectFile, string> =
        let other =
            input.Documents
            |> List.filter (fun document ->
                not (
                    document.Uri.EndsWith(".fs", StringComparison.Ordinal)
                    || document.Uri.EndsWith(".fsi", StringComparison.Ordinal)
                )
            )

        match other with
        | document :: _ ->
            Error
                $"The adapter has no project file for the document '{document.Uri}'. Give a ProjectFile function."
        | [] ->
            let items =
                input.Documents
                |> List.map (fun document ->
                    $"    <Compile Include=\"{itemSpec document.Uri}\" />\n"
                )
                |> String.concat ""

            Ok
                {
                    Path = "Conformance.fsproj"
                    Text =
                        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                        + "  <PropertyGroup>\n"
                        + "    <TargetFramework>net10.0</TargetFramework>\n"
                        + "  </PropertyGroup>\n"
                        + "  <ItemGroup>\n"
                        + items
                        + "  </ItemGroup>\n"
                        + "</Project>\n"
                }

    /// <summary>
    /// Runs a process without a shell. It reads both streams to their end, and stops the process tree when
    /// <paramref name="timeout"/> expires or the token is cancelled.
    /// </summary>
    let runProcess
        (timeout: TimeSpan)
        (invocation: CliProcess)
        (cancellationToken: CancellationToken)
        =
        task {
            let start = ProcessStartInfo(invocation.FileName)

            for argument in invocation.Arguments do
                start.ArgumentList.Add argument

            start.WorkingDirectory <- invocation.WorkingDirectory
            start.UseShellExecute <- false
            start.CreateNoWindow <- true
            start.RedirectStandardOutput <- true
            start.RedirectStandardError <- true
            use proc = new Process(StartInfo = start)
            use deadline = CancellationTokenSource.CreateLinkedTokenSource cancellationToken
            deadline.CancelAfter timeout

            proc.Start()
            |> ignore

            let output = proc.StandardOutput.ReadToEndAsync CancellationToken.None
            let error = proc.StandardError.ReadToEndAsync CancellationToken.None

            try
                do! proc.WaitForExitAsync deadline.Token
                let! output = output
                let! error = error

                return {
                    ExitCode = proc.ExitCode
                    StandardOutput = output
                    StandardError = error
                }
            with :? OperationCanceledException ->
                try
                    proc.Kill true
                with :? InvalidOperationException ->
                    ()

                cancellationToken.ThrowIfCancellationRequested()

                return
                    raise (
                        TimeoutException $"'{invocation.FileName}' did not finish within {timeout}."
                    )
        }

    let private property (name: string) (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Object ->
            match element.TryGetProperty name with
            | true, value -> Some value
            | _ -> None
        | _ -> None

    let private text name element =
        property name element
        |> Option.filter (fun value -> value.ValueKind = JsonValueKind.String)
        |> Option.map (fun value ->
            value.GetString()
            |> string
        )

    /// <summary>The JSON Lines records of stdout and the report of the one, last <c>completed</c> record.</summary>
    let private completedReport (stdout: string) =
        let lines =
            stdout.Split('\n')
            |> Array.map (fun line -> line.TrimEnd('\r'))
            |> Array.filter (fun line -> line.Length > 0)

        let parsed =
            lines
            |> Array.mapi (fun index line ->
                try
                    use document = JsonDocument.Parse line

                    match text "event" document.RootElement with
                    | Some kind -> Ok(kind, document.RootElement.Clone())
                    | None ->
                        Error
                            $"Line {index + 1} of stdout is a JSON value with no string 'event' member."
                with :? JsonException as error ->
                    Error $"Line {index + 1} of stdout is not a JSON Lines record: {error.Message}"
            )

        match
            parsed
            |> Array.tryPick (
                function
                | Error message -> Some message
                | Ok _ -> None
            )
        with
        | Some message -> Error message
        | None ->
            let records =
                parsed
                |> Array.choose (
                    function
                    | Ok record -> Some record
                    | Error _ -> None
                )

            let completed =
                records
                |> Array.filter (fun (kind, _) -> kind = "completed")

            match completed, Array.tryLast records with
            | [| _, record |], Some("completed", _) ->
                match property "report" record with
                | Some report when report.ValueKind = JsonValueKind.Object -> Ok report
                | _ -> Error "The completed record has no report object."
            | [||], _ -> Error "Stdout has no completed record."
            | [| _ |], _ -> Error "The completed record is not the last record."
            | many, _ -> Error $"Stdout has {many.Length} completed records, not one."

    /// <summary>
    /// The case URI of a report URI. A report URI is relative to <c>sources.root</c>; the case URI is relative to
    /// the directory that holds the case documents.
    /// </summary>
    let private caseUri (documents: string) (root: string option) (uri: string) =
        match root with
        | None -> uri
        | Some root ->
            let absolute = Path.GetFullPath(uri, root)

            Path.GetRelativePath(documents, absolute).Replace('\\', '/')

    let private keyOf documents root (diagnostic: JsonElement) : Result<DiagnosticKey, string> =
        let number name element =
            property name element
            |> Option.filter (fun value -> value.ValueKind = JsonValueKind.Number)
            |> Option.map (fun value -> value.GetInt32())

        let position name range =
            match property name range with
            | Some point ->
                match number "line" point, number "character" point with
                | Some line, Some character -> Some(line, character)
                | _ -> None
            | None -> None

        match text "severity" diagnostic, text "message" diagnostic with
        | Some severity, Some _ ->
            let code =
                text "code" diagnostic
                |> Option.defaultValue ""

            match property "location" diagnostic with
            | None ->
                Ok
                    {
                        Severity = severity
                        Code = code
                        Uri = ""
                        StartLine = -1
                        StartCharacter = -1
                        EndLine = -1
                        EndCharacter = -1
                    }
            | Some location ->
                match
                    text "uri" location,
                    property "range" location
                    |> Option.bind (position "start"),
                    property "range" location
                    |> Option.bind (position "end")
                with
                | Some uri, Some(startLine, startCharacter), Some(endLine, endCharacter) ->
                    Ok
                        {
                            Severity = severity
                            Code = code
                            Uri = caseUri documents root uri
                            StartLine = startLine
                            StartCharacter = startCharacter
                            EndLine = endLine
                            EndCharacter = endCharacter
                        }
                | _ -> Error "A report diagnostic has a location without a uri and a range."
        | _ -> Error "A report diagnostic has no severity or message."

    /// <summary>The observation of one report: success, the written IR and the diagnostics.</summary>
    let private observation
        (caseDirectory: string)
        (documents: string)
        (exitCode: int)
        (report: JsonElement)
        =
        let bool name =
            property name report
            |> Option.bind (fun value ->
                match value.ValueKind with
                | JsonValueKind.True -> Some true
                | JsonValueKind.False -> Some false
                | _ -> None
            )

        let reported =
            property "exitCode" report
            |> Option.filter (fun value -> value.ValueKind = JsonValueKind.Number)
            |> Option.map (fun value -> value.GetInt32())

        let root =
            property "sources" report
            |> Option.bind (text "root")
            |> Option.map (fun root -> Path.GetFullPath(root, caseDirectory))

        let diagnostics =
            match property "diagnostics" report with
            | Some array when array.ValueKind = JsonValueKind.Array ->
                array.EnumerateArray()
                |> Seq.map (keyOf documents root)
                |> Seq.toList
                |> List.fold
                    (fun state next ->
                        match state, next with
                        | Ok keys, Ok key ->
                            Ok(
                                key
                                :: keys
                            )
                        | Error message, _
                        | _, Error message -> Error message
                    )
                    (Ok [])
                |> Result.map List.rev
            | _ -> Error "The report has no diagnostics array."

        match bool "success", reported, diagnostics with
        | None, _, _ -> Error(failure "The report has no boolean success member." None)
        | _, None, _ -> Error(failure "The report has no exitCode number." None)
        | _, _, Error message -> Error(failure message None)
        | Some success, Some reported, Ok keys ->
            let partial = {
                Success = success
                ModelJson = None
                Diagnostics = keys
            }

            if
                reported
                <> exitCode
            then
                Error(
                    failure
                        $"The process exit code {exitCode} differs from the report exit code {reported}."
                        (Some partial)
                )
            elif
                success
                <> (exitCode = 0)
            then
                Error(
                    failure
                        $"The report success {success} does not agree with the exit code {exitCode}."
                        (Some partial)
                )
            elif not success then
                Ok partial
            else
                match text "output" report with
                | None -> Error(failure "A successful report has no output path." (Some partial))
                | Some output ->
                    let path = Path.GetFullPath(output, caseDirectory)

                    if File.Exists path then
                        Ok
                            { partial with
                                ModelJson = Some(File.ReadAllText(path, UTF8Encoding(false, true)))
                            }
                    else
                        Error(failure $"The report output '{output}' is not a file." (Some partial))

    let private write (path: string) (text: string) =
        Directory.CreateDirectory(
            Path.GetDirectoryName path
            |> nonNull
        )
        |> ignore

        File.WriteAllText(path, text, UTF8Encoding false)

    /// <summary>The source options of the input kind, and the documents directory.</summary>
    let private stage (options: CliAdapterOptions) (input: CompileInput) (caseDirectory: string) =
        let documents = Path.Combine(caseDirectory, "src")
        write (Path.Combine(caseDirectory, "morphir.toml")) (configuration input)

        for document in input.Documents do
            write (Path.GetFullPath(document.Uri, documents)) document.Text

        let relative (uri: string) =
            Path
                .GetRelativePath(caseDirectory, Path.GetFullPath(uri, documents))
                .Replace('\\', '/')

        match input.InputKind with
        | "files" ->
            Ok(
                "--input"
                :: (input.Documents
                    |> List.map (fun document -> relative document.Uri))
            )
        | "directory" -> Ok [ "--input"; "src" ]
        | "project file" ->
            match options.ProjectFile input with
            | Error message -> Error message
            | Ok project ->
                // Empty build files stop MSBuild from importing files of a directory above the case.
                for name in
                    [
                        "Directory.Build.props"
                        "Directory.Build.targets"
                        "Directory.Packages.props"
                    ] do
                    write (Path.Combine(caseDirectory, name)) "<Project />\n"

                write (Path.GetFullPath(project.Path, documents)) project.Text
                Ok [ "--project-file"; relative project.Path ]
        | kind -> Error $"The cli adapter does not know the input kind '{kind}'."

    let private observe
        (options: CliAdapterOptions)
        (input: CompileInput)
        (cancellationToken: CancellationToken)
        =
        task {
            let caseDirectory =
                Path.Combine(
                    options.WorkRoot,
                    "case-"
                    + Guid.NewGuid().ToString "N"
                )

            Directory.CreateDirectory caseDirectory
            |> ignore

            try
                match stage options input caseDirectory with
                | Error message -> return Error(failure message None)
                | Ok sources ->
                    let arguments =
                        options.PrefixArguments
                        @ [
                            "compile"
                            "--project"
                            caseDirectory
                            "--ir-version"
                            input.IrVersion
                            "--json-lines"
                        ]
                        @ sources
                        @ options.ExtraArguments

                    let! result =
                        options.Run
                            {
                                FileName = options.FileName
                                Arguments = arguments
                                WorkingDirectory = caseDirectory
                            }
                            cancellationToken

                    match completedReport result.StandardOutput with
                    | Error message ->
                        return
                            Error(
                                failure
                                    $"{message} Exit code {result.ExitCode}. Stderr: {result.StandardError.Trim()}"
                                    None
                            )
                    | Ok report ->
                        return
                            observation
                                caseDirectory
                                (Path.Combine(caseDirectory, "src"))
                                result.ExitCode
                                report
            finally
                if not options.KeepCaseDirectories then
                    try
                        Directory.Delete(caseDirectory, true)
                    with
                    | :? IOException
                    | :? UnauthorizedAccessException -> ()
        }

    /// <summary>The default options: the F# project file, no extra options, and <see cref="runProcess"/>.</summary>
    let options fileName prefixArguments workRoot : CliAdapterOptions = {
        FileName = fileName
        PrefixArguments = prefixArguments
        ExtraArguments = []
        WorkRoot = workRoot
        ProjectFile = fsharpProjectFile
        Run = runProcess (TimeSpan.FromMinutes 5.0)
        KeepCaseDirectories = false
    }

    /// <summary>
    /// The identity of an executable: its file name, the first line that <c>--version</c> writes, and the
    /// SHA-256 of <paramref name="implementation"/>, the file that holds the implementation (the executable,
    /// or the assembly of a framework-dependent tool).
    /// </summary>
    let identify
        (options: CliAdapterOptions)
        (implementation: string)
        (cancellationToken: CancellationToken)
        =
        task {
            let! version =
                options.Run
                    {
                        FileName = options.FileName
                        Arguments =
                            options.PrefixArguments
                            @ [ "--version" ]
                        WorkingDirectory = options.WorkRoot
                    }
                    cancellationToken

            let line =
                version.StandardOutput.Split('\n')
                |> Array.map (fun line -> line.Trim())
                |> Array.tryFind (fun line -> line.Length > 0)
                |> Option.defaultValue ""

            return {
                Name = Path.GetFileNameWithoutExtension implementation
                Version = line
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes implementation))
            }
        }

    /// <summary>The <c>cli</c> adapter over <paramref name="options"/>.</summary>
    let create identity (options: CliAdapterOptions) : Adapter = {
        Name = "cli"
        Identity = identity
        Observe = observe options
    }
