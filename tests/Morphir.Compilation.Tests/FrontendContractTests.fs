module Morphir.Compilation.Tests.FrontendContractTests

open System
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Model

let private package =
    match CanonicalNames.parsePath "acme/demo" with
    | Ok path -> PackageName path
    | Error message -> failwith message

let private target = {
    Family = ModelFamily.V4
    Version = Versioning.Classic 4
}

let private fsharp = LanguageId.Create "fsharp"

let private document uri : SourceDocument = {
    Uri = uri
    LanguageId = fsharp
    Version = 0UL
    Text = "namespace Acme.Demo"
}

let private parse
    (language: LanguageId | null)
    (documents: SourceDocument list)
    (package: PackageName | null)
    exposed
    =
    CompileRequest.Parse(language, null, documents, package, exposed, target, "digest")

let private refused expected result =
    match result with
    | Ok _ -> failtest $"Expected the refusal {expected}"
    | Error failure -> Expect.equal failure expected "The refusal names the problem"

[<Tests>]
let tests =
    testList "Compile request (FE-1)" [
        test "A language id is a lowercase token, compared ordinally" {
            Expect.equal (LanguageId.Create "fsharp") (LanguageId.Create "fsharp") "equal ids"
            Expect.equal (LanguageId.Parse "") (Error LanguageIdFailure.Empty) "empty"
            Expect.equal (LanguageId.Parse null) (Error LanguageIdFailure.Empty) "null"

            Expect.equal
                (LanguageId.Parse "FSharp")
                (Error(LanguageIdFailure.InvalidCharacter 0))
                "uppercase"

            Expect.equal
                (LanguageId.Parse "f sharp")
                (Error(LanguageIdFailure.InvalidCharacter 1))
                "space"

            Expect.isOk (LanguageId.Parse "c-sharp.v2_x") "letters, digits, '-', '.' and '_'"
        }

        test "A request keeps the documents in order and the exposure as given" {
            let request =
                match
                    CompileRequest.Parse(
                        fsharp,
                        "/work",
                        [ document "B.fs"; document "A.fs" ],
                        package,
                        [ "Pricing" ],
                        target,
                        "digest"
                    )
                with
                | Ok request -> request
                | Error failure -> failtest $"{failure}"

            Expect.equal
                (request.Documents
                 |> Seq.map (fun document -> document.Uri)
                 |> List.ofSeq)
                [ "B.fs"; "A.fs" ]
                "Document order is compilation order"

            Expect.equal request.ExposedModules (Some [ "Pricing" ]) "The exposed modules are kept"

            match request.ExposedModulesOrNull with
            | null -> failtest "C# exposure is missing"
            | modules -> Expect.equal (List.ofSeq modules) [ "Pricing" ] "C# exposure"

            Expect.equal request.Root (Some "/work") "The root is kept"
            Expect.equal request.RootOrNull "/work" "C# root"
            Expect.equal request.Output target "The output target is kept"
            Expect.equal request.ContextDigest "digest" "The context digest is kept"
        }

        test "Absent exposure exposes every module, and an empty list exposes none" {
            let request exposed =
                match parse fsharp [ document "A.fs" ] package exposed with
                | Ok request -> request
                | Error failure -> failtest $"{failure}"

            Expect.equal (request null).ExposedModules None "No list: every public module"
            Expect.isNull (request null).ExposedModulesOrNull "C#: null"
            Expect.isNull (request null).RootOrNull "C#: no root"
            Expect.equal (request []).ExposedModules (Some []) "An empty list: no module"
        }

        test "A missing language or package name fails before analysis" {
            parse null [ document "A.fs" ] package null
            |> refused CompileRequestFailure.MissingLanguage

            parse fsharp [ document "A.fs" ] null null
            |> refused CompileRequestFailure.MissingPackage
        }

        test "An empty document list fails before analysis" {
            parse fsharp [] package null
            |> refused CompileRequestFailure.NoDocuments
        }

        test "Every document is present and has a URI, a text and the request's language" {
            parse fsharp [ document "A.fs"; Unchecked.defaultof<_> ] package null
            |> refused (CompileRequestFailure.MissingDocument 1)

            parse fsharp [ document "A.fs"; document "" ] package null
            |> refused (CompileRequestFailure.EmptyUri 1)

            parse
                fsharp
                [
                    { document "A.fs" with
                        Text = Unchecked.defaultof<_>
                    }
                ]
                package
                null
            |> refused (CompileRequestFailure.MissingText 0)

            parse
                fsharp
                [
                    { document "A.fs" with
                        LanguageId = LanguageId.Create "elm"
                    }
                ]
                package
                null
            |> refused (CompileRequestFailure.LanguageMismatch 0)

            parse
                fsharp
                [
                    { document "A.fs" with
                        LanguageId = Unchecked.defaultof<_>
                    }
                ]
                package
                null
            |> refused (CompileRequestFailure.LanguageMismatch 0)
        }

        test "A URI appears once" {
            parse fsharp [ document "A.fs"; document "A.fs" ] package null
            |> refused (CompileRequestFailure.RepeatedUri "A.fs")
        }

        test "TryParse sets the request or the failure" {
            let mutable request = Unchecked.defaultof<CompileRequest>
            let mutable failure = Unchecked.defaultof<CompileRequestFailure>

            Expect.isTrue
                (CompileRequest.TryParse(
                    fsharp,
                    null,
                    [ document "A.fs" ],
                    package,
                    null,
                    target,
                    "",
                    &request,
                    &failure
                ))
                "valid"

            Expect.isNotNull (box request) "the request is set"
            Expect.isNull (box failure) "no failure"

            Expect.isFalse
                (CompileRequest.TryParse(
                    fsharp,
                    null,
                    [],
                    package,
                    null,
                    target,
                    "",
                    &request,
                    &failure
                ))
                "invalid"

            Expect.isNull (box request) "no request"
            Expect.equal failure CompileRequestFailure.NoDocuments "the failure is set"
        }

        test "Create names the invalid parameter" {
            let parameterOf (create: unit -> CompileRequest) =
                try
                    create ()
                    |> ignore

                    ""
                with :? ArgumentException as error ->
                    string error.ParamName

            Expect.equal
                (parameterOf (fun () ->
                    CompileRequest.Create(
                        Unchecked.defaultof<_>,
                        null,
                        [ document "A.fs" ],
                        package,
                        null,
                        target,
                        ""
                    )
                ))
                "languageId"
                "language"

            Expect.equal
                (parameterOf (fun () ->
                    CompileRequest.Create(
                        fsharp,
                        null,
                        [ document "A.fs" ],
                        Unchecked.defaultof<_>,
                        null,
                        target,
                        ""
                    )
                ))
                "package"
                "package"

            Expect.equal
                (parameterOf (fun () ->
                    CompileRequest.Create(fsharp, null, [], package, null, target, "")
                ))
                "documents"
                "documents"
        }

        test "Every refusal has a message" {
            for failure in
                [
                    CompileRequestFailure.MissingLanguage
                    CompileRequestFailure.MissingPackage
                    CompileRequestFailure.NoDocuments
                    CompileRequestFailure.MissingDocument 0
                    CompileRequestFailure.EmptyUri 0
                    CompileRequestFailure.MissingText 0
                    CompileRequestFailure.LanguageMismatch 0
                    CompileRequestFailure.RepeatedUri "A.fs"
                ] do
                Expect.isNotEmpty (failure.ToString()) "The refusal can be shown"
        }

        test "A frontend outcome is built and matched without a type test" {
            let failure = {
                Message = "The process stopped."
                Diagnostics = [||]
            }

            let text =
                (FrontendOutcome.Fail failure)
                    .Match(
                        Func<_, _>(fun _ -> "completed"),
                        Func<_, _>(fun (failure: FrontendFailure) -> failure.Message)
                    )

            Expect.equal text "The process stopped." "The failure case is selected"
            Expect.equal FrontendTransport.InProcess.WireValue "in-process" "transport text"
        }
    ]
