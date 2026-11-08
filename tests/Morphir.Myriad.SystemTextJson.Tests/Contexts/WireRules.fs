// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.WireRules

open System.Text.Json
open System.Text.Json.Serialization
open System.Collections.Generic
open Morphir.Myriad.SystemTextJson

type Flags =
    { [<JsonDefaultValue("false")>]
      Cancellation: bool }

type PeerKind =
    | Unspecified
    | Cli

type Diagnostic =
    { Message: string
      [<JsonDefaultValue("[]"); JsonOmitEmpty>]
      Related: string list
      [<JsonDefaultValue("\"unspecified\"")>]
      Kind: PeerKind }

type Capabilities =
    { [<JsonName("known", "legacyKnown")>]
      Known: bool
      [<JsonExtensionData>]
      Extra: Map<string, JsonElement> }

type Claims =
    { [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2", CriticalMember = "critical")>]
      ClaimsVersion: string
      [<JsonDefaultValue("[]");
        JsonOmitEmpty;
        JsonKnownPaths("claimsVersion", "protocolVersions", "capabilities", "capabilities.frontend")>]
      Critical: string list }

type ReadOnlyCapabilities =
    { [<JsonExtensionData>]
      Extra: IReadOnlyDictionary<string, JsonElement> }

[<JsonParsedValue(TextMember = "Value")>]
type ClaimsVersion =
    private
    | ClaimsVersion of string

    member this.Value = let (ClaimsVersion value) = this in value

    static member Parse(text: string) =
        if text = "0.1.0-draft.2" then
            Ok(ClaimsVersion text)
        else
            Error "Expected current claims version"

type ParsedClaims =
    { [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2")>]
      ClaimsVersion: ClaimsVersion }

[<JsonUnion(BaseEncoding = (JsonUnionEncoding.InternalTag
                            ||| JsonUnionEncoding.NamedFields
                            ||| JsonUnionEncoding.UnwrapRecordCases))>]
type Tagged = Tagged of Capabilities

[<JsonRecord(AllowNullFields = true)>]
type NullDefault =
    { [<JsonDefaultValue("null")>]
      Text: string }

[<JsonRecord(SkippableOptionFields = JsonSkippableOptionFields.Always)>]
type OptionDefault =
    { [<JsonDefaultValue("42")>]
      Value: int option }
