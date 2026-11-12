// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.MepLineTests

open Expecto
open Morphir.Mep
open Morphir.Extensions.Installation

[<Tests>]
let tests =
    testList "MepLine" [
        for text in [ "0.2.0-0"; "0.2.0-draft.1"; "0.2.0-alpha.9"; "0.2.0-rc.1"; "0.2.0"; "0.2.7" ] do
            testCase $"supports {text}"
            <| fun () ->
                Expect.isTrue
                    (MepSupport.IsSupported(ProtocolVersion.Create text))
                    "npm prerelease rule"
        for text in [ "0.1"; "0.1.0"; "0.2.1-rc.1"; "0.3.0-draft.1"; "0.3.0"; "1.0.0" ] do
            testCase $"does not support {text}"
            <| fun () ->
                Expect.isFalse (MepSupport.IsSupported(ProtocolVersion.Create text)) "outside line"
        test "implemented set comes from the actual MEP lifecycle" {
            Expect.sequenceEqual
                MepSupport.ImplementedVersions
                Lifecycle.protocolVersions
                "no second implemented list"
        }
        test "a later draft is supported but not implemented" {
            let result = MepSupport.Evaluate [| ProtocolVersion.Create "0.2.0-draft.2" |]
            Expect.equal result.Status "not-implemented" "distinct reason"
            Expect.stringContains (result.ToString()) "not implemented" "plain reason"
            Expect.stringContains (result.ToString()) "0.2.0-draft.2" "offered version"
        }
        test "an unsupported line has a different reason" {
            let result = MepSupport.Evaluate [| ProtocolVersion.Create "0.3.0" |]
            Expect.equal result.Status "not-supported" "distinct reason"
            Expect.stringContains (result.ToString()) "not supported" "plain reason"
        }
        test "a mixed unavailable offer explains both support and implementation" {
            let result =
                MepSupport.Evaluate [|
                    ProtocolVersion.Create "0.1"
                    ProtocolVersion.Create "0.2.0-draft.2"
                |]

            Expect.equal
                result.Status
                "not-implemented"
                "there is a future implementation on the supported line"

            Expect.stringContains
                (result.ToString())
                "MEP [0.2.0-draft.2] is not implemented"
                "supported offer"

            Expect.stringContains
                (result.ToString())
                "MEP [0.1] is not supported"
                "unsupported offer"
        }
        test "a legacy offer alongside the implemented draft runs over the exact draft" {
            let result =
                MepSupport.Evaluate [| ProtocolVersion.Create "0.1"; Lifecycle.protocolVersion |]

            Expect.equal result.Status "runnable" "supported exact intersection"

            let actual =
                result.Match(
                    System.Func<ProtocolVersion, string>(fun v -> v.Value),
                    System.Func<string, string>(id),
                    System.Func<string, string>(id)
                )

            Expect.equal actual "0.2.0-draft.1" "negotiated identity"
        }
        test "precedence equality never substitutes for exact implementation identity" {
            let result = MepSupport.Evaluate [| ProtocolVersion.Create "0.2.0-draft.1+other" |]
            Expect.equal result.Status "not-implemented" "build metadata changes protocol identity"
        }
        test "no offered version is not runnable" {
            Expect.equal (MepSupport.Evaluate [||]).Status "not-supported" "empty intersection"
        }
    ]
