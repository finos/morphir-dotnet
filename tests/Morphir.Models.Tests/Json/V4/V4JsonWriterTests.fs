/// Checks the IR v4 JSON writer against its oracle: the readers, the kit's canonical spellings and the pinned
/// v4 fixtures.
module Morphir.Json.Tests.V4.V4JsonWriterTests

open System
open System.IO
open System.Numerics
open System.Text
open System.Text.Json
open Expecto
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4
open Morphir.Json.Tests.V4.KitCases

let private readWith read (json: string) =
    use document = JsonDocument.Parse json

    match read document.RootElement with
    | Ok value -> value
    | Error(error: V4JsonError) ->
        failtestf "%s should read, got %s at %s: %s" json error.Rule error.Location error.Message

let private written result =
    match result with
    | Ok(bytes: byte array) -> bytes
    | Error(error: V4WriteError) ->
        failtestf "Expected a write, got %s at %s: %s" error.Rule error.Location error.Message

let private refusal result =
    match result with
    | Ok(bytes: byte array) ->
        failtestf "Expected a refusal, wrote %s" (Encoding.UTF8.GetString bytes)
    | Error(error: V4WriteError) -> error

let private text (bytes: byte array) = Encoding.UTF8.GetString bytes

let private parsed (json: string) =
    use document = JsonDocument.Parse json
    document.RootElement.Clone()

let private formatVersion (release: string) =
    match Versioning.parse release with
    | Ok version -> version
    | Error error -> failtestf "%s is not a format version: %O" release error

/// Writes a document in the version it was read with.
let private rewrite (document: V4Document) =
    V4JsonWriter.writeDocument false (formatVersion document.FormatVersion) document.Distribution
    |> written

/// Compares two JSON values exactly: object members in order, array items in order, and number lexemes.
let rec private sameJson (expected: JsonElement) (actual: JsonElement) =
    expected.ValueKind = actual.ValueKind
    && match expected.ValueKind with
       | JsonValueKind.Object ->
           let expectedMembers =
               expected.EnumerateObject()
               |> Seq.toList

           let actualMembers =
               actual.EnumerateObject()
               |> Seq.toList

           expectedMembers.Length = actualMembers.Length
           && List.forall2
               (fun (left: JsonProperty) (right: JsonProperty) ->
                   left.Name = right.Name
                   && sameJson left.Value right.Value
               )
               expectedMembers
               actualMembers
       | JsonValueKind.Array ->
           let expectedItems =
               expected.EnumerateArray()
               |> Seq.toList

           let actualItems =
               actual.EnumerateArray()
               |> Seq.toList

           expectedItems.Length = actualItems.Length
           && List.forall2 sameJson expectedItems actualItems
       | JsonValueKind.Number -> expected.GetRawText() = actual.GetRawText()
       | JsonValueKind.String -> expected.GetString() = actual.GetString()
       | _ -> true

let private navigate (path: string list) (element: JsonElement) =
    path
    |> List.fold
        (fun (current: JsonElement) (memberName: string) ->
            match current.TryGetProperty memberName with
            | true, found -> found
            | false, _ ->
                failtestf
                    "The written JSON has no member '%s' in %s"
                    memberName
                    (current.GetRawText())
        )
        element

/// The reader a kit node goes through, and how to write the model it reads.
type private NodeKind =
    | DocumentNode
    | ValueNode
    | TypeNode
    | PatternNode

/// Reads JSON as a node kind and writes the model back. Returns the model and the written bytes.
let private readAndWrite kind (json: string) : obj * byte array =
    match kind with
    | ValueNode ->
        let model = readWith V4Json.readValue json
        box model, written (V4JsonWriter.writeValue false model)
    | TypeNode ->
        let model = readWith V4Json.readType json
        box model, written (V4JsonWriter.writeType false model)
    | PatternNode ->
        let model = readWith V4Json.readPattern json
        box model, written (V4JsonWriter.writePattern false model)
    | DocumentNode ->
        let model = readWith V4Json.readDistribution json
        box model, rewrite model

let private readBack kind (bytes: byte array) : obj =
    let json = text bytes

    match kind with
    | ValueNode -> box (readWith V4Json.readValue json)
    | TypeNode -> box (readWith V4Json.readType json)
    | PatternNode -> box (readWith V4Json.readPattern json)
    | DocumentNode -> box (readWith V4Json.readDistribution json)

/// Places a kit node where the readers take it, as V4KitTests does, and names where the writer puts it back.
let private placement (case: KitCase) =
    let node =
        case.Tags
        |> List.tryPick (fun tag ->
            if tag.StartsWith "@node:" then
                Some(tag.Substring 6)
            else
                None
        )

    let specModule body =
        $"""{{ "formatVersion": 4, "distribution": {{ "Specs": {{ "packageName": "t", "spec": {{ "modules": {{ "m": {body} }} }} }} }} }}"""

    let defModule body =
        $"""{{ "formatVersion": 4, "distribution": {{ "Library": {{ "packageName": "t", "def": {{ "modules": {{ "m": {{ "Public": {body} }} }} }} }} }} }}"""

    let specPath = [ "distribution"; "Specs"; "spec"; "modules"; "m" ]
    let defPath = [ "distribution"; "Library"; "def"; "modules"; "m"; "Public" ]

    match node with
    | Some "Value" -> ValueNode, id, []
    | Some "Type" -> TypeNode, id, []
    | Some "Pattern" -> PatternNode, id, []
    | Some "Literal" -> ValueNode, (fun json -> $"""{{ "Literal": {json} }}"""), [ "Literal" ]
    | Some "Name" -> ValueNode, (fun json -> $"""{{ "Variable": {json} }}"""), [ "Variable" ]
    | Some "FQName" -> ValueNode, (fun json -> $"""{{ "Reference": {json} }}"""), [ "Reference" ]
    | Some "Path" ->
        DocumentNode,
        (fun json ->
            $"""{{ "formatVersion": 4, "distribution": {{ "Specs": {{ "packageName": {json} }} }} }}"""
        ),
        [ "distribution"; "Specs"; "packageName" ]
    | Some "FormatVersion" ->
        DocumentNode,
        (fun json ->
            $"""{{ "formatVersion": {json}, "distribution": {{ "Library": {{ "packageName": "t" }} }} }}"""
        ),
        [ "formatVersion" ]
    | Some "Distribution" -> DocumentNode, id, []
    | Some "TypeSpecification" ->
        DocumentNode,
        (fun json -> specModule $"""{{ "types": {{ "t": {json} }} }}"""),
        specPath
        @ [ "types"; "t" ]
    | Some "ValueSpecification" ->
        DocumentNode,
        (fun json -> specModule $"""{{ "values": {{ "v": {json} }} }}"""),
        specPath
        @ [ "values"; "v" ]
    | Some "ModuleSpecification" -> DocumentNode, specModule, specPath
    | Some "TypeDefinition" ->
        DocumentNode,
        (fun json -> defModule $"""{{ "types": {{ "t": {{ "Public": {json} }} }} }}"""),
        defPath
        @ [ "types"; "t"; "Public" ]
    | Some "ValueDefinition" ->
        DocumentNode,
        (fun json -> defModule $"""{{ "values": {{ "v": {{ "Public": {json} }} }} }}"""),
        defPath
        @ [ "values"; "v"; "Public" ]
    | Some "AccessControlledTypeDefinition" ->
        DocumentNode,
        (fun json -> defModule $"""{{ "types": {{ "t": {json} }} }}"""),
        defPath
        @ [ "types"; "t" ]
    | Some "AccessControlledValueDefinition" ->
        DocumentNode,
        (fun json -> defModule $"""{{ "values": {{ "v": {json} }} }}"""),
        defPath
        @ [ "values"; "v" ]
    | other -> failtestf "The kit case %s has a node with no placement: %O" case.Id other

/// Canonical steps that no writer can meet, because the kit gives two spellings of one model and the reader
/// reads both to that model. Each one is checked to still differ, so that this list stays exact.
/// - definitions-0020 writes the argument-free annotation `my-org/project:annotations#internal` as the object
///   `{ "name": ... }`, while definitions-0021 and definitions-0022 write an argument-free annotation as the
///   compact string. The writer takes the compact string.
/// - distributions-0004 spells the module path `u-s/f-r-2052-a/data-tables`, while names-0001 and names-0008
///   spell single-letter runs as initialisms (`value-in-USD`, `FR-2052-a`). The writer uses the CanonicalNames
///   writers, which give `US/FR-2052-a/data-tables`.
let private unreachable = set [ "definitions-0020"; "distributions-0004" ]

let private checkCanonical (case: KitCase) (canonical: string) =
    let kind, wrap, path = placement case
    let model, bytes = readAndWrite kind (wrap canonical)
    Expect.equal (readBack kind bytes) model "The written JSON reads back to the same model"
    Expect.equal (snd (readAndWrite kind (text bytes))) bytes "Writing again gives the same bytes"

    let actual = navigate path (parsed (text bytes))
    let matches = sameJson (parsed canonical) actual

    if unreachable.Contains case.Id then
        Expect.isFalse
            matches
            $"{case.Id} is listed as unreachable but now matches; remove it from the list"
    else
        Expect.isTrue matches $"Expected the canonical {canonical}, wrote {actual.GetRawText()}"

let private features = [
    "names.feature"
    "values.feature"
    "types.feature"
    "patterns-and-literals.feature"
    "definitions.feature"
    "distributions.feature"
]

let private canonicalTests =
    testList "Kit canonical spellings" [
        for feature in features do
            testList feature [
                for case in casesOf feature do
                    let canonical =
                        case.Steps
                        |> List.choose (fun step ->
                            match step with
                            | Canonical json -> Some json
                            | Accepts _
                            | ReadsAsList _
                            | Rejects _
                            | AcceptsFile _ -> None
                        )

                    for index, json in List.indexed canonical do
                        testCase $"{case.Id} {case.Title} ({index + 1})"
                        <| fun () -> checkCanonical case json
            ]

        testCase "Every feature has canonical JSON steps"
        <| fun () ->
            let count =
                features
                |> List.sumBy (fun feature ->
                    casesOf feature
                    |> List.sumBy (fun case ->
                        case.Steps
                        |> List.filter (fun step ->
                            match step with
                            | Canonical _ -> true
                            | Accepts _
                            | ReadsAsList _
                            | Rejects _
                            | AcceptsFile _ -> false
                        )
                        |> List.length
                    )
                )

            Expect.isGreaterThan count 70 "The canonical steps are found"
    ]
    |> testLabel "PlatformSpecific"

let private fixturesRoot = Path.Combine(fixtureRoot, "..")

/// Whether a JSON file declares format version 4.
let private isV4 (file: string) =
    use document = JsonDocument.Parse(File.ReadAllText file)

    match document.RootElement.TryGetProperty "formatVersion" with
    | true, version when version.ValueKind = JsonValueKind.Number -> version.GetRawText() = "4"
    | true, version when version.ValueKind = JsonValueKind.String ->
        version.GetString().StartsWith "4."
    | _ -> false

let private v4Fixtures () =
    [
        yield Path.Combine(fixturesRoot, "MorphirIR", "daily-rate-v4.json")
        for folder in [ "pairs"; "records"; "v4" ] do
            yield! Directory.GetFiles(Path.Combine(fixtureRoot, folder), "*.json")
    ]
    |> List.filter isV4
    |> List.sort

let private fixtureName (file: string) =
    Path.GetRelativePath(fixturesRoot, file).Replace('\\', '/')

let private fixtureTests =
    testList "Fixture round trip" [
        for file in v4Fixtures () do
            testCase (fixtureName file)
            <| fun () ->
                let original = readWith V4Json.readDistribution (File.ReadAllText file)
                let bytes = rewrite original
                let again = readWith V4Json.readDistribution (text bytes)
                Expect.equal again original "The written document reads back to an equal model"
                Expect.equal (rewrite again) bytes "Writing again gives identical bytes"

                let indented =
                    V4JsonWriter.writeDocument
                        true
                        (formatVersion original.FormatVersion)
                        original.Distribution
                    |> written

                Expect.equal
                    (readWith V4Json.readDistribution (text indented))
                    original
                    "The indented document reads back to an equal model"

        testCase "The v4 fixtures are found"
        <| fun () ->
            let names =
                v4Fixtures ()
                |> List.map fixtureName

            for expected in
                [
                    "MorphirIR/daily-rate-v4.json"
                    "MorphirIon/pairs/v4-application.json"
                    "MorphirIon/pairs/v4-empty-library.json"
                    "MorphirIon/pairs/v4-library-nodes.json"
                    "MorphirIon/pairs/v4-specs.json"
                    "MorphirIon/pairs/complete-example.json"
                    "MorphirIon/pairs/books-and-records-example.json"
                    "MorphirIon/records/library.json"
                    "MorphirIon/v4/complete-example.json"
                ] do
                Expect.contains names expected "A v4 fixture is checked"

            Expect.isFalse
                (names
                 |> List.exists (fun name -> name.Contains "v3"))
                "No classic fixture is checked"
    ]
    |> testLabel "PlatformSpecific"

let private name word = Name [ word ]

let private fq (package: string) (moduleName: string) (local: string) = {
    PackagePath = PackageName(Morphir.IR.Path [ name package ])
    ModulePath = ModulePath(Morphir.IR.Path [ name moduleName ])
    LocalName = Name(List.ofArray (local.Split('-')))
}

let private map entries =
    match OrderedMap.ofList entries with
    | Ok map -> map
    | Error key -> failtestf "The key %O repeats" key

let private intType =
    Type.Reference(TypeAttributes.empty, fq "sdk" "basics" "int", [])

let private literalValue literal =
    Value.Literal(ValueAttributes.empty, literal)

let private expressionDefinition output body : Documented<ValueDefinition> = {
    Doc = None
    Value = {
        InputTypes = []
        Body = ExpressionBody(output, body)
    }
}

let private library (values: (Name * AccessControlled<Documented<ValueDefinition>>) list) =
    Library(
        PackageName(Morphir.IR.Path [ name "acme" ]),
        OrderedMap.empty,
        {
            Modules =
                map [
                    ModulePath(Morphir.IR.Path [ name "main" ]),
                    {
                        Access = Public
                        Value = {
                            Types = OrderedMap.empty
                            Values = map values
                            Doc = None
                        }
                    }
                ]
        }
    )

let private publicValue body = {
    Access = Public
    Value = expressionDefinition intType body
}

let private decimalOf lexeme =
    match DecimalLexeme.create lexeme with
    | Ok value -> value
    | Error message -> failtest message

let private exactnessTests =
    testList "Number exactness" [
        testCase "An integer past 2^53 and the decimal 1.10 keep their lexemes"
        <| fun () ->
            let big = BigInteger.Parse "9007199254740993"

            let model =
                library [
                    name "big", publicValue (literalValue (IntegerLiteral big))
                    name "rate", publicValue (literalValue (DecimalLiteral(decimalOf "1.10")))
                ]

            let json = text (written (V4JsonWriter.writeDistribution false model))
            Expect.stringContains json "{\"IntegerLiteral\":9007199254740993}" "The integer lexeme"
            Expect.stringContains json "{\"DecimalLiteral\":\"1.10\"}" "The decimal lexeme"

            let read = readWith V4Json.readDistribution json
            Expect.equal read.Distribution model "The model reads back"

            match read.Distribution with
            | Library(_, _, definition) ->
                let values =
                    definition.Modules
                    |> OrderedMap.toList
                    |> List.collect (fun (_, controlled) ->
                        OrderedMap.toList controlled.Value.Values
                    )

                let literalOf key =
                    match List.tryFind (fun (entry, _) -> entry = name key) values with
                    | Some(_, controlled) ->
                        match controlled.Value.Value.Body with
                        | ExpressionBody(_, Value.Literal(_, literal)) -> literal
                        | other -> failtestf "Unexpected body %O" other
                    | None -> failtestf "No value %s" key

                match literalOf "big" with
                | IntegerLiteral value -> Expect.equal value big "The integer value"
                | other -> failtestf "Unexpected literal %O" other

                match literalOf "rate" with
                | DecimalLiteral lexeme ->
                    Expect.equal (DecimalLexeme.value lexeme) "1.10" "The decimal lexeme"

                    Expect.equal
                        (Decimal.Parse(
                            DecimalLexeme.value lexeme,
                            Globalization.CultureInfo.InvariantCulture
                        ))
                            .Scale
                        (byte 2)
                        "The decimal scale"
                | other -> failtestf "Unexpected literal %O" other
            | other -> failtestf "Expected a Library, got %O" other

        testCase "A float lexeme is kept and a float without one is written as a float"
        <| fun () ->
            let withLexeme =
                match FloatValue.create 1e21 (Some "1.0e21") with
                | Ok value -> value
                | Error message -> failtest message

            let withoutLexeme =
                match FloatValue.create 4.0 None with
                | Ok value -> value
                | Error message -> failtest message

            let json value =
                text (written (V4JsonWriter.writeValue false (literalValue (FloatLiteral value))))

            Expect.equal
                (json withLexeme)
                "{\"Literal\":{\"FloatLiteral\":1.0e21}}"
                "The lexeme is kept"

            Expect.equal
                (json withoutLexeme)
                "{\"Literal\":{\"FloatLiteral\":4.0}}"
                "A float stays a float"

        testCase "A document number keeps its lexeme"
        <| fun () ->
            let document =
                DocumentObject(
                    map [ "id", DocumentNumber "9007199254740993"; "ratio", DocumentNumber "0.10" ]
                )

            Expect.equal
                (text (
                    written (
                        V4JsonWriter.writeValue false (literalValue (DocumentLiteral document))
                    )
                ))
                "{\"Literal\":{\"DocumentLiteral\":{\"id\":9007199254740993,\"ratio\":0.10}}}"
                "The lexemes"
    ]

let private emptyLibrary =
    Library(
        PackageName(Morphir.IR.Path [ name "acme" ]),
        OrderedMap.empty,
        { Modules = OrderedMap.empty }
    )

let private layoutTests =
    testList "Layout and format version" [
        testCase "Compact output has no whitespace"
        <| fun () ->
            Expect.equal
                (text (written (V4JsonWriter.writeDistribution false emptyLibrary)))
                "{\"formatVersion\":4,\"distribution\":{\"Library\":{\"packageName\":\"acme\",\"dependencies\":{},\"def\":{\"modules\":{}}}}}"
                "compact"

        testCase "Indented output uses two spaces"
        <| fun () ->
            let expected =
                String.concat "\n" [
                    "{"
                    "  \"formatVersion\": 4,"
                    "  \"distribution\": {"
                    "    \"Library\": {"
                    "      \"packageName\": \"acme\","
                    "      \"dependencies\": {},"
                    "      \"def\": {"
                    "        \"modules\": {}"
                    "      }"
                    "    }"
                    "  }"
                    "}"
                ]

            Expect.equal
                (text (written (V4JsonWriter.writeDistribution true emptyLibrary)))
                expected
                "indented"

        for release, spelling in
            [ "4", "4"; "4.0.0", "4"; "4.0.1", "\"4.0.1\""; "4.1.0-draft.1", "\"4.1.0-draft.1\"" ] do
            testCase $"Format version {release} is spelled {spelling}"
            <| fun () ->
                let json =
                    text (
                        written (
                            V4JsonWriter.writeDocument false (formatVersion release) emptyLibrary
                        )
                    )

                Expect.stringStarts json $"{{\"formatVersion\":{spelling}," "The spelling"

        for release in [ "3"; "5"; "3.1.0" ] do
            testCase $"Format version {release} is refused"
            <| fun () ->
                let error =
                    refusal (V4JsonWriter.writeDocument false (formatVersion release) emptyLibrary)

                Expect.equal error.Rule "unsupported_format_version" "The rule"
                Expect.equal error.Location "$.formatVersion" "The location"
    ]

let private source line = {
    StartLine = line
    StartColumn = 1
    EndLine = line
    EndColumn = 2
}

let private extensions =
    DocumentObject(map [ "tag", DocumentString "x"; "weight", DocumentNumber "1.50" ])

let private typeAttributes line = {
    Source = Some(source line)
    Constraints = Some(DocumentObject(map [ "min", DocumentNumber "0" ]))
    Extensions = Some extensions
}

let private valueAttributes line = {
    Source = Some(source line)
    InferredType = Some intType
    Extensions = Some extensions
}

/// Every type expression, with attributes and without.
let private everyType (attributes: TypeAttributes) =
    let a = attributes

    Type.Function(
        a,
        Type.Tuple(
            a,
            [
                Type.Variable(a, name "a")
                Type.Variable(a, Name [ "u"; "s"; "d" ])
                Type.Reference(a, fq "sdk" "list" "list", [ Type.Variable(a, name "a") ])
                Type.Reference(a, fq "sdk" "basics" "int", [])
            ]
        ),
        Type.Record(
            a,
            [
                name "name", Type.ExtensibleRecord(a, name "r", [ name "email", Type.Unit a ])
                name "fields", Type.Tuple(a, [])
            ]
        )
    )

/// Every pattern.
let private everyPattern (a: ValueAttributes) =
    TuplePattern(
        a,
        [
            WildcardPattern a
            AsPattern(a, WildcardPattern a, name "x")
            ConstructorPattern(a, fq "sdk" "maybe" "just", [ EmptyListPattern a ])
            HeadTailPattern(a, LiteralPattern(a, CharLiteral "😀"), UnitPattern a)
            LiteralPattern(a, StringLiteral "s")
        ]
    )

let private documented value : Documented<_> = { Doc = Some "Docs."; Value = value }

/// Every value expression and every value definition body.
let private everyValue (a: ValueAttributes) =
    let variable = Value.Variable(a, name "x")
    let literal value = Value.Literal(a, value)

    let floatValue =
        match FloatValue.create 0.5 (Some "0.5") with
        | Ok value -> value
        | Error message -> failtest message

    let definition body : Documented<ValueDefinition> =
        documented
            {
                InputTypes = [ name "x", intType ]
                Body = body
            }

    let bodies = [
        ExpressionBody(intType, variable)
        NativeBody(
            intType,
            {
                Hint = PlatformSpecific "jvm"
                Description = Some "Native."
            }
        )
        NativeBody(
            intType,
            {
                Hint = Arithmetic
                Description = None
            }
        )
        ExternalBody(
            intType,
            {
                Externals = [
                    {
                        TargetPlatform = "javascript"
                        ExternalName = "Math.abs"
                    }
                    {
                        TargetPlatform = "erlang"
                        ExternalName = "math:abs"
                    }
                ]
                Fallback = Some variable
            }
        )
        IncompleteBody(None, Incompleteness.Draft, Some variable)
        IncompleteBody(
            Some intType,
            Incompleteness.Hole(TypeMismatch("int", "string"), Some intType),
            None
        )
    ]

    Value.Tuple(
        a,
        [
            literal (BoolLiteral false)
            literal (CharLiteral "c")
            literal (StringLiteral "It's \"quoted\" and ünïcode")
            literal (IntegerLiteral(BigInteger.Parse "-18446744073709551616"))
            literal (FloatLiteral floatValue)
            literal (DecimalLiteral(decimalOf "-0.00"))
            literal (DocumentLiteral(DocumentArray [ DocumentNull; DocumentBool true; extensions ]))
            Value.Constructor(a, fq "sdk" "maybe" "just")
            Value.List(a, [ variable; Value.Unit a ])
            Value.Record(a, [ name "name", variable; name "fields", Value.Unit a ])
            Value.Reference(a, fq "sdk" "basics" "add")
            Value.Field(a, variable, name "name")
            Value.FieldFunction(a, name "name")
            Value.Apply(a, Value.Reference(a, fq "sdk" "basics" "negate"), variable)
            Value.Lambda(a, everyPattern a, variable)
            Value.LetDefinition(a, name "y", definition bodies.Head, variable)
            Value.LetRecursion(
                a,
                map (
                    bodies
                    |> List.mapi (fun index body -> Name [ "f"; string index ], definition body)
                ),
                variable
            )
            Value.Destructure(a, everyPattern a, variable, variable)
            Value.IfThenElse(a, literal (BoolLiteral true), variable, variable)
            Value.PatternMatch(
                a,
                variable,
                [ everyPattern a, variable; WildcardPattern a, variable ]
            )
            Value.UpdateRecord(a, variable, [ name "name", variable ])
            Value.Hole(a, UnresolvedReference(fq "sdk" "basics" "gone"), Some intType)
            Value.Hole(a, DeletedDuringRefactor "tx-1", None)
        ]
    )

let private everyTypeDefinition =
    map [
        name "alias",
        {
            Access = Public
            Value = documented (TypeAliasDefinition([ name "a" ], everyType TypeAttributes.empty))
        }
        name "custom",
        {
            Access = Private
            Value = {
                Doc = None
                Value =
                    CustomTypeDefinition(
                        [],
                        Private,
                        map [ name "just", [ name "value", intType ]; name "nothing", [] ]
                    )
            }
        }
        name "incomplete",
        {
            Access = Public
            Value =
                documented (
                    IncompleteTypeDefinition(
                        [],
                        Incompleteness.Hole(UnresolvedReference(fq "sdk" "basics" "gone"), None),
                        Some intType
                    )
                )
        }
    ]

let private annotation (local: string list) arguments = {
    Name =
        { fq "acme" "annotations" "x" with
            LocalName = Name local
        }
    Arguments = arguments
}

let private everyAnnotation = [
    annotation [ "pure" ] []
    annotation [ "deprecated" ] [
        PositionalArgument(literalValue (StringLiteral "Use v2: renamed"))
    ]
    annotation [ "empty"; "text" ] [ PositionalArgument(literalValue (StringLiteral "")) ]
    annotation [ "since" ] [
        PositionalArgument(literalValue (IntegerLiteral BigInteger.One))
        NamedArgument(name "reason", literalValue (StringLiteral "renamed"))
    ]
]

let private everyModuleSpecification: ModuleSpecification = {
    Annotations = everyAnnotation
    Types =
        map [
            name "alias",
            documented (TypeAliasSpecification(everyAnnotation, [ name "a" ], intType))
            name "opaque",
            {
                Doc = None
                Value = OpaqueTypeSpecification([], [])
            }
            Name [ "opaque"; "with"; "params" ],
            {
                Doc = None
                Value = OpaqueTypeSpecification([], [ name "a" ])
            }
            name "custom",
            documented (CustomTypeSpecification([], [ name "a" ], map [ name "none", [] ]))
            name "derived",
            documented (
                DerivedTypeSpecification(
                    everyAnnotation,
                    [],
                    intType,
                    fq "acme" "main" "from-int",
                    fq "acme" "main" "to-int"
                )
            )
        ]
    Values =
        map [
            name "f",
            documented
                {
                    Annotations = everyAnnotation
                    Inputs = [ name "x", intType ]
                    Output = intType
                }
            name "g",
            {
                Doc = None
                Value = {
                    Annotations = []
                    Inputs = []
                    Output = intType
                }
            }
        ]
    Doc = Some "The spec module."
}

let private everyPackageDefinition (attributes: ValueAttributes) : PackageDefinition = {
    Modules =
        map [
            ModulePath(Morphir.IR.Path [ name "main"; Name [ "f"; "r"; "2052"; "a" ] ]),
            {
                Access = Public
                Value = {
                    Types = everyTypeDefinition
                    Values =
                        map [
                            name "every",
                            {
                                Access = Private
                                Value = expressionDefinition intType (everyValue attributes)
                            }
                        ]
                    Doc = Some "The main module."
                }
            }
        ]
}

let private everyDistribution attributes = [
    "Library",
    Library(
        PackageName(Morphir.IR.Path [ name "acme"; Name [ "s"; "d"; "k" ] ]),
        map [
            PackageName(Morphir.IR.Path [ name "dep" ]),
            {
                Modules =
                    map [ ModulePath(Morphir.IR.Path [ name "util" ]), everyModuleSpecification ]
            }
        ],
        everyPackageDefinition attributes
    )
    "Specs",
    Specs(
        PackageName(Morphir.IR.Path [ name "acme" ]),
        OrderedMap.empty,
        {
            Modules = map [ ModulePath(Morphir.IR.Path [ name "main" ]), everyModuleSpecification ]
        }
    )
    "Application",
    Application(
        PackageName(Morphir.IR.Path [ name "acme" ]),
        map [ PackageName(Morphir.IR.Path [ name "dep" ]), everyPackageDefinition attributes ],
        everyPackageDefinition attributes,
        [
            "start",
            {
                Target = fq "acme" "main" "every"
                Kind = Main
                Doc = Some "Starts."
            }
            "build",
            {
                Target = fq "acme" "main" "every"
                Kind = Command
                Doc = None
            }
            "serve",
            {
                Target = fq "acme" "main" "every"
                Kind = Handler
                Doc = None
            }
            "nightly",
            {
                Target = fq "acme" "main" "every"
                Kind = Job
                Doc = None
            }
            "rule",
            {
                Target = fq "acme" "main" "every"
                Kind = Policy
                Doc = None
            }
        ]
    )
]

let private modelTests =
    testList "Whole model round trip" [
        for label, attributes, typeAttributesOf in
            [
                "without attributes", ValueAttributes.empty, TypeAttributes.empty
                "with attributes", valueAttributes 7, typeAttributes 3
            ] do
            testCase $"Every type {label}"
            <| fun () ->
                let model = everyType typeAttributesOf
                let bytes = written (V4JsonWriter.writeType false model)
                Expect.equal (readWith V4Json.readType (text bytes)) model "reads back"

            testCase $"Every pattern {label}"
            <| fun () ->
                let model = everyPattern attributes
                let bytes = written (V4JsonWriter.writePattern false model)
                Expect.equal (readWith V4Json.readPattern (text bytes)) model "reads back"

            testCase $"Every value {label}"
            <| fun () ->
                let model = everyValue attributes
                let bytes = written (V4JsonWriter.writeValue false model)
                Expect.equal (readWith V4Json.readValue (text bytes)) model "reads back"

            for kind, distribution in everyDistribution attributes do
                testCase $"Every {kind} node {label}"
                <| fun () ->
                    let bytes = written (V4JsonWriter.writeDistribution false distribution)
                    let read = readWith V4Json.readDistribution (text bytes)
                    Expect.equal read.Distribution distribution "reads back"
                    Expect.equal (rewrite read) bytes "Writing again gives identical bytes"

                    let indented = written (V4JsonWriter.writeDistribution true distribution)

                    Expect.equal
                        (readWith V4Json.readDistribution (text indented)).Distribution
                        distribution
                        "The indented document reads back"

        testCase "Attributes come first in an expanded payload"
        <| fun () ->
            let json =
                text (
                    written (
                        V4JsonWriter.writeValue false (Value.Variable(valueAttributes 3, name "x"))
                    )
                )

            Expect.equal
                json
                "{\"Variable\":{\"attributes\":{\"source\":{\"startLine\":3,\"startColumn\":1,\"endLine\":3,\"endColumn\":2},\"inferredType\":\"sdk:basics#int\",\"extensions\":{\"tag\":\"x\",\"weight\":1.50}},\"name\":\"x\"}}"
                "The expanded spelling"

        testCase "A type variable that is not a bare variable keeps its wrapper"
        <| fun () ->
            Expect.equal
                (text (
                    written (
                        V4JsonWriter.writeType
                            false
                            (Type.Variable(TypeAttributes.empty, Name [ "u"; "s"; "d" ]))
                    )
                ))
                "{\"Variable\":\"USD\"}"
                "The wrapped variable"
    ]

let private refusalTests =
    let refuses rule result =
        Expect.equal (refusal result).Rule rule "The rule"

    testList "Refusals" [
        testCase "A document in a literal pattern"
        <| fun () ->
            V4JsonWriter.writePattern
                false
                (LiteralPattern(ValueAttributes.empty, DocumentLiteral DocumentNull))
            |> refuses "invalid_literal"

        testCase "An external body with no binding"
        <| fun () ->
            let definition = {
                InputTypes = []
                Body = ExternalBody(intType, { Externals = []; Fallback = None })
            }

            V4JsonWriter.writeValue
                false
                (Value.LetDefinition(
                    ValueAttributes.empty,
                    name "x",
                    { Doc = None; Value = definition },
                    Value.Unit ValueAttributes.empty
                ))
            |> refuses "invalid_type"

        testCase "A record field that repeats"
        <| fun () ->
            V4JsonWriter.writeType
                false
                (Type.Record(TypeAttributes.empty, [ name "a", intType; name "a", intType ]))
            |> refuses "duplicate_member"

        testCase "A name with no canonical spelling"
        <| fun () ->
            let error =
                refusal (
                    V4JsonWriter.writeValue
                        false
                        (Value.Variable(ValueAttributes.empty, Name [ "Upper" ]))
                )

            Expect.equal error.Rule "invalid_name" "The rule"
            Expect.equal error.Location "$.Variable" "The location"

        testCase "A float lexeme that spells another value"
        <| fun () ->
            match FloatValue.create 1.5 (Some "2.5") with
            | Ok value ->
                V4JsonWriter.writeValue false (literalValue (FloatLiteral value))
                |> refuses "invalid_literal"
            | Error message -> failtest message

        testCase "A document number that is not a JSON number"
        <| fun () ->
            V4JsonWriter.writeValue false (literalValue (DocumentLiteral(DocumentNumber "1_000")))
            |> refuses "invalid_number"

        testCase "A string with a lone surrogate"
        <| fun () ->
            V4JsonWriter.writeValue false (literalValue (StringLiteral(string (char 0xD800))))
            |> refuses "invalid_text"

        testCase "An entry point that repeats"
        <| fun () ->
            let entry = {
                Target = fq "acme" "main" "run"
                Kind = Main
                Doc = None
            }

            V4JsonWriter.writeDistribution
                false
                (Application(
                    PackageName(Morphir.IR.Path [ name "acme" ]),
                    OrderedMap.empty,
                    { Modules = OrderedMap.empty },
                    [ "start", entry; "start", entry ]
                ))
            |> refuses "duplicate_member"
    ]

[<Tests>]
let tests =
    testList "IR v4 JSON writer" [
        canonicalTests
        fixtureTests
        exactnessTests
        layoutTests
        modelTests
        refusalTests
    ]
