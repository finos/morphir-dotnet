// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.WireTests

open System.Text.Json
open System
open Expecto
open Morphir.Mep

[<Tests>]
let tests =
    testList
        "MEP wire contract"
        [ testCase "describe binds its one-member params object"
          <| fun _ ->
              let json = """{"protocolVersions":["0.2.0-draft.1"]}"""

              let value =
                  JsonSerializer.Deserialize(
                      json,
                      MepJson.Default.DescribeParams :> Serialization.Metadata.JsonTypeInfo
                  )
                  |> unbox<DescribeParams>

              Expect.equal value.ProtocolVersions[0].Value "0.2.0-draft.1" "Exact draft version"
              Expect.equal (JsonSerializer.Serialize(value, MepJson.Default.DescribeParams)) json "Same object"
          testCase "the generated resolver has every type dependency"
          <| fun _ -> MepJson.Default.Verify MepJson.Default.Options
          testCase "required params cannot be absent or null"
          <| fun _ ->
              for json in [ "{}"; """{"protocolVersions":null}""" ] do
                  Expect.throwsT<JsonException>
                      (fun () -> JsonSerializer.Deserialize(json, MepJson.Default.DescribeParams) |> ignore)
                      "The protocol versions are required"
          testCase "source version literals use exact ordinal identities"
          <| fun _ ->
              let version = ProtocolVersion.Create "0.2.0-draft.1"
              Expect.equal version.Value "0.2.0-draft.1" "Pinned draft"
              Expect.notEqual version (ProtocolVersion.Create "0.2.0-DRAFT.1") "Draft spelling is exact"
              Expect.isTrue (version.Equals(ProtocolVersion.Create "0.2.0-draft.1")) "Value identity"
          testCase "invalid version text is refused at the generated reader boundary"
          <| fun _ ->
              for version in [ ""; "0.2"; "01.2.3"; "1.2.3-01"; "1.2.3+"; " 1.2.3" ] do
                  let json = $"""{{"protocolVersions":["{version}"]}}"""

                  Expect.throwsT<JsonException>
                      (fun () -> JsonSerializer.Deserialize(json, MepJson.Default.DescribeParams) |> ignore)
                      "Invalid domain version cannot enter a message"
          testCase "TryParse reports the correct result for C# callers"
          <| fun _ ->
              let mutable version = Unchecked.defaultof<ProtocolVersion>
              let mutable failure = Unchecked.defaultof<ParseFailure>
              Expect.isTrue (ProtocolVersion.TryParse("0.2.0-draft.1", &version, &failure)) "Valid version"
              Expect.equal version.Value "0.2.0-draft.1" "Result is present"
              Expect.isFalse (ProtocolVersion.TryParse(null, &version, &failure)) "Null refused"

              Expect.equal
                  (failure.ToString())
                  "Expected a full SemVer protocol version or the frozen legacy version 0.1."
                  "Failure is present"
          testCase "full SemVer metadata has no local length restriction"
          <| fun _ ->
              let text = "1.2.3+" + String('a', 300)
              Expect.equal (ProtocolVersion.Create text).Value text "No length rule added to the wire contract"
          testCase "legacy offers can be read before session negotiation refuses them"
          <| fun _ ->
              let json = """{"protocolVersions":["0.1"]}"""

              let value =
                  JsonSerializer.Deserialize(
                      json,
                      MepJson.Default.DescribeParams :> Serialization.Metadata.JsonTypeInfo
                  )
                  |> unbox<DescribeParams>

              Expect.equal value.ProtocolVersions[0].Value "0.1" "The wire codec does not decide session negotiation" ]
