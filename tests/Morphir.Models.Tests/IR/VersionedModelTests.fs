module Morphir.IR.Tests.VersionedModelTests

open System.Text.Json
open Expecto
open Morphir.IR
open Morphir.IR.Model

let private ok =
    function
    | Ok value -> value
    | Error error -> failtestf "%A" error

let private classicJson =
    """{"formatVersion":3,"distribution":["Library",[["example"]],[],{"modules":[]}]}"""

[<Tests>]
let tests =
    testList "Versioned semantic model" [
        testCase "a source format version prints its exact declaration"
        <| fun () ->
            for text in [ "3"; "3.0.0"; "4.1.0-draft.1" ] do
                match SourceFormatVersion.parse text with
                | Ok version -> Expect.equal (string version) text "declared text"
                | Error error -> failtest $"refused {text}: {error}"
        testCase "TryParse gives C# callers the declared version and no failure"
        <| fun () ->
            for text in [ "3"; "3.0.0"; "4.1.0-draft.1" ] do
                let parsed, version, failure = SourceFormatVersion.TryParse text
                Expect.isTrue parsed $"parsed {text}"
                Expect.equal version.Text text "declared text"
                Expect.equal version.Version ((SourceFormatVersion.parse text |> ok).Version) "parsed version"
                Expect.isTrue (obj.ReferenceEquals(failure, null)) "no failure"
        testCase "TryParse gives C# callers a failure for null, empty and malformed text"
        <| fun () ->
            for text in [ null; ""; "v4"; "4.x" ] do
                let parsed, version, failure = SourceFormatVersion.TryParse text
                Expect.isFalse parsed $"refused {text}"
                Expect.isTrue (obj.ReferenceEquals(version, null)) "no version"
                Expect.isFalse (obj.ReferenceEquals(failure, null)) "a failure"
        testCase "Create builds a literal version and throws for malformed text"
        <| fun () ->
            Expect.equal (SourceFormatVersion.Create "4").Text "4" "declared text"
            Expect.equal (SourceFormatVersion.Create "4.1.0-draft.1").Version ((SourceFormatVersion.parse "4.1.0-draft.1" |> ok).Version) "parsed version"
            Expect.throwsT<System.ArgumentException> (fun () -> SourceFormatVersion.Create "4.x" |> ignore) "malformed text"
        testCase "classic integer and semantic versions retain separate source context"
        <| fun () ->
            use document = JsonDocument.Parse classicJson

            let distribution =
                (Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement
                 |> ok)
                    .Distribution

            for text in [ "3"; "3.0.0"; "3.1.0" ] do
                let model = {
                    SourceVersion =
                        SourceFormatVersion.parse text
                        |> ok
                    Distribution = DistributionModel.Classic distribution
                }

                Expect.equal model.SourceVersion.Text text "exact source version"
                Expect.equal model.Family ModelFamily.Classic "semantic family"
                Expect.equal model.Kind DistributionKind.Library "distribution identity"
        testCase "v4 kinds do not become classic libraries"
        <| fun () ->
            for kind in [ "Library"; "Specs"; "Application" ] do
                let payload =
                    match kind with
                    | "Library" -> "\"def\":{\"modules\":{}}"
                    | "Specs" -> "\"spec\":{\"modules\":{}}"
                    | _ -> "\"def\":{\"modules\":{}},\"entryPoints\":{}"

                use document =
                    JsonDocument.Parse(
                        "{\"formatVersion\":4,\"distribution\":{\""
                        + kind
                        + "\":{\"packageName\":\"example\",\"dependencies\":{},"
                        + payload
                        + "}}}"
                    )

                let distribution =
                    (Morphir.Json.V4.V4Json.readDistribution document.RootElement
                     |> ok)
                        .Distribution

                let model = {
                    SourceVersion =
                        SourceFormatVersion.parse "4.0.0"
                        |> ok
                    Distribution = DistributionModel.V4 distribution
                }

                Expect.equal model.Family ModelFamily.V4 "v4 semantic family"
                Expect.equal (string model.Kind) kind "preserve kind"
        testCase "bad source version is refused rather than defaulted"
        <| fun () ->
            for text in [ ""; "latest"; "-1" ] do
                match SourceFormatVersion.parse text with
                | Ok _ -> failtest "invalid version accepted"
                | Error _ -> ()

        test "Families and kinds name themselves for C# callers, as reports write them" {
            Expect.equal [ ModelFamily.Classic.WireValue; ModelFamily.V4.WireValue ] [ "Classic"; "V4" ] "families"

            Expect.equal
                [ DistributionKind.Library.WireValue; DistributionKind.Specs.WireValue; DistributionKind.Application.WireValue ]
                [ "Library"; "Specs"; "Application" ]
                "kinds"

            Expect.equal (string ModelFamily.V4) "V4" "ToString is the wire value"
            Expect.equal (string DistributionKind.Specs) "Specs" "ToString is the wire value"
        }
    ]
