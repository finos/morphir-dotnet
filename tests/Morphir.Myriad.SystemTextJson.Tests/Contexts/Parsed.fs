// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.Parsed

open Morphir.Extensions
open Morphir.Myriad.SystemTextJson

/// Makes ExtensionId, which another assembly declares, a parsed value in this context.
[<JsonParsedValue(TextMember = "Value")>]
type ExtensionIdText = ExtensionId

/// Makes ReleaseVersion a parsed value; its text is ToString.
[<JsonParsedValue>]
type ReleaseVersionText = ReleaseVersion

/// A parsed value declared here: a single-case union with a private case.
[<JsonParsedValue(TextMember = "Value")>]
type Code =
    private
    | Code of string

    /// The code text.
    member this.Value = let (Code value) = this in value

    /// Parses a code of three letters.
    static member Parse(text: string) : Result<Code, string> =
        if text.Length = 3 then Ok(Code text) else Error $"'{text}' is not a code of three letters."

/// Parsed values as fields, in an option, as map keys and in a list.
type Manifest =
    {
        Id: ExtensionId
        Version: ReleaseVersion
        Replaces: ExtensionId option
        Owners: Map<ExtensionId, string>
        Codes: Code list
    }
