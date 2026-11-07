// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
// The F# samples of docs/content/docs/guides/myriad-json.md that use the generated context.
module Guide.Usage

open System.Text.Json
open System.Text.Json.Serialization.Metadata
open Guide
open Guide.Json

let writePerson () =
    JsonSerializer.Serialize({ Name = "Ada"; Age = 36; Nickname = None }, AppJson.Default.Person)

let readShape (json: string) =
    JsonSerializer.Deserialize(json, AppJson.Default.Shape)

let combine (other: IJsonTypeInfoResolver) =
    let options = JsonSerializerOptions(TypeInfoResolver = JsonTypeInfoResolver.Combine(AppJson.Default, other))
    AppJson.Default.Verify options
    options
