// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The compatibility reference: FSharp.SystemTextJson (reflection-based, tests only).
module Morphir.Myriad.SystemTextJson.Tests.Compatibility

open System.Text.Json
open System.Text.Json.Serialization
open Expecto

/// JSON from FSharp.SystemTextJson with the given options.
let reference (fsharpOptions: JsonFSharpOptions) (value: 'T) =
    let options = JsonSerializerOptions()
    fsharpOptions.AddToJsonSerializerOptions options
    JsonSerializer.Serialize(value, options)

/// Checks that the generated JSON equals the FSharp.SystemTextJson JSON for the same options.
let expectSameAsReference (fsharpOptions: JsonFSharpOptions) (generated: string) (value: 'T) =
    Expect.equal generated (reference fsharpOptions value) "the generated JSON equals the FSharp.SystemTextJson JSON"
