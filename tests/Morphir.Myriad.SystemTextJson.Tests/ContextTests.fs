// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.ContextTests

open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open Expecto
open Morphir.Myriad.SystemTextJson.Tests.Empty

[<Tests>]
let tests =
    testList "generated context" [
        test "an empty context is a System.Text.Json context" {
            let context: JsonSerializerContext = EmptyJson.Default
            Expect.isNotNull (box context) "Default exists"
            Expect.isTrue (box context :? IJsonTypeInfoResolver) "the context is a type info resolver"
        }

        test "an empty context has no type info" {
            Expect.isNull (EmptyJson.Default.GetTypeInfo typeof<string>) "no type info for string"
        }

        test "an empty context resolves nothing for other options" {
            let resolver = EmptyJson.Default :> IJsonTypeInfoResolver
            Expect.isNull (resolver.GetTypeInfo(typeof<int>, JsonSerializerOptions())) "no type info for int"
        }

        test "a context can be created with options" {
            let context = EmptyJson(JsonSerializerOptions())
            Expect.isTrue (obj.ReferenceEquals(context.Options.TypeInfoResolver, context)) "the options use the context"
        }

        test "Verify accepts options that cover nothing, because the context needs nothing" {
            EmptyJson.Default.Verify(JsonSerializerOptions(TypeInfoResolver = EmptyJson.Default))
        }
    ]
