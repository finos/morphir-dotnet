// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.WireRules

open System.Text.Json
open System.Text.Json.Serialization
open System.Collections.Generic
open Morphir.Myriad.SystemTextJson

type Flags =
    { [<JsonDefaultValue(false)>]
      Cancellation: bool }

type PeerKind =
    | Unspecified
    | Cli

type Diagnostic =
    { Message: string
      [<JsonDefaultEmpty; JsonOmitEmpty>]
      Related: string list
      [<JsonDefaultValue("unspecified")>]
      Kind: PeerKind }

type Frontend =
    { Compile: bool
      [<JsonDefaultValue(false); JsonOmitDefault>]
      MultiDocument: bool }

type Capabilities =
    { [<JsonName("known", "legacyKnown")>]
      Known: bool
      [<JsonExtensionData>]
      Extra: Map<string, JsonElement> }

type Claims =
    { [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2", CriticalMember = "critical")>]
      ClaimsVersion: string
      [<JsonDefaultEmpty;
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
    { [<JsonDefaultJson("null")>]
      Text: string }

[<JsonRecord(SkippableOptionFields = JsonSkippableOptionFields.Always)>]
type OptionDefault =
    { [<JsonDefaultValue(42)>]
      Value: int option }

type DefaultClaims =
    { [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2"); JsonDefaultValue("0.1.0-draft.2")>]
      ClaimsVersion: string }

type DefaultParsedClaims =
    { [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2"); JsonDefaultValue("0.1.0-draft.2")>]
      ClaimsVersion: ClaimsVersion }


type ScalarDefaults =
    { [<JsonDefaultValue("a \"quoted\" name\nnext")>]
      Name: string
      [<JsonDefaultValue(-3)>]
      Count: int
      [<JsonDefaultValue(-9223372036854775808L)>]
      Minimum: int64
      [<JsonDefaultValue(18446744073709551615UL)>]
      Maximum: uint64
      [<JsonDefaultValue(0.5)>]
      Ratio: float
      [<JsonDefaultValue(-0.25f)>]
      Single: float32
      [<JsonDefaultValue(true)>]
      Enabled: bool }

type EmptyDefaults =
    { [<JsonDefaultEmpty>]
      Array: int array
      [<JsonDefaultEmpty>]
      Set: Set<int>
      [<JsonDefaultEmpty>]
      Map: Map<string, int>
      [<JsonDefaultEmpty>]
      Pairs: Map<int, string>
      [<JsonDefaultEmpty>]
      Dictionary: IReadOnlyDictionary<string, int> }

type DefaultValues =
    static member Amount() : decimal = 12.50M
    static member Items() = ResizeArray<int>([ 1 ])
    static member Current() = "0.1.0-draft.2"
    static member Legacy() = "0.1.0-draft.1"

    static member Parsed() =
        ClaimsVersion.Parse("0.1.0-draft.2") |> Result.defaultWith failwith

    static member NullText() : string = Unchecked.defaultof<string>
    static member BadPaths() = [ "unknown.child" ]

    static member Never() : int =
        invalidOp "Provider must not run for an explicit member"

type ProviderDefaults =
    { [<JsonDefaultValueProvider(typeof<DefaultValues>, nameof DefaultValues.Amount)>]
      Amount: decimal
      [<JsonDefaultValueProvider(typeof<DefaultValues>, nameof DefaultValues.Items)>]
      Items: ResizeArray<int> }

type CurrentProviderClaims =
    { [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2");
        JsonDefaultValueProvider(typeof<DefaultValues>, nameof DefaultValues.Current)>]
      ClaimsVersion: string }

type ParsedProviderClaims =
    { [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2");
        JsonDefaultValueProvider(typeof<DefaultValues>, nameof DefaultValues.Parsed)>]
      ClaimsVersion: ClaimsVersion }

type LegacyProviderClaims =
    { [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2");
        JsonDefaultValueProvider(typeof<DefaultValues>, nameof DefaultValues.Legacy)>]
      ClaimsVersion: string }

type NullProvider =
    { [<JsonDefaultValueProvider(typeof<DefaultValues>, nameof DefaultValues.NullText)>]
      Text: string }

[<JsonRecord(AllowNullFields = true)>]
type AllowedNullProvider =
    { [<JsonDefaultValueProvider(typeof<DefaultValues>, nameof DefaultValues.NullText)>]
      Text: string }

type InvalidPathProvider =
    { [<JsonKnownPaths("known"); JsonDefaultValueProvider(typeof<DefaultValues>, nameof DefaultValues.BadPaths)>]
      Paths: string list }

type ExplicitProvider =
    { [<JsonDefaultValueProvider(typeof<DefaultValues>, nameof DefaultValues.Never)>]
      Value: int }

module ProviderScope =
    type Defaults =
        static member Amount() : decimal = 3.25M

    type ScopedPayment =
        { [<JsonDefaultValueProvider(typeof<Defaults>, nameof Defaults.Amount)>]
          Amount: decimal }
