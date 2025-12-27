module Morphir.Json.Tests.CodecTests

open Expecto
open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic.Literal
open Morphir.IR.Versioning
open Morphir.Json
open Morphir.Json.Codecs

[<Tests>]
let nameCodecTests =
    testList "NameCodec" [
        testList "roundtrip" [
            testCase "simple name roundtrips"
            <| fun _ ->
                let name = Name.fromList [ "foo"; "bar"; "baz" ]
                let encoded = NameCodec.encode name
                let decoded = NameCodec.decode encoded
                Expect.equal decoded (Ok name) "Name should roundtrip"

            testCase "empty name roundtrips"
            <| fun _ ->
                let name = Name.empty
                let encoded = NameCodec.encode name
                let decoded = NameCodec.decode encoded
                Expect.equal decoded (Ok name) "Empty name should roundtrip"

            testCase "single segment name roundtrips"
            <| fun _ ->
                let name = Name.fromList [ "single" ]
                let encoded = NameCodec.encode name
                let decoded = NameCodec.decode encoded
                Expect.equal decoded (Ok name) "Single segment name should roundtrip"
        ]

        testList "encoding" [
            testCase "encodes as array of strings"
            <| fun _ ->
                let name = Name.fromList [ "foo"; "bar" ]
                let encoded = NameCodec.encode name
                Expect.equal encoded.ValueKind JsonValueKind.Array "Should be an array"
                let items = encoded.EnumerateArray() |> Seq.toList
                Expect.equal items.Length 2 "Should have 2 items"
                Expect.equal (items.[0].GetString()) "foo" "First segment"
                Expect.equal (items.[1].GetString()) "bar" "Second segment"
        ]

        testList "decoding" [
            testCase "decodes valid array"
            <| fun _ ->
                let json = """["hello", "world"]"""
                let element = JsonDocument.Parse(json).RootElement
                let decoded = NameCodec.decode element
                Expect.equal decoded (Ok (Name.fromList ["hello"; "world"])) "Should decode"

            testCase "fails on invalid type (number)"
            <| fun _ ->
                let json = "42"
                let element = JsonDocument.Parse(json).RootElement
                let decoded = NameCodec.decode element
                Expect.isError decoded "Should fail on invalid type (number)"
        ]

        testList "JsonConverter" [
            testCase "converter serializes correctly"
            <| fun _ ->
                let name = Name.fromList [ "test"; "name" ]
                let options = JsonSerializerOptions()
                options.Converters.Add(NameJsonConverter())
                let json = JsonSerializer.Serialize(name, options)
                Expect.equal json """["test","name"]""" "Should serialize as array"

            testCase "converter deserializes correctly"
            <| fun _ ->
                let json = """["test","name"]"""
                let options = JsonSerializerOptions()
                options.Converters.Add(NameJsonConverter())
                let name = JsonSerializer.Deserialize<Name>(json, options)
                Expect.equal name (Name.fromList ["test"; "name"]) "Should deserialize"
        ]
    ]

[<Tests>]
let pathCodecTests =
    testList "PathCodec" [
        testList "roundtrip" [
            testCase "simple path roundtrips"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList ["foo"]; Name.fromList ["bar"] ]
                let encoded = PathCodec.encode path
                let decoded = PathCodec.decode encoded
                Expect.equal decoded (Ok path) "Path should roundtrip"

            testCase "empty path roundtrips"
            <| fun _ ->
                let path = Path.empty
                let encoded = PathCodec.encode path
                let decoded = PathCodec.decode encoded
                Expect.equal decoded (Ok path) "Empty path should roundtrip"
        ]

        testList "encoding" [
            testCase "encodes as nested arrays"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList ["foo"]; Name.fromList ["bar"; "baz"] ]
                let encoded = PathCodec.encode path
                Expect.equal encoded.ValueKind JsonValueKind.Array "Should be an array"
                let items = encoded.EnumerateArray() |> Seq.toList
                Expect.equal items.Length 2 "Should have 2 names"
        ]

        testList "JsonConverter" [
            testCase "converter roundtrips"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList ["morphir"]; Name.fromList ["sdk"] ]
                let options = JsonSerializerOptions()
                options.Converters.Add(PathJsonConverter())
                options.Converters.Add(NameJsonConverter())
                let json = JsonSerializer.Serialize(path, options)
                let deserialized = JsonSerializer.Deserialize<Path>(json, options)
                Expect.equal deserialized path "Should roundtrip via converter"
        ]
    ]

[<Tests>]
let packageNameCodecTests =
    testList "PackageNameCodec" [
        testList "roundtrip" [
            testCase "package name roundtrips"
            <| fun _ ->
                let packageName = PackageName.packageNameFromString "morphir.sdk"
                let encoded = PackageNameCodec.encode packageName
                let decoded = PackageNameCodec.decode encoded
                Expect.equal decoded (Ok packageName) "PackageName should roundtrip"
        ]

        testList "JsonConverter" [
            testCase "converter roundtrips"
            <| fun _ ->
                let packageName = PackageName.packageNameFromString "morphir.sdk"
                let options = JsonSerializerOptions()
                options.Converters.Add(PackageNameJsonConverter())
                options.Converters.Add(PathJsonConverter())
                options.Converters.Add(NameJsonConverter())
                let json = JsonSerializer.Serialize(packageName, options)
                let deserialized = JsonSerializer.Deserialize<PackageName>(json, options)
                Expect.equal deserialized packageName "Should roundtrip via converter"
        ]
    ]

[<Tests>]
let modulePathCodecTests =
    testList "ModulePathCodec" [
        testList "roundtrip" [
            testCase "module path roundtrips"
            <| fun _ ->
                let modulePath = ModulePath.modulePathFromString "basics"
                let encoded = ModulePathCodec.encode modulePath
                let decoded = ModulePathCodec.decode encoded
                Expect.equal decoded (Ok modulePath) "ModulePath should roundtrip"
        ]

        testList "JsonConverter" [
            testCase "converter roundtrips"
            <| fun _ ->
                let modulePath = ModulePath.modulePathFromString "list.extra"
                let options = JsonSerializerOptions()
                options.Converters.Add(ModulePathJsonConverter())
                options.Converters.Add(PathJsonConverter())
                options.Converters.Add(NameJsonConverter())
                let json = JsonSerializer.Serialize(modulePath, options)
                let deserialized = JsonSerializer.Deserialize<ModulePath>(json, options)
                Expect.equal deserialized modulePath "Should roundtrip via converter"
        ]
    ]

[<Tests>]
let fqNameCodecTests =
    testList "FQNameCodec" [
        testList "roundtrip" [
            testCase "fqname roundtrips"
            <| fun _ ->
                let packagePath = Path.fromString "morphir.sdk"
                let modulePath = Path.fromString "basics"
                let localName = Name.fromString "int"
                let fqName = FQName.fqNameFromPaths packagePath modulePath localName
                let encoded = FQNameCodec.encode fqName
                let decoded = FQNameCodec.decode encoded
                Expect.equal decoded (Ok fqName) "FQName should roundtrip"
        ]

        testList "encoding" [
            testCase "encodes as 3-element array"
            <| fun _ ->
                let packagePath = Path.fromString "morphir.sdk"
                let modulePath = Path.fromString "basics"
                let localName = Name.fromString "int"
                let fqName = FQName.fqNameFromPaths packagePath modulePath localName
                let encoded = FQNameCodec.encode fqName
                Expect.equal encoded.ValueKind JsonValueKind.Array "Should be an array"
                let items = encoded.EnumerateArray() |> Seq.toList
                Expect.equal items.Length 3 "Should have 3 elements (package, module, local)"
        ]

        testList "JsonConverter" [
            testCase "converter roundtrips"
            <| fun _ ->
                let packagePath = Path.fromString "morphir.sdk"
                let modulePath = Path.fromString "list"
                let localName = Name.fromString "map"
                let fqName = FQName.fqNameFromPaths packagePath modulePath localName
                let options = JsonSerializerOptions()
                options.Converters.Add(FQNameJsonConverter())
                options.Converters.Add(PackageNameJsonConverter())
                options.Converters.Add(ModulePathJsonConverter())
                options.Converters.Add(PathJsonConverter())
                options.Converters.Add(NameJsonConverter())
                let json = JsonSerializer.Serialize(fqName, options)
                let deserialized = JsonSerializer.Deserialize<FQName>(json, options)
                Expect.equal deserialized fqName "Should roundtrip via converter"
        ]
    ]

[<Tests>]
let literalCodecTests =
    testList "LiteralCodec" [
        testList "roundtrip v3" [
            testCase "bool literal roundtrips"
            <| fun _ ->
                let literal = BoolLiteral true
                let encoded = LiteralCodec.encodeWithOptions MorphirJsonOptions.v3 literal
                let decoded = LiteralCodec.decode encoded
                Expect.equal decoded (Ok literal) "BoolLiteral should roundtrip"

            testCase "string literal roundtrips"
            <| fun _ ->
                let literal = StringLiteral "hello world"
                let encoded = LiteralCodec.encodeWithOptions MorphirJsonOptions.v3 literal
                let decoded = LiteralCodec.decode encoded
                Expect.equal decoded (Ok literal) "StringLiteral should roundtrip"

            testCase "char literal roundtrips"
            <| fun _ ->
                let literal = CharLiteral 'x'
                let encoded = LiteralCodec.encodeWithOptions MorphirJsonOptions.v3 literal
                let decoded = LiteralCodec.decode encoded
                Expect.equal decoded (Ok literal) "CharLiteral should roundtrip"

            testCase "whole number literal roundtrips"
            <| fun _ ->
                let literal = WholeNumberLiteral 42L
                let encoded = LiteralCodec.encodeWithOptions MorphirJsonOptions.v3 literal
                let decoded = LiteralCodec.decode encoded
                Expect.equal decoded (Ok literal) "WholeNumberLiteral should roundtrip"

            testCase "float literal roundtrips"
            <| fun _ ->
                let literal = FloatLiteral 3.14159
                let encoded = LiteralCodec.encodeWithOptions MorphirJsonOptions.v3 literal
                let decoded = LiteralCodec.decode encoded
                Expect.equal decoded (Ok literal) "FloatLiteral should roundtrip"

            testCase "decimal literal roundtrips"
            <| fun _ ->
                let literal = DecimalLiteral 123.456m
                let encoded = LiteralCodec.encodeWithOptions MorphirJsonOptions.v3 literal
                let decoded = LiteralCodec.decode encoded
                Expect.equal decoded (Ok literal) "DecimalLiteral should roundtrip"
        ]

        testList "version-specific tags" [
            testCase "v1 uses snake_case tags"
            <| fun _ ->
                let literal = BoolLiteral true
                let encoded = LiteralCodec.encodeWithOptions MorphirJsonOptions.v1 literal
                let items = encoded.EnumerateArray() |> Seq.toList
                Expect.equal (items.[0].GetString()) "bool_literal" "v1 should use snake_case"

            testCase "v2 uses snake_case tags"
            <| fun _ ->
                let literal = StringLiteral "test"
                let encoded = LiteralCodec.encodeWithOptions MorphirJsonOptions.v2 literal
                let items = encoded.EnumerateArray() |> Seq.toList
                Expect.equal (items.[0].GetString()) "string_literal" "v2 should use snake_case"

            testCase "v3 uses PascalCase tags"
            <| fun _ ->
                let literal = BoolLiteral false
                let encoded = LiteralCodec.encodeWithOptions MorphirJsonOptions.v3 literal
                let items = encoded.EnumerateArray() |> Seq.toList
                Expect.equal (items.[0].GetString()) "BoolLiteral" "v3 should use PascalCase"

            testCase "SemVer uses PascalCase tags"
            <| fun _ ->
                let semVer = { Major = 4; Minor = 0; Patch = 0; PreRelease = None; BuildMetadata = None }
                let literal = WholeNumberLiteral 100L
                let semVerOptions = { FormatVersion = SemVer semVer; WriteIndented = false }
                let encoded = LiteralCodec.encodeWithOptions semVerOptions literal
                let items = encoded.EnumerateArray() |> Seq.toList
                Expect.equal (items.[0].GetString()) "WholeNumberLiteral" "SemVer should use PascalCase"
        ]

        testList "cross-version decoding" [
            testCase "decodes v1 format"
            <| fun _ ->
                let json = """["bool_literal", true]"""
                let element = JsonDocument.Parse(json).RootElement
                let decoded = LiteralCodec.decode element
                Expect.equal decoded (Ok (BoolLiteral true)) "Should decode v1 format"

            testCase "decodes v2 format"
            <| fun _ ->
                let json = """["string_literal", "hello"]"""
                let element = JsonDocument.Parse(json).RootElement
                let decoded = LiteralCodec.decode element
                Expect.equal decoded (Ok (StringLiteral "hello")) "Should decode v2 format"

            testCase "decodes v3 format"
            <| fun _ ->
                let json = """["BoolLiteral", false]"""
                let element = JsonDocument.Parse(json).RootElement
                let decoded = LiteralCodec.decode element
                Expect.equal decoded (Ok (BoolLiteral false)) "Should decode v3 format"
        ]

        testList "JsonConverter" [
            testCase "converter with v3 options roundtrips"
            <| fun _ ->
                let literal = StringLiteral "test"
                let options = JsonSerializerOptions()
                options.Converters.Add(LiteralJsonConverter(MorphirJsonOptions.v3))
                let json = JsonSerializer.Serialize(literal, options)
                let deserialized = JsonSerializer.Deserialize<Literal>(json, options)
                Expect.equal deserialized literal "Should roundtrip via converter"

            testCase "converter with v1 options produces snake_case"
            <| fun _ ->
                let literal = BoolLiteral true
                let options = JsonSerializerOptions()
                options.Converters.Add(LiteralJsonConverter(MorphirJsonOptions.v1))
                let json = JsonSerializer.Serialize(literal, options)
                Expect.stringContains json "bool_literal" "v1 converter should use snake_case"
        ]
    ]

[<Tests>]
let tagNamingTests =
    testList "TagNaming" [
        testList "literalTag" [
            testCase "v1 produces snake_case with _literal suffix"
            <| fun _ ->
                let tag = TagNaming.literalTag (Classic 1) "BoolLiteral"
                Expect.equal tag "bool_literal" "v1 literal tag"

            testCase "v2 produces snake_case with _literal suffix"
            <| fun _ ->
                let tag = TagNaming.literalTag (Classic 2) "StringLiteral"
                Expect.equal tag "string_literal" "v2 literal tag"

            testCase "v3 preserves PascalCase"
            <| fun _ ->
                let tag = TagNaming.literalTag (Classic 3) "BoolLiteral"
                Expect.equal tag "BoolLiteral" "v3 literal tag"
        ]

        testList "typeTag" [
            testCase "v1 produces lowercase"
            <| fun _ ->
                let tag = TagNaming.typeTag (Classic 1) "Variable"
                Expect.equal tag "variable" "v1 type tag"

            testCase "v2 preserves PascalCase"
            <| fun _ ->
                let tag = TagNaming.typeTag (Classic 2) "Variable"
                Expect.equal tag "Variable" "v2 type tag"

            testCase "v3 preserves PascalCase"
            <| fun _ ->
                let tag = TagNaming.typeTag (Classic 3) "Reference"
                Expect.equal tag "Reference" "v3 type tag"
        ]

        testList "valueTag" [
            testCase "v1 produces lowercase"
            <| fun _ ->
                let tag = TagNaming.valueTag (Classic 1) "Apply"
                Expect.equal tag "apply" "v1 value tag"

            testCase "v2 produces lowercase"
            <| fun _ ->
                let tag = TagNaming.valueTag (Classic 2) "Lambda"
                Expect.equal tag "lambda" "v2 value tag"

            testCase "v3 preserves PascalCase"
            <| fun _ ->
                let tag = TagNaming.valueTag (Classic 3) "Apply"
                Expect.equal tag "Apply" "v3 value tag"
        ]

        testList "patternTag" [
            testCase "v1 produces snake_case"
            <| fun _ ->
                let tag = TagNaming.patternTag (Classic 1) "AsPattern"
                Expect.equal tag "as_pattern" "v1 pattern tag"

            testCase "v2 produces snake_case"
            <| fun _ ->
                let tag = TagNaming.patternTag (Classic 2) "WildcardPattern"
                Expect.equal tag "wildcard_pattern" "v2 pattern tag"

            testCase "v3 preserves PascalCase"
            <| fun _ ->
                let tag = TagNaming.patternTag (Classic 3) "AsPattern"
                Expect.equal tag "AsPattern" "v3 pattern tag"
        ]
    ]

[<Tests>]
let morphirJsonOptionsTests =
    testList "MorphirJsonOptions" [
        testCase "v1 has Classic 1"
        <| fun _ ->
            Expect.equal MorphirJsonOptions.v1.FormatVersion (Classic 1) "v1 format version"

        testCase "v2 has Classic 2"
        <| fun _ ->
            Expect.equal MorphirJsonOptions.v2.FormatVersion (Classic 2) "v2 format version"

        testCase "v3 has Classic 3"
        <| fun _ ->
            Expect.equal MorphirJsonOptions.v3.FormatVersion (Classic 3) "v3 format version"

        testCase "default is v3"
        <| fun _ ->
            Expect.equal MorphirJsonOptions.defaultOptions.FormatVersion (Classic 3) "default is v3"

        testCase "withIndented sets WriteIndented"
        <| fun _ ->
            let opts = MorphirJsonOptions.v3 |> MorphirJsonOptions.withIndented
            Expect.isTrue opts.WriteIndented "WriteIndented should be true"

        testCase "isClassicVersion returns true for Classic"
        <| fun _ ->
            Expect.isTrue (MorphirJsonOptions.isClassicVersion MorphirJsonOptions.v3) "v3 is classic"

        testCase "getMajorVersion returns correct value"
        <| fun _ ->
            Expect.equal (MorphirJsonOptions.getMajorVersion MorphirJsonOptions.v3) 3 "Major version of v3"
    ]

