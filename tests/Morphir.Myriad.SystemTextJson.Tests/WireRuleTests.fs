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
          testCase "typed string defaults are JSON strings rather than JSON documents"
          <| fun _ ->
              let errors =
                  generationErrors "type R = { [<JsonDefaultValue(\"anonymous\")>] Name: string }"

              Expect.equal errors "" "An ordinary string literal is a valid scalar default"
          testCase "scalar defaults reject incompatible primitive kinds and numeric bounds"
          <| fun _ ->
              for value, fieldType in
                  [ "false", "int"
                    "42", "string"
                    "\"false\"", "bool"
                    "256", "byte"
                    "1.5", "int" ] do
                  let errors =
                      generationErrors $"type R = {{ [<JsonDefaultValue({value})>] Value: {fieldType} }}"

                  Expect.stringContains errors "JsonDefaultValue" "The scalar default is checked against its field"
          testCase "empty defaults require collections and default declarations cannot conflict"
          <| fun _ ->
              for source in
                  [ "type R = { [<JsonDefaultEmpty>] Value: int }"
                    "type R = { [<JsonDefaultValue(false); JsonDefaultJson(\"false\")>] Value: bool }"
                    "type R = { [<JsonDefaultEmpty; JsonDefaultValueProvider(typeof<Defaults>, \"Create\")>] Value: int list }" ] do
                  Expect.isNonEmpty (generationErrors source) "Invalid default declaration is refused"
          testCase "provider calls use typeof and nameof without executing user code"
          <| fun _ ->
              let settings =
                  ContextSettings.parse (Dictionary<string, string>(dict [ "Name", "AppJson"; "Namespace", "Sample" ]))
                  |> Result.defaultWith (fun _ -> failwith "settings")

              let source =
                  "namespace Sample\ntype Defaults = static member Amount() : decimal = failwith \"never execute during generation\"\ntype R = { [<JsonDefaultValueProvider(typeof<Defaults>, nameof Defaults.Amount)>] Amount: decimal }"

              match Generation.generate settings "Types.fs" source with
              | Error errors -> failtestf "%A" errors
              | Ok generated ->
                  Expect.stringContains generated "Defaults.``Amount``()" "A direct statically checked call is emitted"
          testCase "typed scalars preserve escaped strings, exact integer bounds and floating point values"
          <| fun _ ->
              let value = read WireJson.Default.ScalarDefaults "{}"
              Expect.equal value.Name "a \"quoted\" name\nnext" "String value is escaped by generation"
              Expect.equal value.Count -3 "Negative integer"
              Expect.equal value.Minimum System.Int64.MinValue "Signed bound"
              Expect.equal value.Maximum System.UInt64.MaxValue "Unsigned bound"
              Expect.equal value.Ratio 0.5 "Double"
              Expect.equal value.Single -0.25f "Single"
              Expect.isTrue value.Enabled "Boolean literal"
              let json = JsonSerializer.Serialize(value, WireJson.Default.ScalarDefaults)
              Expect.equal (read WireJson.Default.ScalarDefaults json) value "Scalar defaults round trip"
          testCase "empty defaults follow sequence and map encodings"
          <| fun _ ->
              let value = read WireJson.Default.EmptyDefaults "{}"
              Expect.isEmpty value.Array "Array"
              Expect.isEmpty value.Set "Set"
              Expect.isEmpty value.Map "String-keyed map"
              Expect.isEmpty value.Pairs "Non-string-keyed map"
              Expect.equal value.Dictionary.Count 0 "Read-only dictionary"
              let json = JsonSerializer.Serialize(value, WireJson.Default.EmptyDefaults)
              let again = read WireJson.Default.EmptyDefaults json
              Expect.equal again.Pairs value.Pairs "Pair-array map round trip"
          testCase "providers return exact decimals and fresh mutable defaults"
          <| fun _ ->
              let first = read WireJson.Default.ProviderDefaults "{}"
              let second = read WireJson.Default.ProviderDefaults "{}"
              Expect.equal first.Amount 12.50M "Decimal needs no JSON text or double conversion"
              first.Items.Add 2
              Expect.sequenceEqual second.Items [ 1 ] "A new provider call creates an independent collection"
              let json = JsonSerializer.Serialize(second, WireJson.Default.ProviderDefaults)
              let again = read WireJson.Default.ProviderDefaults json
              Expect.equal again.Amount second.Amount "Decimal round trip"
          testCase "providers resolve in the record's declaring module"
          <| fun _ ->
              let value = read WireJson.Default.ScopedPayment "{}"
              Expect.equal value.Amount 3.25M "Nested provider type is resolved from its lexical scope"
          testCase "providers are called only for absent members"
          <| fun _ ->
              let value = read WireJson.Default.ExplicitProvider """{"value":7}"""
              Expect.equal value.Value 7 "Explicit value never calls throwing provider"

              Expect.throwsT<JsonException>
                  (fun () -> read WireJson.Default.ExplicitProvider """{"value":null}""" |> ignore)
                  "Explicit null is checked without calling the provider"

              Expect.throwsT<System.InvalidOperationException>
                  (fun () -> read WireJson.Default.ExplicitProvider "{}" |> ignore)
                  "Absent member calls the provider"
          testCase "provider results preserve version, critical path and null invariants"
          <| fun _ ->
              let plain = read WireJson.Default.CurrentProviderClaims "{}"
              Expect.equal plain.ClaimsVersion "0.1.0-draft.2" "Canonical string provider"
              let parsed = read WireJson.Default.ParsedProviderClaims "{}"
              Expect.equal parsed.ClaimsVersion.Value plain.ClaimsVersion "Typed parsed-value provider"

              Expect.throwsT<JsonException>
                  (fun () -> read WireJson.Default.LegacyProviderClaims "{}" |> ignore)
                  "Legacy version cannot bypass validation"

              Expect.throwsT<JsonException>
                  (fun () -> read WireJson.Default.InvalidPathProvider "{}" |> ignore)
                  "Provider paths must be known"

              Expect.throwsT<JsonException>
                  (fun () -> read WireJson.Default.NullProvider "{}" |> ignore)
                  "Null provider value is refused"

              let nullable = read WireJson.Default.AllowedNullProvider "{}"
              Expect.isNull (box nullable.Text) "Configured nullable field permits a null provider result"
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
          testCase "a bool equal to its default is omitted and an absent bool reads as the default"
          <| fun _ ->
              let single = read WireJson.Default.Frontend """{"compile":true}"""
              Expect.isFalse single.MultiDocument "Absent reads as the default"

              Expect.equal
                  (JsonSerializer.Serialize(single, WireJson.Default.Frontend))
                  """{"compile":true}"""
                  "The default value is not written"

              let multiple = { single with MultiDocument = true }

              Expect.equal
                  (JsonSerializer.Serialize(multiple, WireJson.Default.Frontend))
                  """{"compile":true,"multiDocument":true}"""
                  "Another value is written"

              Expect.equal (read WireJson.Default.Frontend """{"compile":true,"multiDocument":false}""") single "An explicit default is read"
          testCase "invalid wire declarations fail generation"
          <| fun _ ->
              for source, expected in
                  [ "type R = { [<JsonDefaultJson(\"[\")>] A: bool }", "JsonDefaultJson"
                    "type R = { [<JsonOmitEmpty>] A: int }", "collection"
                    "type R = { [<JsonOmitDefault>] A: bool }", "JsonOmitDefault"
                    "type R = { [<JsonDefaultValue(1); JsonOmitDefault>] A: int }", "JsonOmitDefault"
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
                      $"type R = {{ [<JsonVersionAlias(\"old\", \"1\", \"2\"); JsonDefaultJson({literal})>] Version: string }}"

                  let errors = generationErrors source
                  Expect.stringContains errors "JsonDefaultJson" "The incompatible default is refused at generation"
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
