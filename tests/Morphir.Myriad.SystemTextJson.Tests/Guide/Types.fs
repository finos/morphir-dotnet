// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
// The F# samples of docs/content/docs/guides/myriad-json.md. GuideTests checks that each sample is here.
namespace Guide

open Morphir.Myriad.SystemTextJson

type Person = { Name: string; Age: int; Nickname: string option }

[<JsonUnion(BaseEncoding = (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.NamedFields), TagName = "kind")>]
type Shape =
    | Circle of radius: float
    | Square of side: float

[<JsonRecord(Naming = JsonNaming.SnakeCaseLower, UnknownMembers = JsonUnknownMembers.Refuse)>]
type Settings =
    { MaxItems: int
      [<JsonName("colour", "color")>]
      Colour: string }

[<JsonParsedValue(TextMember = "Value")>]
type Email =
    private
    | Email of string

    member this.Value = let (Email value) = this in value

    static member Parse(text: string) : Result<Email, string> =
        if text.Contains "@" then Ok(Email text) else Error $"'{text}' is not an e-mail address."

[<JsonUnion(BaseEncoding = (JsonUnionEncoding.Untagged ||| JsonUnionEncoding.SelectByTokenKind))>]
type RequestId =
    | Number of uint64
    | Text of string

type Diagnostic =
    { Message: string
      [<JsonDefaultEmpty; JsonOmitEmpty>]
      Related: string list
      [<JsonDefaultValue(false)>]
      Retryable: bool }

type Claims =
    { [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2", CriticalMember = "critical")>]
      ClaimsVersion: string
      [<JsonDefaultEmpty; JsonOmitEmpty; JsonKnownPaths("claimsVersion", "protocolVersions", "capabilities", "capabilities.frontend")>]
      Critical: string list }

type PaymentDefaults =
    static member Amount() : decimal = 12.50M

type Payment =
    { [<JsonDefaultValue("anonymous")>]
      Name: string
      [<JsonDefaultValue(3)>]
      Retries: int
      [<JsonDefaultValue(0.5)>]
      Ratio: float
      [<JsonDefaultValueProvider(typeof<PaymentDefaults>, nameof PaymentDefaults.Amount)>]
      Amount: decimal }
