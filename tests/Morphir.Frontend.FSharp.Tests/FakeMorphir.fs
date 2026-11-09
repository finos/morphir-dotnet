// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// A scripted fake <c>morphir</c> executable for the <c>cli</c> adapter tests (task 6.3). The test executable runs
/// it when its first argument is <see cref="Argument"/>. It accepts only the documented <c>compile</c> options
/// (<c>docs/content/docs/cli/compile.md</c>) and refuses all others. It compiles with the in-process frontend, and
/// it writes only documented output, but it changes everything that the documentation leaves open: it adds
/// unknown records and members, writes logs to stderr, gives an absolute <c>sources.root</c> one directory above
/// the documents, and writes the IR to another path than <c>dotnet-morphir</c>. An adapter that depends on
/// undocumented behavior fails against it. <c>FAKE_MORPHIR_MODE</c> selects a broken output for the negative tests.
/// </summary>
module Morphir.Frontend.FSharp.Tests.FakeMorphir

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open Morphir.Conformance

/// <summary>A type of this assembly, to find it.</summary>
type Marker =
    class
    end

/// <summary>The first argument that selects the fake.</summary>
[<Literal>]
let Argument = "--fake-morphir"

/// <summary>The environment variable that selects a broken output.</summary>
[<Literal>]
let ModeVariable = "FAKE_MORPHIR_MODE"

/// <summary>The fake version that <c>--version</c> writes.</summary>
[<Literal>]
let Version = "0.0.0-fake.1"

/// <summary>The options of <c>compile</c> that compile.md documents, with whether each takes values.</summary>
let private documented =
    Map [
        "--language", true
        "-l", true
        "--extension", true
        "--input", true
        "-i", true
        "--project-file", true
        "--package-name", true
        "--ir-version", true
        "--project", true
        "--config", true
        "--json", false
        "--json-lines", false
        "--types-only", false
        "--target", true
        "--generate-output", true
        "--option", true
    ]

/// <summary>The options and their values, in order. An option that is not documented is an error.</summary>
let private parse (args: string list) =
    let rec loop (args: string list) (current: string option) (acc: Map<string, string list>) =
        match args with
        | [] -> Ok acc
        | arg :: rest when arg.StartsWith("-", StringComparison.Ordinal) ->
            match documented.TryFind arg with
            | None -> Error $"The option '{arg}' is not a documented compile option."
            | Some takesValues ->
                let acc =
                    acc
                    |> Map.change arg (Option.orElse (Some []))

                loop rest (if takesValues then Some arg else None) acc
        | value :: rest ->
            match current with
            | None -> Error $"The value '{value}' follows no option that takes values."
            | Some option ->
                loop
                    rest
                    current
                    (acc
                     |> Map.change
                         option
                         (Option.map (fun values ->
                             values
                             @ [ value ]
                         )))

    loop args None Map.empty

let private tomlValue (text: string) (key: string) =
    let line = Regex.Match(text, $"(?m)^{key} = (.*)$")

    if line.Success then Some line.Groups[1].Value else None

let private strings (text: string) = [
    for m in Regex.Matches(text, "\"((?:[^\"\\\\]|\\\\.)*)\"") ->
        m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\")
]

let private unescapeItem (text: string) =
    let xml =
        text
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&")

    Regex.Replace(
        xml,
        "%([0-9A-Fa-f]{2})",
        fun m -> string (char (Convert.ToInt32(m.Groups[1].Value, 16)))
    )

/// <summary>The documents directory and the documents in compile order, by the source option.</summary>
let private sources (options: Map<string, string list>) (cwd: string) =
    let full (path: string) = Path.GetFullPath(path, cwd)

    let relative root (path: string) =
        Path.GetRelativePath(root, path).Replace('\\', '/')

    match options.TryFind "--project-file", options.TryFind "--input" with
    | Some [ projectFile ], None ->
        let path = full projectFile

        let root =
            Path.GetDirectoryName path
            |> nonNull

        let items = [
            for m in Regex.Matches(File.ReadAllText path, "<Compile Include=\"([^\"]+)\"") ->
                unescapeItem m.Groups[1].Value
        ]

        Ok("project file", root, items)
    | None, Some [ directory ] when Directory.Exists(full directory) ->
        let root = full directory

        Ok(
            "directory",
            root,
            Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            |> Array.map (relative root)
            |> Array.sortWith (fun left right -> String.CompareOrdinal(left, right))
            |> Array.toList
        )
    | None, Some files when not files.IsEmpty ->
        let paths =
            files
            |> List.map full

        let root =
            paths
            |> List.map (fun path ->
                Path.GetDirectoryName path
                |> nonNull
            )
            |> List.reduce (fun left right ->
                let mutable common = left

                while not (right.StartsWith(common, StringComparison.Ordinal)) do
                    common <-
                        Path.GetDirectoryName common
                        |> nonNull

                common
            )

        Ok(
            "files",
            root,
            paths
            |> List.map (relative root)
        )
    | _ -> Error "Give one --project-file, one --input directory or --input files."

let private writeRecord (output: Stream) (write: Utf8JsonWriter -> unit) =
    use buffer = new MemoryStream()

    do
        use writer = new Utf8JsonWriter(buffer)
        writer.WriteStartObject()
        write writer
        writer.WriteEndObject()

    output.Write(buffer.ToArray())
    output.Write("\n"B)
    output.Flush()

let private compile (options: Map<string, string list>) (cwd: string) (mode: string) =
    let stdout = Console.OpenStandardOutput()

    let project =
        Path.GetFullPath(
            (options.TryFind "--project"
             |> Option.bind List.tryHead
             |> Option.defaultValue "."),
            cwd
        )

    let configuration = File.ReadAllText(Path.Combine(project, "morphir.toml"))

    match
        tomlValue configuration "name"
        |> Option.map strings,
        tomlValue configuration "exposed_modules"
        |> Option.map strings,
        options.TryFind "--ir-version",
        sources options cwd
    with
    | Some [ package ], Some exposed, Some [ version ], Ok(kind, root, documents) when
        options.ContainsKey "--json-lines"
        ->
        eprintfn "fake-morphir: compiling %d document(s) at IR version %s" documents.Length version

        let input = {
            Package = package
            ExposedModules = exposed
            Documents = [
                for uri in documents ->
                    {
                        Uri = uri
                        Text = File.ReadAllText(Path.GetFullPath(uri, root))
                    }
            ]
            InputKind = kind
            IrVersion = version
        }

        let observed =
            (CorpusTests.adapter.Observe input CancellationToken.None)
                .GetAwaiter()
                .GetResult()

        let observation =
            match observed with
            | Ok observation -> observation
            | Error failure ->
                failure.Partial
                |> Option.defaultValue
                    {
                        Success = false
                        ModelJson = None
                        Diagnostics = []
                    }

        // The report root is one directory above the documents, and absolute.
        let reportRoot =
            Path.GetDirectoryName root
            |> nonNull

        let prefix =
            Path.GetFileName root
            + "/"

        let output =
            observation.ModelJson
            |> Option.map (fun model ->
                let path =
                    Path.Combine(
                        project,
                        "elsewhere",
                        Guid.NewGuid().ToString "N"
                        + ".ir.json"
                    )

                Directory.CreateDirectory(
                    Path.GetDirectoryName path
                    |> nonNull
                )
                |> ignore

                File.WriteAllText(path, model)
                path
            )

        let exitCode = if observation.Success then 0 else 1

        let diagnostic (writer: Utf8JsonWriter) (key: DiagnosticKey) =
            writer.WriteStartObject()
            writer.WriteString("severity", key.Severity)

            if
                key.Code
                <> ""
            then
                writer.WriteString("code", key.Code)

            writer.WriteString("message", "A fake message.")
            writer.WriteString("unknownMember", "ignored")

            if
                key.Uri
                <> ""
            then
                writer.WriteStartObject("location")

                writer.WriteString(
                    "uri",
                    prefix
                    + key.Uri
                )

                writer.WriteStartObject("range")
                writer.WriteStartObject("start")
                writer.WriteNumber("line", key.StartLine)
                writer.WriteNumber("character", key.StartCharacter)
                writer.WriteEndObject()
                writer.WriteStartObject("end")
                writer.WriteNumber("line", key.EndLine)
                writer.WriteNumber("character", key.EndCharacter)
                writer.WriteEndObject()
                writer.WriteEndObject()
                writer.WriteEndObject()

            writer.WriteEndObject()

        let report (reportedExit: int) (writer: Utf8JsonWriter) =
            writer.WriteString("event", "completed")
            writer.WriteStartObject("report")
            writer.WriteString("unknownFirst", "ignored")
            writer.WriteBoolean("success", observation.Success)
            writer.WriteNumber("exitCode", reportedExit)
            writer.WriteStartObject("sources")
            writer.WriteNull("projectFile")
            writer.WriteString("root", reportRoot)
            writer.WriteStartArray("documents")

            for document in documents do
                writer.WriteStringValue(
                    prefix
                    + document
                )

            writer.WriteEndArray()
            writer.WriteEndObject()

            match output with
            | Some path -> writer.WriteString("output", path)
            | None -> writer.WriteNull("output")

            writer.WriteStartArray("diagnostics")

            for key in observation.Diagnostics do
                diagnostic writer key

            writer.WriteEndArray()
            writer.WriteEndObject()

        if mode = "not-json" then
            stdout.Write("compiling...\n"B)

        writeRecord
            stdout
            (fun writer ->
                writer.WriteString("event", "fakeProgress")
                writer.WriteNumber("percent", 10)
            )

        writeRecord
            stdout
            (fun writer ->
                writer.WriteString("event", "stageStarted")
                writer.WriteString("stage", "fakeStage")
            )

        if not observation.Diagnostics.IsEmpty then
            writeRecord
                stdout
                (fun writer ->
                    writer.WriteString("event", "diagnostics")
                    writer.WriteString("stage", "fakeStage")
                    writer.WriteStartArray("diagnostics")

                    for key in observation.Diagnostics do
                        diagnostic writer key

                    writer.WriteEndArray()
                )

        match mode with
        | "no-completed" -> ()
        | "two-completed" ->
            writeRecord stdout (report exitCode)
            writeRecord stdout (report exitCode)
        | "exit-mismatch" ->
            writeRecord
                stdout
                (report (
                    exitCode
                    + 1
                ))
        | _ -> writeRecord stdout (report exitCode)

        if mode = "completed-not-last" then
            writeRecord stdout (fun writer -> writer.WriteString("event", "stageCompleted"))

        exitCode
    | _, _, _, Error message ->
        eprintfn "fake-morphir: %s" message
        2
    | _ ->
        eprintfn
            "fake-morphir: compile needs --json-lines, --ir-version and project.name and project.exposed_modules in morphir.toml."

        2

/// <summary>Runs the fake with the arguments after <see cref="Argument"/>.</summary>
let run (args: string array) =
    let mode =
        Environment.GetEnvironmentVariable ModeVariable
        |> Option.ofObj
        |> Option.defaultValue "normal"

    match List.ofArray args with
    | [ "--version" ] ->
        Console.Out.WriteLine Version
        0
    | "compile" :: rest ->
        match parse rest with
        | Error message ->
            eprintfn "fake-morphir: %s" message
            2
        | Ok options -> compile options Environment.CurrentDirectory mode
    | _ ->
        eprintfn "fake-morphir: only --version and compile are scripted."
        2
