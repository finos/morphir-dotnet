// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.RegistrationTests

open System
open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.Frontend.FSharp
open Morphir.Frontend.FSharp.Tests.AnalysisSupport
open Morphir.IR
open Morphir.IR.Model

let private frontend = FSharpFrontend.registration

let private package =
    match CanonicalNames.parsePath "acme/demo" with
    | Ok path -> PackageName path
    | Error message -> failwith message

let private target family version = { Family = family; Version = version }

let private source uri text : Morphir.Compilation.SourceDocument = {
    Uri = uri
    LanguageId = FSharpFrontend.languageId
    Version = 0UL
    Text = text
}

let private request language output (documents: Morphir.Compilation.SourceDocument list) =
    let language = LanguageId.Create language

    let documents =
        documents
        |> List.map (fun document -> { document with LanguageId = language })

    match CompileRequest.Parse(language, null, documents, package, null, output, "") with
    | Ok request -> request
    | Error failure -> failtest $"{failure}"

let private compile (request: CompileRequest) =
    frontend.CompileAsync(request, CancellationToken.None).GetAwaiter().GetResult()

let private completed outcome =
    match outcome with
    | FrontendOutcome.Completed result -> result
    | FrontendOutcome.Failed failure -> failtest $"Expected a completed run, got: {failure.Message}"

let private operational outcome =
    match outcome with
    | FrontendOutcome.Failed failure -> failure
    | FrontendOutcome.Completed result -> failtest $"Expected an operational failure, got: {result}"

let private rates =
    source "Rates.fs" "namespace Acme.Demo\nmodule Rates =\n    let dailyRate : int = 40\n"

let private pricing =
    source
        "Pricing.fs"
        "namespace Acme.Demo\nmodule Pricing =\n    let total (days: int) : int = days * Rates.dailyRate\n"

let private every = [
    target ModelFamily.Classic (Versioning.Classic 1)
    target ModelFamily.Classic (Versioning.Classic 2)
    target ModelFamily.Classic (Versioning.Classic 3)
    target ModelFamily.V4 (Versioning.Classic 4)
]

[<Tests>]
let tests =
    testList "F# frontend registration (FE-1)" [
        test "The registration advertises F#, every writer version and several documents" {
            let capability = frontend.Capability

            Expect.equal frontend.Identity (ComponentId.Create "morphir-dotnet.fsharp") "identity"
            Expect.equal frontend.Transport FrontendTransport.InProcess "transport"

            Expect.equal
                [
                    for language in capability.Languages ->
                        language.Id.Value, List.ofSeq language.FileExtensions
                ]
                [ "fsharp", [ ".fs"; ".fsi" ] ]
                "languages"

            Expect.equal (List.ofSeq capability.Outputs) every "one target per writer version"
            Expect.isTrue capability.MultiDocument "several documents per request"
            Expect.isFalse capability.Incremental "no incremental compilation"
        }

        test "Ordered documents compile to the requested family for every advertised target" {
            for output in every do
                let result = compile (request "fsharp" output [ rates; pricing ])
                let compiled = passed (completed result)

                Expect.equal compiled.Model.Family output.Family $"family for {output}"

                Expect.equal
                    (List.ofSeq compiled.Modules)
                    [ "Rates"; "Pricing" ]
                    $"modules in compilation order for {output}"
        }
        |> testLabel "PlatformSpecific"

        test "A source problem completes with a failed result and no model" {
            let result =
                compile (
                    request "fsharp" every[3] [
                        source
                            "Pricing.fs"
                            "namespace Acme.Demo\nmodule Pricing =\n    let mutable total = 0\n"
                    ]
                )

            let diagnostics = failed (completed result)

            Expect.equal
                (diagnostics
                 |> List.map codeOf)
                [ "fsharp.unsupported_mutable" ]
                "the refusal"
        }
        |> testLabel "PlatformSpecific"

        test "An F# warning travels with a successful result" {
            let result =
                compile (
                    request "fsharp" every[3] [
                        source
                            "Pricing.fs"
                            "namespace Acme.Demo\nmodule Pricing =\n    let describe (x: int) =\n        match x with\n        | 0 -> \"zero\"\n"
                    ]
                )

            match completed result with
            | Passed(_, diagnostics) ->
                Expect.equal
                    (diagnostics
                     |> List.map (fun diagnostic -> diagnostic.Severity, codeOf diagnostic))
                    [ DiagnosticSeverity.Warning, "fsharp.compiler" ]
                    "FS0025 stays beside the model"
            | Failed diagnostics -> failtest (describeAll diagnostics)
        }
        |> testLabel "PlatformSpecific"

        test "Another language is an operational failure that names the language" {
            let failure = operational (compile (request "elm" every[3] [ rates ]))
            Expect.stringContains failure.Message "elm" "names the language"
        }

        test "A target that is not advertised is an operational failure that names the target" {
            let draft =
                match Versioning.parse "4.1.0-draft.1" with
                | Ok version -> target ModelFamily.V4 version
                | Error error -> failtest $"{error}"

            let failure = operational (compile (request "fsharp" draft [ rates ]))
            Expect.stringContains failure.Message "4.1.0-draft.1" "names the target"
        }

        test "Cancellation ends the run" {
            use cancellation = new CancellationTokenSource()
            cancellation.Cancel()

            Expect.throwsT<OperationCanceledException>
                (fun () ->
                    frontend
                        .CompileAsync(request "fsharp" every[3] [ rates ], cancellation.Token)
                        .GetAwaiter()
                        .GetResult()
                    |> ignore
                )
                "cancellation is not a frontend failure"
        }
    ]
