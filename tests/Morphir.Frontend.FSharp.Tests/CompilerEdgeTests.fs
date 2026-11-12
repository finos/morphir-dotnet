// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.CompilerEdgeTests

open System
open System.IO
open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.Frontend.FSharp.Analysis
open Morphir.Frontend.FSharp.Tests.AnalysisSupport
open Morphir.TestSupport

let private rates =
    document "src/Rates.fs" "namespace Acme.Demo\nmodule Rates =\n    let dailyRate = 42m\n"

let private usesRates =
    document
        "src/Pricing.fs"
        "namespace Acme.Demo\nmodule Pricing =\n    let price (days: int) = Rates.dailyRate * decimal days\n"

/// <summary>
/// The child-process side of the working-directory test: it prints the diagnostics of a fixed input.
/// </summary>
type WorkingDirectoryProbe =
    static member Argument = "--print-analysis-diagnostics"

    static member Run() =
        let input = [
            rates
            document
                "src/Pricing.fs"
                (program [
                    "let describe (x: int) ="
                    "    match x with"
                    "    | 0 -> \"zero\""
                    "let broken = 1 + \"a\""
                ])
        ]

        Console.Out.Write(
            Checked.diagnostics (analyze input)
            |> describeAll
        )

        0

[<Tests>]
let tests =
    testList "F# compiler edge" [
        test "a later document uses an earlier document" {
            let evidence = passed (analyze [ rates; usesRates ])

            Expect.equal
                (evidence.Original.Documents
                 |> List.map (fun document -> document.Uri))
                [ "src/Rates.fs"; "src/Pricing.fs" ]
                "order"
        }

        test "reversed order is an F# error in the later-used document" {
            let diagnostics = failed (analyze [ usesRates; rates ])
            Expect.isNonEmpty diagnostics "errors"

            for diagnostic in
                diagnostics
                |> List.filter (fun diagnostic -> diagnostic.IsError) do
                Expect.equal (codeOf diagnostic) "fsharp.compiler" "code"
                Expect.equal diagnostic.LocationOrNull.Uri "src/Pricing.fs" "located in Pricing.fs"

            mustNotLeak diagnostics
        }

        test "a type error is an fsharp.compiler error with its number and a zero-based range" {
            // "1 + \"a\"" gives two F# errors (FS0001 and FS0043). The frontend keeps both, so this case uses one.
            let diagnostics =
                failed (analyze [ pricing (program [ "let total: int = \"a\"" ]) ])

            let error =
                diagnostics
                |> List.filter (fun diagnostic -> diagnostic.IsError)
                |> List.exactlyOne

            Expect.equal (codeOf error) "fsharp.compiler" "code"
            Expect.equal (errorNumber error) 1 "F# error number"
            Expect.equal error.LocationOrNull.Uri "Pricing.fs" "uri"
            Expect.equal error.LocationOrNull.Range.Start.Line 2 "zero-based line"
            Expect.equal error.LocationOrNull.Range.Start.Character 21 "the string literal"
            Expect.equal error.LocationOrNull.Range.End.Character 24 "exclusive end"
        }

        test "a range after a non-BMP character counts UTF-16 code units" {
            let diagnostics =
                failed (analyze [ pricing (program [ "let s = \"😀\" + mutable" ]) ])

            let onMutable =
                diagnostics
                |> List.filter (fun diagnostic -> errorNumber diagnostic = 10)
                |> List.exactlyOne

            // "    let s = \"" is 13 units, the emoji is 2, then "\" + " is 4: 'mutable' starts at 19.
            Expect.equal onMutable.LocationOrNull.Range.Start.Line 2 "zero-based line"

            Expect.equal
                onMutable.LocationOrNull.Range.Start.Character
                19
                "the emoji counts as two units"

            Expect.equal onMutable.LocationOrNull.Range.End.Character 26 "exclusive end"
        }

        test "an F# warning stays beside a passed result" {
            let result =
                analyze [
                    pricing (
                        program [
                            "let describe (x: int) ="
                            "    match x with"
                            "    | 0 -> \"zero\""
                        ]
                    )
                ]

            passed result
            |> ignore

            let warning =
                Checked.diagnostics result
                |> List.exactlyOne

            Expect.equal warning.Severity DiagnosticSeverity.Warning "severity"
            Expect.equal (codeOf warning) "fsharp.compiler" "code"
            Expect.equal (errorNumber warning) 25 "FS0025"
            Expect.equal warning.LocationOrNull.Range.Start.Line 3 "at the match"
        }

        test "an F# warning stays when a later stage fails the run" {
            let result =
                analyzeWith
                    unchanged
                    { EvidenceLimits.Default with
                        MaxMatches = 0
                    }
                    [
                        pricing (
                            program [
                                "let describe (x: int) ="
                                "    match x with"
                                "    | 0 -> \"zero\""
                            ]
                        )
                    ]

            let diagnostics = failed result

            Expect.equal
                (diagnostics
                 |> List.map (fun diagnostic -> codeOf diagnostic, diagnostic.Severity))
                [
                    "fsharp.unsupported_match_shape", DiagnosticSeverity.Error
                    "fsharp.compiler", DiagnosticSeverity.Warning
                ]
                "both, in position order: the match starts before its subject"
        }

        test "diagnostics are in document order, then position order" {
            let first =
                document
                    "A.fs"
                    "namespace Acme.Demo\nmodule A =\n    let b = 1 + \"b\"\n    let a = 1 + \"a\"\n"

            let second =
                document "B.fs" "namespace Acme.Demo\nmodule B =\n    let c = 1 + \"c\"\n"

            let diagnostics = failed (analyze [ first; second ])

            Expect.equal
                (diagnostics
                 |> List.map (fun diagnostic ->
                     diagnostic.LocationOrNull.Uri,
                     diagnostic.LocationOrNull.Range.Start.Line,
                     diagnostic.LocationOrNull.Range.Start.Character
                 ))
                [
                    "A.fs", 2, 14
                    "A.fs", 2, 16
                    "A.fs", 3, 14
                    "A.fs", 3, 16
                    "B.fs", 2, 14
                    "B.fs", 2, 16
                ]
                "document, then position: FS0043 on '+' before FS0001 on the string"
        }

        test "compiler and evidence diagnostics are in one document and position order" {
            let earlier =
                document
                    "A.fs"
                    (program [ "let first (xs: int list) = match xs with | [ a ] -> a | _ -> 0" ])

            let later =
                document
                    "B.fs"
                    "namespace Acme.Demo\nmodule Later =\n    let second (x: int) =\n        match x with\n        | 0 -> 1\n"

            let diagnostics = failed (analyze [ earlier; later ])

            Expect.equal
                (diagnostics
                 |> List.map (fun diagnostic -> diagnostic.LocationOrNull.Uri, codeOf diagnostic))
                [ "A.fs", "fsharp.unsupported_list_pattern"; "B.fs", "fsharp.compiler" ]
                "the refusal in A.fs before the warning in B.fs"
        }

        test
            "the original and analysis checks share options and order, with separate project identities" {
            let evidence =
                passed (
                    analyze [
                        rates
                        document
                            "src/Pricing.fs"
                            (program [ "let f (x: int) = match x with | n -> n" ])
                    ]
                )

            let original = evidence.Original.Results.ProjectContext.ProjectOptions
            let analysis = evidence.Project.Results.ProjectContext.ProjectOptions
            Expect.notEqual original.ProjectFileName analysis.ProjectFileName "separate identities"

            Expect.equal
                analysis.OtherOptions.Length
                original.OtherOptions.Length
                "same number of options"

            let withoutSources (options: string array) =
                options
                |> Array.filter (fun option ->
                    not (option.EndsWith(".fs", StringComparison.Ordinal))
                )

            Expect.sequenceEqual
                (withoutSources analysis.OtherOptions)
                (withoutSources original.OtherOptions)
                "same options"

            Expect.equal
                (analysis.SourceFiles
                 |> Array.map Path.GetFileName)
                (original.SourceFiles
                 |> Array.map Path.GetFileName)
                "same document order"

            Expect.isTrue
                (Array.forall2
                    (fun (left: string) right ->
                        left
                        <> right
                    )
                    analysis.SourceFiles
                    original.SourceFiles)
                "separate document paths"
        }

        test
            "the references are the runtime framework, the host FSharp.Core and the Morphir attributes only" {
            let evidence = passed (analyze [ rates ])
            let options = evidence.Original.Results.ProjectContext.ProjectOptions

            let references =
                options.OtherOptions
                |> Array.filter (fun option -> option.StartsWith("-r:", StringComparison.Ordinal))
                |> Array.map (fun option -> Path.GetFileName(option.Substring 3))

            Expect.contains references "System.Runtime.dll" "framework"
            Expect.contains references "FSharp.Core.dll" "FSharp.Core"
            Expect.contains references "Morphir.FSharp.Annotations.dll" "the Morphir attributes"

            for name in [ "Expecto.dll"; "FSharp.Compiler.Service.dll"; "Morphir.Compilation.dll" ] do
                Expect.isFalse
                    (Array.contains name references)
                    $"{name} is a host assembly, not a reference"
        }

        test "the framework assemblies come from the runtime pack or the shared framework entry of a deps file" {
            let selfContained =
                """{"targets":{".NETCoreApp,Version=v10.0/win-arm64":{
                     "morphir-fsharp-frontend/1.0.0":{"runtime":{"morphir-fsharp-frontend.dll":{}}},
                     "runtimepack.Microsoft.NETCore.App.Runtime.win-arm64/10.0.14":{"runtime":{"System.Runtime.dll":{},"System.Private.CoreLib.dll":{}}},
                     "Morphir.Mep/1.0.0":{"runtime":{"Morphir.Mep.dll":{}}}}}}"""

            let sharedFramework =
                """{"targets":{".NETCoreApp,Version=v10.0/linux-x64":{
                     "Microsoft.NETCore.App.Runtime.linux-x64/10.0.14":{"runtime":{"runtimes/linux-x64/lib/net10.0/System.Runtime.dll":{},"System.Collections.dll":{}}}}}}"""

            let application =
                """{"targets":{".NETCoreApp,Version=v10.0":{"Morphir.Mep/1.0.0":{"runtime":{"lib/net10.0/Morphir.Mep.dll":{}}}}}}"""

            Expect.equal
                (CompilerEdge.frameworkAssemblies [ selfContained ])
                (set [ "System.Private.CoreLib.dll"; "System.Runtime.dll" ])
                "the runtime pack of a self-contained application"

            Expect.equal
                (CompilerEdge.frameworkAssemblies [ application; sharedFramework ])
                (set [ "System.Collections.dll"; "System.Runtime.dll" ])
                "the shared framework of a framework-dependent application, by file name"

            Expect.isEmpty (CompilerEdge.frameworkAssemblies [ application ]) "an application deps file alone names no framework"
        }

        test "in a self-contained folder the references are the framework, FSharp.Core and the attributes, not the application's assemblies" {
            let folder = Path.Combine(Path.GetTempPath(), "morphir app", "frontend")
            let inFolder name = Path.Combine(folder, name)

            let layout: CompilerEdge.ReferenceLayout = {
                RuntimeDirectory = folder
                ApplicationDirectory = folder
                Trusted = [
                    "System.Runtime.dll"
                    "Morphir.Mep.dll"
                    "System.Private.CoreLib.dll"
                    "Morphir.FSharp.Annotations.dll"
                    "FSharp.Core.dll"
                    "FSharp.Compiler.Service.dll"
                    "morphir-fsharp-frontend.dll"
                    "Morphir.Compilation.dll"
                  ]
                  |> List.map inFolder
                Framework = set [ "System.Runtime.dll"; "System.Private.CoreLib.dll" ]
                FSharpCore = inFolder "FSharp.Core.dll"
                Annotations = inFolder "Morphir.FSharp.Annotations.dll"
            }

            Expect.equal
                (CompilerEdge.selectReferences layout)
                (Ok [|
                    inFolder "System.Private.CoreLib.dll"
                    inFolder "System.Runtime.dll"
                    inFolder "FSharp.Core.dll"
                    inFolder "Morphir.FSharp.Annotations.dll"
                |])
                "framework in ordinal order, then FSharp.Core and the attributes, once each"

            match CompilerEdge.selectReferences { layout with Framework = Set.empty } with
            | Error error ->
                Expect.equal error CompilerEdge.ReferenceError.NoFrameworkList "a self-contained folder without a framework list is refused"
                Expect.stringContains error.Message "framework" "the message says why"
            | Ok references -> failtest $"Without the framework list the application assemblies would be references: {references}"

            match CompilerEdge.selectReferences { layout with FSharpCore = "" } with
            | Error error ->
                Expect.equal error CompilerEdge.ReferenceError.NoFileLocation "an assembly without a location is refused"
                Expect.stringContains error.Message "single-file" "the message says why"
            | Ok references -> failtest $"An empty location is not a reference: {references}"
        }

        test "APP_CONTEXT_DEPS_FILES is split on ';' on every platform, not on the path separator" {
            // hostpolicy joins the deps files with ';' (hostpolicy_context.cpp), and DependencyContextPaths in
            // Microsoft.Extensions.DependencyModel splits on ';' too. A Unix path may contain ':'.
            let value =
                "/opt/app/morphir-fsharp-frontend.deps.json;/usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.14/Microsoft.NETCore.App.deps.json;/srv/odd:name/x.deps.json"

            Expect.equal
                (CompilerEdge.parseDepsFiles value)
                [
                    "/opt/app/morphir-fsharp-frontend.deps.json"
                    "/usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.14/Microsoft.NETCore.App.deps.json"
                    "/srv/odd:name/x.deps.json"
                ]
                "three entries, the ':' kept inside its path"

            Expect.equal (CompilerEdge.parseDepsFiles "/a.deps.json;;/b.deps.json;") [ "/a.deps.json"; "/b.deps.json" ] "empty entries are dropped"
            Expect.isEmpty (CompilerEdge.parseDepsFiles "") "an empty value lists none"
        }

        test "a deps file that the host lists but that cannot be read is an error, not a silent fallback" {
            use sandbox = TestSandbox.Create "morphir-deps-"
            let folder = Directory.CreateDirectory(sandbox.PathOf "deps").FullName

            let good = Path.Combine(folder, "app.deps.json")

            File.WriteAllText(
                good,
                """{"targets":{".NETCoreApp,Version=v10.0/win-arm64":{"runtimepack.Microsoft.NETCore.App.Runtime.win-arm64/10.0.14":{"runtime":{"System.Runtime.dll":{}}}}}}"""
            )

            let missing = Path.Combine(folder, "missing.deps.json")
            let malformed = Path.Combine(folder, "malformed.deps.json")
            File.WriteAllText(malformed, "{ not json")

            Expect.equal (CompilerEdge.hostFramework (box good)) (Ok(set [ "System.Runtime.dll" ])) "a readable deps file"
            Expect.equal (CompilerEdge.hostFramework null) (Ok Set.empty) "no listed deps files: no framework list"

            for path in [ missing; malformed ] do
                match CompilerEdge.hostFramework (box $"{good};{path}") with
                | Error(CompilerEdge.ReferenceError.UnreadableDepsFile(named, reason) as error) ->
                    Expect.equal named path "the error names the file"
                    Expect.isNotEmpty reason "and the reason"
                    Expect.stringContains error.Message path "the message names the file"
                | other -> failtest $"{path}: {other}"
        }

        test "a reference failure is an fsharp.references_unavailable error diagnostic, not an exception" {
            use session = new CompilerEdge.Session([ rates ])

            for error in [
                CompilerEdge.ReferenceError.NoFileLocation
                CompilerEdge.ReferenceError.NoFrameworkList
                CompilerEdge.ReferenceError.NoTrustedAssemblies
                CompilerEdge.ReferenceError.UnreadableDepsFile("/opt/app/app.deps.json", "not found")
            ] do
                let checkedProject =
                    (CompilerEdge.checkOriginalWith (Error error) session CancellationToken.None).GetAwaiter().GetResult()

                Expect.isNone (Checked.tryValue checkedProject) $"{error.Message}: the check fails"

                match Checked.diagnostics checkedProject with
                | [ diagnostic ] ->
                    Expect.equal (codeOf diagnostic) "fsharp.references_unavailable" "the code"
                    Expect.equal diagnostic.Severity DiagnosticSeverity.Error "an error"
                    Expect.equal diagnostic.Message error.Message "the message"
                | other -> failtest $"one diagnostic: {other}"
        }

        test "for a framework-dependent host the selection is the same as the runtime-directory rule" {
            let shared = Path.Combine(Path.GetTempPath(), "dotnet", "shared", "Microsoft.NETCore.App", "10.0.14")
            let application = Path.Combine(Path.GetTempPath(), "tools", "frontend")

            let layout: CompilerEdge.ReferenceLayout = {
                RuntimeDirectory = shared
                ApplicationDirectory = application
                Trusted = [
                    Path.Combine(application, "Morphir.Mep.dll")
                    Path.Combine(shared, "System.Runtime.dll")
                    Path.Combine(application, "FSharp.Core.dll")
                    Path.Combine(shared, "System.Collections.dll")
                ]
                Framework = set [ "System.Runtime.dll"; "System.Collections.dll" ]
                FSharpCore = Path.Combine(application, "FSharp.Core.dll")
                Annotations = Path.Combine(application, "Morphir.FSharp.Annotations.dll")
            }

            let expected = [|
                Path.Combine(shared, "System.Collections.dll")
                Path.Combine(shared, "System.Runtime.dll")
                Path.Combine(application, "FSharp.Core.dll")
                Path.Combine(application, "Morphir.FSharp.Annotations.dll")
            |]

            Expect.equal (CompilerEdge.selectReferences layout) (Ok expected) "with the framework list"

            Expect.equal
                (CompilerEdge.selectReferences { layout with Framework = Set.empty })
                (Ok expected)
                "without one, as before"
        }

        test "in this framework-dependent test host the references are the runtime-directory rule of earlier releases" {
            // The rule before self-contained support: every trusted assembly in the runtime directory except
            // FSharp.*, in ordinal order, then FSharp.Core and the Morphir attributes.
            let runtime = Path.TrimEndingDirectorySeparator(Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory())

            let earlier =
                Array.append
                    ((AppContext.GetData "TRUSTED_PLATFORM_ASSEMBLIES" :?> string)
                        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                     |> Array.filter (fun path ->
                         String.Equals(
                             Path.TrimEndingDirectorySeparator(Path.GetDirectoryName path |> nonNull),
                             runtime,
                             StringComparison.OrdinalIgnoreCase
                         )
                         && not ((Path.GetFileName path |> nonNull).StartsWith("FSharp.", StringComparison.OrdinalIgnoreCase)))
                     |> Array.sortWith (fun left right -> String.CompareOrdinal(left, right)))
                    [| typeof<unit>.Assembly.Location; typeof<Morphir.MorphirNameAttribute>.Assembly.Location |]

            match CompilerEdge.hostFramework (AppContext.GetData "APP_CONTEXT_DEPS_FILES") with
            | Ok framework -> Expect.isNonEmpty framework "the host deps files name the framework"
            | Error error -> failtest error.Message

            Expect.equal (CompilerEdge.references.Force()) (Ok earlier) "the same references, in the same order"
        }

        test "the frontend identity names the compiler, core, runtime and evidence revisions" {
            let identity = CompilerEdge.identity.Force()
            Expect.stringContains identity "FSharp.Compiler.Service 43.12.403" "compiler service"
            Expect.stringContains identity "FSharp.Core 10.1.403" "core"
            Expect.stringContains identity "Morphir.FSharp.Annotations" "annotations"
            Expect.stringContains identity ".NET" "runtime"

            Expect.stringContains
                identity
                $"evidence {CompilerEdge.EvidenceRevision}"
                "evidence transform"
        }

        test "sanitizing replaces virtual paths with document URIs" {
            use session = new CompilerEdge.Session([ rates; usesRates ])

            for kind in [ CheckKind.Original; CheckKind.Analysis ] do
                Expect.equal
                    (session.Sanitize $"in {session.PathOf(kind, 1)}")
                    "in src/Pricing.fs"
                    "document URI"

            Expect.equal
                (session.Sanitize $"see {CompilerEdge.virtualRoot}elsewhere")
                "see elsewhere"
                "virtual root"
        }

        test "a cancelled request stops" {
            use cancelled = new CancellationTokenSource()
            cancelled.Cancel()

            let run () =
                (FSharpAnalysis.analyze EvidenceLimits.Default [ rates ] cancelled.Token)
                    .GetAwaiter()
                    .GetResult()
                |> ignore

            Expect.throwsT<OperationCanceledException> run "cancelled"
        }

        testTask "runs in different working directories give byte-identical diagnostics" {
            use first = TestSandbox.Create("morphir-fsharp-cwd-a-")
            use second = TestSandbox.Create("morphir-fsharp-cwd-b-")

            // Each run is a child process with its own working directory. Tests never change the
            // working directory of the test process.
            let run (sandbox: TestSandbox) = task {
                let start =
                    ChildProcess.StartInfo(
                        (match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
                         | null -> "dotnet"
                         | host -> host),
                        "exec",
                        typeof<WorkingDirectoryProbe>.Assembly.Location,
                        WorkingDirectoryProbe.Argument
                    )

                start.WorkingDirectory <- sandbox.Work
                sandbox.Apply start
                let! result = ChildProcess.RunAsync(start, TimeSpan.FromMinutes 3.0)
                Expect.equal result.ExitCode 0 result.CombinedOutput
                return result.StandardOutput
            }

            let! left = run first
            let! right = run second
            Expect.stringContains left "fsharp.compiler" "diagnostics"
            Expect.equal left right "byte-identical"
            Expect.isFalse (left.Contains first.Work) "no working directory"
            Expect.isFalse (left.Contains "morphir-virtual") "no virtual path"
        }
    ]
    |> testLabel "PlatformSpecific"
