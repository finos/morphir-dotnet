// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.WireRuleTests

open System.Text.Json
open System.Text.Json.Serialization.Metadata
open Expecto
open System.Collections.Generic
open Morphir.Myriad.SystemTextJson.Generator
open Morphir.Myriad.SystemTextJson.Tests.WireRules

let private read (info: JsonTypeInfo<'T>) (json: string) =
    JsonSerializer.Deserialize(json, info :> JsonTypeInfo) |> unbox<'T>

let private generationErrors source =
    let settings =
        match ContextSettings.parse (Dictionary<string, string>(dict [ "Name", "AppJson"; "Namespace", "Sample" ])) with
        | Ok value -> value
        | Error _ -> failwith "Invalid test settings"

    match Generation.generate settings "Types.fs" ("namespace Sample\n\n" + source) with
    | Ok _ -> ""
    | Error errors -> errors |> List.map GenerationError.describe |> String.concat "\n"

[<Tests>]
let tests =
    testList
        "generated wire rules"
        [ testCase "a missing boolean reads its declared default"
          <| fun _ ->
              let value = read WireJson.Default.Flags "{}"
              Expect.isFalse value.Cancellation "A missing flag defaults to false"

              Expect.equal
                  (JsonSerializer.Serialize(value, WireJson.Default.Flags))
                  """{"cancellation":false}"""
                  "Flag is still written"
          testCase "empty collections are omitted and missing fields use their own codecs"
          <| fun _ ->
              let value = read WireJson.Default.Diagnostic """{"message":"test"}"""
              Expect.equal value.Related [] "Missing collection defaults to empty"
              Expect.equal value.Kind PeerKind.Unspecified "A union default uses its normal string encoding"

              Expect.equal
                  (JsonSerializer.Serialize(value, WireJson.Default.Diagnostic))
                  """{"message":"test","kind":"unspecified"}"""
                  "Empty related is omitted"

              let nonempty = { value with Related = [ "next" ] }
              let json = JsonSerializer.Serialize(nonempty, WireJson.Default.Diagnostic)
              Expect.stringContains json "\"related\":[\"next\"]" "Nonempty list is written"
              Expect.equal (read WireJson.Default.Diagnostic json) nonempty "Round trip"
          testCase "a default does not replace an explicit null or a wrong kind"
          <| fun _ ->
              for json in [ """{"cancellation":null}"""; """{"cancellation":"false"}""" ] do
                  Expect.throwsT<JsonException>
                      (fun () -> read WireJson.Default.Flags json |> ignore)
                      "Invalid explicit flag"

              Expect.throwsT<JsonException>
                  (fun () -> read WireJson.Default.Diagnostic "{}" |> ignore)
                  "Unannotated message is required"
          testCase "extension members are captured and flattened without an extra wrapper"
          <| fun _ ->
              let json = """{"known":true,"future":{"items":[1,null,"x"]}}"""
              let value = read WireJson.Default.Capabilities json
              Expect.equal (value.Extra["future"].GetRawText()) """{"items":[1,null,"x"]}""" "Nested JSON preserved"
              Expect.equal (JsonSerializer.Serialize(value, WireJson.Default.Capabilities)) json "No extra wrapper"
              let empty = read WireJson.Default.Capabilities """{"known":false}"""
              Expect.isEmpty empty.Extra "Missing extension data is empty"

              Expect.equal
                  (JsonSerializer.Serialize(empty, WireJson.Default.Capabilities))
                  """{"known":false}"""
                  "No synthetic member"
          testCase "extension data refuses duplicate input and reserved keys on output"
          <| fun _ ->
              for json in
                  [ """{"known":true,"future":1,"future":2}"""
                    """{"known":true,"legacyKnown":false}""" ] do
                  Expect.throwsT<JsonException>
                      (fun () -> read WireJson.Default.Capabilities json |> ignore)
                      "Duplicate refused"

              use document = JsonDocument.Parse "1"

              for key in [ "known"; "legacyKnown" ] do
                  let value =
                      { Known = true
                        Extra = Map.ofList [ key, document.RootElement.Clone() ] }

                  Expect.throwsT<JsonException>
                      (fun () -> JsonSerializer.Serialize(value, WireJson.Default.Capabilities) |> ignore)
                      "Reserved key refused"
          testCase "invalid wire declarations fail generation"
          <| fun _ ->
              for source, expected in
                  [ "type R = { [<JsonDefaultValue(\"[\")>] A: bool }", "JsonDefaultValue"
                    "type R = { [<JsonOmitEmpty>] A: int }", "collection"
                    "type R = { [<JsonExtensionData>] Extra: Map<int, string> }", "JsonElement"
                    "type R = { [<JsonExtensionData>] A: Map<string, System.Text.Json.JsonElement>; [<JsonExtensionData>] B: Map<string, System.Text.Json.JsonElement> }",
                    "one JsonExtensionData" ] do
                  Expect.stringContains (generationErrors source) expected "Specific build diagnostic"
          testCase "legacy claims normalize their version member and exact critical path"
          <| fun _ ->
              let value =
                  read
                      WireJson.Default.Claims
                      """{"statementVersion":"0.1.0-draft.1","critical":["statementVersion","capabilities.frontend"]}"""

              Expect.equal value.ClaimsVersion "0.1.0-draft.2" "Current version in memory"
              Expect.equal value.Critical [ "claimsVersion"; "capabilities.frontend" ] "Only exact legacy path migrated"

              Expect.equal
                  (JsonSerializer.Serialize(value, WireJson.Default.Claims))
                  """{"claimsVersion":"0.1.0-draft.2","critical":["claimsVersion","capabilities.frontend"]}"""
                  "Canonical current output"
          testCase "migration refuses mismatched versions, duplicate aliases and unknown critical paths"
          <| fun _ ->
              for json in
                  [ """{"claimsVersion":"0.1.0-draft.1"}"""
                    """{"statementVersion":"0.1.0-draft.2"}"""
                    """{"claimsVersion":"0.1.0-draft.2","statementVersion":"0.1.0-draft.1"}"""
                    """{"statementVersion":"0.1.0-draft.1","claimsVersion":"0.1.0-draft.2"}"""
                    """{"statementVersion":"0.1.0-draft.1","critical":["claimsVersion"]}"""
                    """{"claimsVersion":"0.1.0-draft.2","critical":["statementVersion"]}"""
                    """{"claimsVersion":"0.1.0-draft.2","critical":["capabilities.frontend.unknown"]}"""
                    """{"claimsVersion":"0.1.0-draft.2","critical":null}"""
                    """{"claimsVersion":2}""" ] do
                  Expect.throwsT<JsonException>
                      (fun () -> read WireJson.Default.Claims json |> ignore)
                      $"Invalid claims: {json}"
          testCase "current claims omit an absent critical list and permit either member order"
          <| fun _ ->
              let current = read WireJson.Default.Claims """{"claimsVersion":"0.1.0-draft.2"}"""
              Expect.isEmpty current.Critical "Empty default"

              Expect.equal
                  (JsonSerializer.Serialize(current, WireJson.Default.Claims))
                  """{"claimsVersion":"0.1.0-draft.2"}"""
                  "Empty list omitted"

              let legacy =
                  read
                      WireJson.Default.Claims
                      """{"critical":["statementVersion"],"statementVersion":"0.1.0-draft.1"}"""

              Expect.equal legacy.Critical [ "claimsVersion" ] "Migration runs after reading the whole object"
          testCase "writers refuse legacy version values and unknown critical paths"
          <| fun _ ->
              for version, paths in
                  [ "0.1.0-draft.1", []
                    "0.1.0-draft.2", [ "capabilities.unknown" ]
                    "0.1.0-draft.2", [ "statementVersion" ] ] do
                  let value: Claims =
                      { ClaimsVersion = version
                        Critical = paths }

                  Expect.throwsT<JsonException>
                      (fun () -> JsonSerializer.Serialize(value, WireJson.Default.Claims) |> ignore)
                      "Only canonical current claims"
          testCase "version migration enters a parsed value only through its current-version parser"
          <| fun _ ->
              let value =
                  read WireJson.Default.ParsedClaims """{"statementVersion":"0.1.0-draft.1"}"""

              Expect.equal value.ClaimsVersion.Value "0.1.0-draft.2" "Canonical version was parsed"

              Expect.equal
                  (JsonSerializer.Serialize(value, WireJson.Default.ParsedClaims))
                  """{"claimsVersion":"0.1.0-draft.2"}"""
                  "Parsed value on output"
          testCase "read-only extension data needs no named member"
          <| fun _ ->
              let json = """{"future":[true,42]}"""
              let value = read WireJson.Default.ReadOnlyCapabilities json
              Expect.equal value.Extra.Count 1 "One captured member"
              Expect.equal (JsonSerializer.Serialize(value, WireJson.Default.ReadOnlyCapabilities)) json "Same JSON"
              Expect.isEmpty (read WireJson.Default.ReadOnlyCapabilities "{}").Extra "Empty default"
          testCase "invalid migration and critical-path declarations fail generation"
          <| fun _ ->
              for source, expected in
                  [ "type R = { [<JsonKnownPaths(\"x\")>] A: int }", "string collection"
                    "type R = { [<JsonVersionAlias(\"old\", \"1\", \"2\")>] A: int }", "string or parsed-value"
                    "type R = { [<JsonVersionAlias(\"A\", \"1\", \"2\")>] A: string }", "distinct"
                    "type R = { [<JsonVersionAlias(\"old\", \"1\", \"2\", CriticalMember = \"missing\")>] A: string }",
                    "CriticalMember"
                    "type R = { [<JsonVersionAlias(\"B\", \"1\", \"2\")>] A: string; B: string }", "JSON name" ] do
                  Expect.stringContains (generationErrors source) expected "Specific declaration failure"
          testCase "extension data cannot overwrite an enclosing union tag"
          <| fun _ ->
              use document = JsonDocument.Parse "1"

              let collision =
                  Tagged
                      { Known = true
                        Extra = Map.ofList [ "Case", document.RootElement.Clone() ] }

              Expect.throwsT<JsonException>
                  (fun () -> JsonSerializer.Serialize(collision, WireJson.Default.Tagged) |> ignore)
                  "Union tag is reserved"

              let json = """{"Case":"tagged","known":true,"future":1}"""
              let value = read WireJson.Default.Tagged json

              Expect.equal
                  (JsonSerializer.Serialize(value, WireJson.Default.Tagged))
                  json
                  "Other extension data survives the wrapper"
          testCase "defaults follow field null and skippable-option rules"
          <| fun _ ->
              let value = read WireJson.Default.NullDefault "{}"
              Expect.isNull (box value.Text) "An allowed null default follows the field's null rule"
              let option = read WireLegacy.Default.OptionDefault "{}"
              Expect.equal option.Value (Some 42) "A skippable option default uses its unwrapped member form"
          testCase "duplicate extension-member errors name the offending key"
          <| fun _ ->
              try
                  read WireJson.Default.Capabilities """{"known":false,"future":1,"future":2}"""
                  |> ignore

                  failtest "Expected a duplicate-member refusal"
              with :? JsonException as error ->
                  Expect.stringContains error.Message "future" "The duplicate key is named"
          testCase "a version alias cannot default to a noncanonical version"
          <| fun _ ->
              for json in [ "\"1\""; "\"3\""; "null"; "2"; "false"; "[]" ] do
                  let literal = JsonSerializer.Serialize json

                  let source =
                      $"type R = {{ [<JsonVersionAlias(\"old\", \"1\", \"2\"); JsonDefaultValue({literal})>] Version: string }}"

                  let errors = generationErrors source
                  Expect.stringContains errors "JsonDefaultValue" "The incompatible default is refused at generation"
                  Expect.stringContains errors "current version" "The diagnostic explains the canonical invariant"
          testCase "current-version defaults round-trip for string and parsed version fields"
          <| fun _ ->
              let expected = """{"claimsVersion":"0.1.0-draft.2"}"""
              let plain = read WireJson.Default.DefaultClaims "{}"
              Expect.equal plain.ClaimsVersion "0.1.0-draft.2" "Canonical string default"

              Expect.equal
                  (JsonSerializer.Serialize(plain, WireJson.Default.DefaultClaims))
                  expected
                  "String default writes canonically"

              let parsed = read WireJson.Default.DefaultParsedClaims "{}"
              Expect.equal parsed.ClaimsVersion.Value "0.1.0-draft.2" "Canonical default passes through its parser"

              Expect.equal
                  (JsonSerializer.Serialize(parsed, WireJson.Default.DefaultParsedClaims))
                  expected
                  "Parsed default writes canonically" ]
