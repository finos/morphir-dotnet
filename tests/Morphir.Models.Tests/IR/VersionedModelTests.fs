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
    ]
