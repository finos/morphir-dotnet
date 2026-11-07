// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.Members

open System.Text.Json.Serialization
open Morphir.Myriad.SystemTextJson

/// Uses the context defaults: camel case, absent options left out, unknown members ignored.
type Contact =
    {
        FirstName: string
        Nickname: string option
        [<JsonName("e-mail", "email")>]
        Email: string
        [<JsonPropertyName("phone")>]
        Phone: string voption
    }

/// Overrides the defaults: snake case, options written as null, unknown members refused.
[<JsonRecord(Naming = JsonNaming.SnakeCaseLower, SkippableOptionFields = JsonSkippableOptionFields.Never, UnknownMembers = JsonUnknownMembers.Refuse)>]
type Strict = { FirstName: string; MiddleName: string option }

/// Accepts null for a field that is not an option, where the type accepts null.
[<JsonRecord(AllowNullFields = true)>]
type Loose = { Text: string; Count: int }
