// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Analysis

// DocumentSource is marked experimental in FSharp.Compiler.Service. It is the only way to give the checker
// in-memory sources without replacing the process-wide file system. The FCS version is pinned exactly, and
// the frontend tests run again on every compiler upgrade (FSF-5).
#nowarn "57"

open System
open System.Collections.Concurrent
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Syntax
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open Morphir.Compilation
open Morphir.Frontend.FSharp

/// <summary>Which check of a session: the original documents, or the analysis-only copy (FSF-2).</summary>
[<RequireQualifiedAccess>]
type internal CheckKind =
    | Original
    | Analysis

    member this.Folder =
        match this with
        | Original -> "original"
        | Analysis -> "analysis"

/// <summary>One checked document: the caller's URI and text, its virtual path and its syntax tree.</summary>
type internal CheckedDocument = {
    Index: int
    Uri: string
    Text: string
    Lines: LineIndex
    Path: string
    Syntax: ParsedInput
}

/// <summary>One checked project: the documents in compilation order and the compiler results.</summary>
type internal CheckedProject =
    {
        Kind: CheckKind
        Documents: CheckedDocument list
        Results: FSharpCheckProjectResults
        Uses: FSharpSymbolUse array
    }

    /// <summary>The document at a virtual path, if the path belongs to this project.</summary>
    member this.TryDocument(path: string) =
        this.Documents
        |> List.tryFind (fun document ->
            String.Equals(document.Path, path, StringComparison.Ordinal)
        )

/// <summary>
/// The hermetic F# compiler service edge (FSF-5). The project options are made in memory from the ordered
/// documents; the frontend never reads a project file. The references are the hosting runtime's framework
/// assemblies, the FSharp.Core that the host loaded, and the Morphir F# annotations. Sources reach the compiler through an in-memory
/// document source under a virtual root, so no host path reaches a range, a message or a model.
/// </summary>
[<RequireQualifiedAccess>]
module internal CompilerEdge =

    /// <summary>The revision of the analysis-copy transform (FSF-2). Change it when the transform changes.</summary>
    [<Literal>]
    let EvidenceRevision = 1

    /// <summary>The fixed F# language version.</summary>
    [<Literal>]
    let LanguageVersion = "10.0"

    let private separator = string Path.DirectorySeparatorChar

    /// <summary>The virtual root. It is absolute on every platform and is never a real directory.</summary>
    let virtualRoot =
        if OperatingSystem.IsWindows() then
            @"Z:\morphir-virtual\"
        else
            "/morphir-virtual/"

    let private sources =
        ConcurrentDictionary<string, ISourceText>(StringComparer.Ordinal)

    let private checker =
        lazy
            (FSharpChecker.Create(
                keepAssemblyContents = true,
                keepAllBackgroundResolutions = true,
                documentSource =
                    DocumentSource.Custom(fun path -> async {
                        match sources.TryGetValue path with
                        | true, text -> return Some text
                        | _ -> return None
                    })
            ))

    let private version (assembly: Assembly) =
        match assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> string (assembly.GetName().Version)
        | attribute -> attribute.InformationalVersion.Split('+')[0]

    /// <summary>
    /// The frontend identity: the compiler service, FSharp.Core, runtime and evidence-transform revisions.
    /// </summary>
    let identity =
        lazy
            (let compiler = version typeof<FSharpChecker>.Assembly
             let core = version typeof<unit>.Assembly
             let annotations = version typeof<Morphir.MorphirNameAttribute>.Assembly

             $"FSharp.Compiler.Service {compiler}; FSharp.Core {core}; Morphir.FSharp.Annotations {annotations}; {RuntimeInformation.FrameworkDescription}; evidence {EvidenceRevision}")

    /// <summary>
    /// The file names of the framework's runtime assemblies (Microsoft.NETCore.App), from the texts of deps files.
    /// A self-contained application lists them under its runtime pack
    /// (<c>runtimepack.Microsoft.NETCore.App.Runtime.&lt;rid&gt;</c>); the shared framework of a framework-dependent
    /// application lists them in its own deps file (<c>Microsoft.NETCore.App.Runtime.&lt;rid&gt;</c>). An
    /// application deps file alone names none.
    /// </summary>
    let frameworkAssemblies (depsFiles: string seq) : Set<string> =
        let isFramework (library: string) =
            library.StartsWith("runtimepack.Microsoft.NETCore.App.Runtime.", StringComparison.OrdinalIgnoreCase)
            || library.StartsWith("Microsoft.NETCore.App.Runtime.", StringComparison.OrdinalIgnoreCase)

        set [
            for text in depsFiles do
                use document = JsonDocument.Parse text

                match document.RootElement.TryGetProperty "targets" with
                | true, targets when targets.ValueKind = JsonValueKind.Object ->
                    for target in targets.EnumerateObject() do
                        if target.Value.ValueKind = JsonValueKind.Object then
                            for library in target.Value.EnumerateObject() do
                                match library.Value.TryGetProperty "runtime" with
                                | true, runtime when isFramework library.Name && runtime.ValueKind = JsonValueKind.Object ->
                                    for asset in runtime.EnumerateObject() do
                                        yield asset.Name.Substring(asset.Name.LastIndexOf '/' + 1)
                                | _ -> ()
                | _ -> ()
        ]

    /// <summary>The texts of the deps files that the host loaded (APP_CONTEXT_DEPS_FILES), or none.</summary>
    let depsFiles () : string list =
        match AppContext.GetData "APP_CONTEXT_DEPS_FILES" with
        | :? string as paths ->
            [
                for path in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) do
                    if File.Exists path then
                        yield File.ReadAllText path
            ]
        | _ -> []

    /// <summary>What the reference selection reads from the hosting process.</summary>
    type ReferenceLayout = {
        /// <summary>The runtime directory: the shared framework folder, or the application folder when self-contained.</summary>
        RuntimeDirectory: string
        /// <summary>The application folder.</summary>
        ApplicationDirectory: string
        /// <summary>The trusted platform assemblies, as full paths.</summary>
        Trusted: string list
        /// <summary>The file names of the framework's runtime assemblies; empty when no deps file names them.</summary>
        Framework: Set<string>
        /// <summary>The location of the FSharp.Core that the host loaded.</summary>
        FSharpCore: string
        /// <summary>The location of the Morphir F# annotations.</summary>
        Annotations: string
    }

    /// <summary>
    /// The compiler references: the trusted framework assemblies of the runtime directory in ordinal path order, then
    /// FSharp.Core and the Morphir attributes. The framework assemblies are the trusted assemblies in the runtime
    /// directory, except FSharp.*, that the framework list names. For a framework-dependent host every such assembly
    /// is a framework assembly, so the list changes nothing. For a self-contained folder the runtime directory is
    /// the application folder, and the list keeps the application's own assemblies (Morphir.*, the compiler service)
    /// out. A self-contained folder without a framework list, and an assembly without a location (a single-file
    /// bundle), are refused rather than given a guessed reference set.
    /// </summary>
    let selectReferences (layout: ReferenceLayout) : Result<string array, string> =
        let trim (path: string) = Path.TrimEndingDirectorySeparator path

        let sameDirectory (left: string) (right: string) =
            String.Equals(trim left, trim right, StringComparison.OrdinalIgnoreCase)

        if String.IsNullOrEmpty layout.FSharpCore || String.IsNullOrEmpty layout.Annotations then
            Error
                "FSharp.Core or the Morphir F# annotations have no file location, as in a single-file bundle; the F# frontend needs its references as files."
        elif Set.isEmpty layout.Framework && sameDirectory layout.RuntimeDirectory layout.ApplicationDirectory then
            Error
                "The runtime directory is the application folder (a self-contained application), and no deps file lists the framework assemblies, so the framework references cannot be told from the application's own assemblies."
        else
            let framework =
                layout.Trusted
                |> List.filter (fun path ->
                    match Path.GetDirectoryName path, Path.GetFileName path with
                    | null, _
                    | _, null -> false
                    | directory, name ->
                        sameDirectory directory layout.RuntimeDirectory
                        && not (name.StartsWith("FSharp.", StringComparison.OrdinalIgnoreCase))
                        && (Set.isEmpty layout.Framework || layout.Framework.Contains name))
                |> List.sortWith (fun left right -> String.CompareOrdinal(left, right))

            // FSharp.Core and the Morphir attributes (MorphirNameAttribute) are the only non-framework references.
            Ok(Array.ofList (framework @ [ layout.FSharpCore; layout.Annotations ]))

    /// <summary>The compiler references of this process (see <c>selectReferences</c>).</summary>
    let references =
        lazy
            (let trusted =
                match AppContext.GetData "TRUSTED_PLATFORM_ASSEMBLIES" with
                | :? string as paths ->
                    paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
                | _ -> invalidOp "The host runtime does not list its trusted platform assemblies."

             let layout = {
                 RuntimeDirectory = RuntimeEnvironment.GetRuntimeDirectory()
                 ApplicationDirectory = AppContext.BaseDirectory
                 Trusted = trusted
                 Framework = frameworkAssemblies (depsFiles ())
                 FSharpCore = typeof<unit>.Assembly.Location
                 Annotations = typeof<Morphir.MorphirNameAttribute>.Assembly.Location
             }

             match selectReferences layout with
             | Ok references -> references
             | Error message -> invalidOp message)

    let private options = [|
        "--targetprofile:netcore"
        "--target:library"
        "--noframework"
        "--simpleresolution"
        "--optimize-"
        "--debug-"
        "--deterministic+"
        "--warn:3"
        $"--langversion:{LanguageVersion}"
    |]

    let private fileName index (uri: string) =
        let name =
            match uri.LastIndexOfAny [| '/'; '\\' |] with
            | -1 -> uri
            | at -> uri.Substring(at + 1)

        if
            name.Length = 0
            || name.IndexOfAny(Path.GetInvalidFileNameChars())
               >= 0
        then
            $"Document{index}.fs"
        else
            name

    let private requests = ref 0L

    /// <summary>
    /// One compilation request. It owns the virtual documents of its checks and removes them, and the
    /// compiler caches for them, when disposed.
    /// </summary>
    [<Sealed>]
    type Session(documents: FSharpDocument list) =
        do
            if List.isEmpty documents then
                invalidArg (nameof documents) "A compilation needs at least one document."

        let request = Interlocked.Increment requests
        let folder = $"{virtualRoot}r{request}{separator}"
        let registered = ConcurrentBag<string>()
        let projects = ConcurrentBag<FSharpProjectOptions>()

        let pathOf (kind: CheckKind) index uri =
            Path.GetFullPath
                $"{folder}{kind.Folder}{separator}{index}{separator}{fileName index uri}"

        /// <summary>The caller's documents, in compilation order.</summary>
        member _.Documents = documents

        /// <summary>The virtual path of a document in a check of this session.</summary>
        member _.PathOf(kind: CheckKind, index: int) = pathOf kind index documents[index].Uri

        /// <summary>
        /// Replaces the request's virtual paths in compiler text: a document path becomes its URI, and any
        /// other virtual path loses its virtual folder.
        /// </summary>
        member _.Sanitize(text: string) =
            let documentPaths =
                [ CheckKind.Original; CheckKind.Analysis ]
                |> List.collect (fun kind ->
                    documents
                    |> List.mapi (fun index document ->
                        pathOf kind index document.Uri, document.Uri
                    )
                )

            let replaced =
                documentPaths
                |> List.fold
                    (fun (text: string) (path, uri) ->
                        text.Replace(path, uri, StringComparison.Ordinal)
                    )
                    text

            replaced
                .Replace(folder, "", StringComparison.Ordinal)
                .Replace(virtualRoot, "", StringComparison.Ordinal)

        /// <summary>
        /// Parses and checks the documents in order, with the given texts. The original and the analysis
        /// check use the same options and document order, but separate project identities.
        /// </summary>
        member this.Check
            (
                kind: CheckKind,
                texts: string list,
                cancellationToken: CancellationToken
            ) : Task<CheckedProject> =
            if
                texts.Length
                <> documents.Length
            then
                invalidArg (nameof texts) "A check needs one text for each document."

            task {
                cancellationToken.ThrowIfCancellationRequested()
                let checker = checker.Force()

                let documents =
                    List.mapi2
                        (fun index (document: FSharpDocument) text ->
                            let path = pathOf kind index document.Uri
                            sources[path] <- SourceText.ofString text
                            registered.Add path
                            index, document.Uri, text, path
                        )
                        documents
                        texts

                let arguments =
                    Array.concat [
                        options
                        references.Force()
                        |> Array.map (fun reference -> $"-r:{reference}")
                        documents
                        |> List.map (fun (_, _, _, path) -> path)
                        |> Array.ofList
                    ]

                let projectOptions =
                    checker.GetProjectOptionsFromCommandLineArgs(
                        Path.GetFullPath $"{folder}{kind.Folder}.fsproj",
                        arguments
                    )

                projects.Add projectOptions

                let! results =
                    Async.StartAsTask(
                        checker.ParseAndCheckProject projectOptions,
                        cancellationToken = cancellationToken
                    )

                let parsing, _ = checker.GetParsingOptionsFromProjectOptions projectOptions
                let parsed = ResizeArray()

                for index, uri, text, path in documents do
                    let! parse =
                        Async.StartAsTask(
                            checker.ParseFile(path, SourceText.ofString text, parsing),
                            cancellationToken = cancellationToken
                        )

                    parsed.Add
                        {
                            Index = index
                            Uri = uri
                            Text = text
                            Lines = LineIndex text
                            Path = path
                            Syntax = parse.ParseTree
                        }

                cancellationToken.ThrowIfCancellationRequested()

                return {
                    Kind = kind
                    Documents = List.ofSeq parsed
                    Results = results
                    Uses = results.GetAllUsesOfAllSymbols(cancellationToken)
                }
            }

        interface IDisposable with
            member _.Dispose() =
                for path in registered do
                    sources.TryRemove path
                    |> ignore

                if not projects.IsEmpty then
                    checker.Force().ClearCache(projects)

    /// <summary>The zero-based diagnostic range of an F# compiler range (one-based lines).</summary>
    let rangeOf (range: range) =
        Morphir.Compilation.Range.Create(
            Morphir.Compilation.Position.Create(
                Math.Max(
                    range.StartLine
                    - 1,
                    0
                ),
                range.StartColumn
            ),
            Morphir.Compilation.Position.Create(
                Math.Max(
                    range.EndLine
                    - 1,
                    0
                ),
                range.EndColumn
            )
        )

    let private severityOf (severity: FSharpDiagnosticSeverity) =
        match severity with
        | FSharpDiagnosticSeverity.Error -> Some DiagnosticSeverity.Error
        | FSharpDiagnosticSeverity.Warning -> Some DiagnosticSeverity.Warning
        | FSharpDiagnosticSeverity.Info -> Some DiagnosticSeverity.Info
        | FSharpDiagnosticSeverity.Hidden -> None

    let private errorNumber (number: int) =
        use document = JsonDocument.Parse $"{{\"errorNumber\":{number}}}"
        document.RootElement.Clone()

    /// <summary>
    /// Orders diagnostics by document, then by position. A diagnostic with no location comes first. Ties
    /// keep a fixed order by severity, code and message, so the order never depends on the compiler.
    /// </summary>
    let order (documents: FSharpDocument list) (diagnostics: Diagnostic list) =
        let indexOf =
            documents
            |> List.mapi (fun index document -> document.Uri, index)
            |> List.distinctBy fst
            |> dict

        let key (diagnostic: Diagnostic) =
            let location =
                match diagnostic.LocationOrNull with
                | null -> struct (-1, 0, 0, 0, 0)
                | location ->
                    let index =
                        match indexOf.TryGetValue location.Uri with
                        | true, index -> index
                        | _ -> documents.Length

                    struct (index,
                            location.Range.Start.Line,
                            location.Range.Start.Character,
                            location.Range.End.Line,
                            location.Range.End.Character)

            let code =
                match diagnostic.CodeOrNull with
                | null -> ""
                | code -> code.Value

            struct (location, diagnostic.Severity.WireValue, code, diagnostic.Message)

        diagnostics
        |> List.sortWith (fun left right ->
            let struct (struct (d1, l1, c1, el1, ec1), s1, k1, m1) = key left
            let struct (struct (d2, l2, c2, el2, ec2), s2, k2, m2) = key right

            [
                compare d1 d2
                compare l1 l2
                compare c1 c2
                compare el1 el2
                compare ec1 ec2
                String.CompareOrdinal(s1, s2)
                String.CompareOrdinal(k1, k2)
                String.CompareOrdinal(m1, m2)
            ]
            |> List.tryFind ((<>) 0)
            |> Option.defaultValue 0
        )

    /// <summary>
    /// The F# compiler diagnostics of an original check, as <c>fsharp.compiler</c> diagnostics with the F#
    /// severity, message and error number, in document and position order.
    /// </summary>
    let diagnostics (session: Session) (project: CheckedProject) =
        project.Results.Diagnostics
        |> Array.distinctBy (fun diagnostic ->
            struct (diagnostic.Severity,
                    diagnostic.ErrorNumber,
                    diagnostic.Message,
                    diagnostic.FileName,
                    diagnostic.Range.StartLine,
                    diagnostic.Range.StartColumn,
                    diagnostic.Range.EndLine,
                    diagnostic.Range.EndColumn)
        )
        |> Array.choose (fun diagnostic ->
            severityOf diagnostic.Severity
            |> Option.map (fun severity ->
                let reported =
                    Diagnostic.make
                        severity
                        FSharpCodes.compiler
                        (session.Sanitize diagnostic.Message)
                    |> Diagnostic.withData (errorNumber diagnostic.ErrorNumber)

                match project.TryDocument diagnostic.FileName with
                | Some document ->
                    reported
                    |> Diagnostic.at (Location.Create(document.Uri, rangeOf diagnostic.Range))
                | None -> reported
            )
        )
        |> List.ofArray
        |> order session.Documents

    /// <summary>
    /// Checks the original documents. F# errors fail the result. Warnings and information pass with it.
    /// </summary>
    let checkOriginal
        (session: Session)
        (cancellationToken: CancellationToken)
        : Task<Checked<CheckedProject>> =
        task {
            let! project =
                session.Check(
                    CheckKind.Original,
                    session.Documents
                    |> List.map (fun document -> document.Text),
                    cancellationToken
                )

            return Checked.ofDiagnostics project (diagnostics session project)
        }
