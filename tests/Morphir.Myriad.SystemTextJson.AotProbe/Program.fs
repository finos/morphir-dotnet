// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Serializes every supported shape through generated contexts, reads each one back, and prints one line for each.
/// The Native AOT lane of the black-box tests (TestAot) runs this program under JIT and as a Native AOT binary and
/// compares the output.
module Morphir.Myriad.SystemTextJson.AotProbe.Program

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization.Metadata
open Morphir.Extensions
open Morphir.Myriad.SystemTextJson.Tests

let mutable private failures = 0

/// Writes the value, reads it back, writes it again, and prints the JSON. The two JSON texts must be equal.
let private probe (name: string) (info: JsonTypeInfo<'T>) (value: 'T) =
    let json = JsonSerializer.Serialize(value, info)
    let untyped = info :> JsonTypeInfo
    let again = JsonSerializer.Serialize(JsonSerializer.Deserialize(json, untyped), untyped)

    if json <> again then
        failures <- failures + 1
        eprintfn "%s: the value read back is different: %s" name again

    printfn "%s %s" name json

[<EntryPoint>]
let main _ =
    let combined =
        JsonSerializerOptions(TypeInfoResolver = JsonTypeInfoResolver.Combine(Records.RecordJson.Default, Addresses.AddressJson.Default))

    let ada: Records.Person = { Name = "Ada"; Age = 36; Active = true }
    probe "record" Records.RecordJson.Default.Person ada
    probe "combined" (combined.GetTypeInfo typeof<Records.Household> :?> JsonTypeInfo<Records.Household>) { Head = ada; Home = { Street = "Main"; Number = 7 } }
    probe "members" Members.MemberJson.Default.Contact { FirstName = "Ada"; Nickname = Some "C"; Email = "a@b"; Phone = ValueNone }

    probe
        "integers"
        Numbers.NumberJson.Default.Integers
        { B = Byte.MaxValue; SB = SByte.MinValue; I16 = Int16.MinValue; U16 = UInt16.MaxValue; I32 = Int32.MinValue; U32 = UInt32.MaxValue; I64 = Int64.MinValue; U64 = UInt64.MaxValue }

    probe "reals" Numbers.NumberJson.Default.Reals { F32 = 0.25f; F64 = 0.1; Dec = Decimal.MaxValue }

    probe
        "scalars"
        Numbers.NumberJson.Default.Scalars
        {
            Flag = true
            Text = "é"
            Letter = 'A'
            Id = Guid("0f8fad5b-d9cb-469f-a165-70867728950e")
            At = DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours 1.0)
            When = DateTime(2026, 11, 7, 10, 0, 0)
            Day = DateOnly(2026, 11, 7)
            Time = TimeOnly(10, 30, 15)
            Span = TimeSpan(1, 2, 3)
            Raw = JsonDocument.Parse("""{"a":[1,true]}""").RootElement.Clone()
        }

    probe
        "collections"
        Collections.CollectionJson.Default.Bag
        {
            Maybe = Some 1
            MaybeValue = ValueNone
            Items = [ 1; 2 ]
            Words = [| "a" |]
            Tags = set [ "x" ]
            Buffer = ResizeArray [ 3 ]
            ReadOnly = [| 4 |]
            Collection = [| "c" |]
            Pair = (6, "six")
            Nested = [ [ 1 ]; [] ]
            Lookup = Map [ "one", 1 ]
            ByNumber = Map [ 1, "one" ]
            Dictionary = Dictionary(dict [ "yes", true ])
        }

    for shape in [ Unions.NoArgs; Unions.OneArg 1; Unions.Unnamed 2; Unions.TwoArgs(3, "t"); Unions.WithRecord { X = 4; Y = 5 } ] do
        probe "union" Unions.UnionJson.Default.Shape shape

    probe "fieldless" Unions.UnionJson.Default.ExtensionType Unions.Validator
    probe "internal" Unions.UnionJson.Default.Problem (Unions.TypeMismatch("Int", "String"))
    probe "token-kind" Unions.UnionJson.Default.RequestId (Unions.RequestId.Number UInt64.MaxValue)
    probe "thoth" Unions.UnionJson.Default.Literal (Unions.BoolLiteral true)
    probe "recursive" Unions.UnionJson.Default.Tree (Unions.Node [ Unions.Leaf; Unions.Node [] ])

    let bag: Corpus.Bag =
        { Maybe = Some 1; Absent = None; MaybeValue = ValueSome 2; Shapes = [ Corpus.WithRecord { X = 1; Y = 2 }; Corpus.NoArgs ]; Choice = Corpus.No; Wrapped = Corpus.Wrapped "w" }

    probe "newtonsoft" Corpus.CorpusNewtonsoft.Default.Bag bag
    probe "thoth-bag" Corpus.CorpusThoth.Default.Bag bag
    probe "record-cases" Corpus.CorpusRecordCases.Default.Bag bag

    probe
        "untagged"
        UntaggedCorpus.CorpusUntagged.Default.Trip
        { Stops = [ UntaggedCorpus.Nowhere; UntaggedCorpus.Exact { Lat = 1.5; Long = 2.0 } ]; Note = None }

    probe
        "parsed"
        Parsed.ParsedJson.Default.Manifest
        {
            Id = ExtensionId.Create "morphir.csharp"
            Version = ReleaseVersion.Create "1.0.0"
            Replaces = Some(ExtensionId.Create "acme.old")
            Owners = Map [ ExtensionId.Create "acme.lint", "Acme" ]
            Codes = []
        }

    probe "converters" Converters.ConverterJson.Default.Labelled { Label = "hello"; Plain = "x"; Pair = { A = 1; B = 2 } }

    let flags = JsonSerializer.Deserialize("{}", WireRules.WireJson.Default.Flags :> JsonTypeInfo) |> unbox<WireRules.Flags>
    probe "wire-default-flags" WireRules.WireJson.Default.Flags flags
    let diagnostic = JsonSerializer.Deserialize("""{"message":"test"}""", WireRules.WireJson.Default.Diagnostic :> JsonTypeInfo) |> unbox<WireRules.Diagnostic>
    probe "wire-default-list-and-union" WireRules.WireJson.Default.Diagnostic diagnostic
    let extra = JsonSerializer.Deserialize("""{"known":true,"future":{"items":[1,null,"x"]}}""", WireRules.WireJson.Default.Capabilities :> JsonTypeInfo) |> unbox<WireRules.Capabilities>
    probe "wire-extension-data" WireRules.WireJson.Default.Capabilities extra
    let claims = JsonSerializer.Deserialize("""{"statementVersion":"0.1.0-draft.1","critical":["statementVersion"]}""", WireRules.WireJson.Default.Claims :> JsonTypeInfo) |> unbox<WireRules.Claims>
    probe "wire-migrated-claims" WireRules.WireJson.Default.Claims claims
    let parsed = JsonSerializer.Deserialize("""{"statementVersion":"0.1.0-draft.1"}""", WireRules.WireJson.Default.ParsedClaims :> JsonTypeInfo) |> unbox<WireRules.ParsedClaims>
    probe "wire-parsed-migration" WireRules.WireJson.Default.ParsedClaims parsed
    let option = JsonSerializer.Deserialize("{}", WireRules.WireLegacy.Default.OptionDefault :> JsonTypeInfo) |> unbox<WireRules.OptionDefault>
    probe "wire-default-option" WireRules.WireLegacy.Default.OptionDefault option
    let nullDefault = JsonSerializer.Deserialize("{}", WireRules.WireJson.Default.NullDefault :> JsonTypeInfo) |> unbox<WireRules.NullDefault>
    probe "wire-default-null" WireRules.WireJson.Default.NullDefault nullDefault
    let defaultClaims = JsonSerializer.Deserialize("{}", WireRules.WireJson.Default.DefaultClaims :> JsonTypeInfo) |> unbox<WireRules.DefaultClaims>
    probe "wire-default-current-version" WireRules.WireJson.Default.DefaultClaims defaultClaims
    let defaultParsedClaims = JsonSerializer.Deserialize("{}", WireRules.WireJson.Default.DefaultParsedClaims :> JsonTypeInfo) |> unbox<WireRules.DefaultParsedClaims>
    probe "wire-default-parsed-version" WireRules.WireJson.Default.DefaultParsedClaims defaultParsedClaims

    let scalars = JsonSerializer.Deserialize("{}", WireRules.WireJson.Default.ScalarDefaults :> JsonTypeInfo) |> unbox<WireRules.ScalarDefaults>
    probe "wire-typed-scalars" WireRules.WireJson.Default.ScalarDefaults scalars
    let empty = JsonSerializer.Deserialize("{}", WireRules.WireJson.Default.EmptyDefaults :> JsonTypeInfo) |> unbox<WireRules.EmptyDefaults>
    probe "wire-empty-collections" WireRules.WireJson.Default.EmptyDefaults empty
    let providers = JsonSerializer.Deserialize("{}", WireRules.WireJson.Default.ProviderDefaults :> JsonTypeInfo) |> unbox<WireRules.ProviderDefaults>
    probe "wire-decimal-and-collection-providers" WireRules.WireJson.Default.ProviderDefaults providers
    let currentProvider = JsonSerializer.Deserialize("{}", WireRules.WireJson.Default.CurrentProviderClaims :> JsonTypeInfo) |> unbox<WireRules.CurrentProviderClaims>
    probe "wire-current-provider" WireRules.WireJson.Default.CurrentProviderClaims currentProvider
    let parsedProvider = JsonSerializer.Deserialize("{}", WireRules.WireJson.Default.ParsedProviderClaims :> JsonTypeInfo) |> unbox<WireRules.ParsedProviderClaims>
    probe "wire-parsed-provider" WireRules.WireJson.Default.ParsedProviderClaims parsedProvider

    if failures = 0 then 0 else 1
