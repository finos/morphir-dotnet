module Morphir.Json.Tests.ClassicJsonWriterTests

open System.IO
open System.Text
open System.Text.Json
open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Distribution
open Morphir.IR.Classic.Documented
open Morphir.Json.Classic

/// Maps attributes read from JSON to their minified text, so models from different documents compare by content.
module private Normalize =
    let attribute (element: JsonElement) =
        use stream = new MemoryStream()

        (using
            (new Utf8JsonWriter(stream))
            (fun writer ->
                element.WriteTo writer
                writer.Flush()
            ))

        Encoding.UTF8.GetString(stream.ToArray())

    let rec typ (t: Type<JsonElement>) : Type<string> =
        let fields =
            List.map (fun (f: Field<JsonElement>) -> { Name = f.Name; Type = typ f.Type })

        match t with
        | Type.Variable(a, n) -> Type.Variable(attribute a, n)
        | Type.Reference(a, fq, args) -> Type.Reference(attribute a, fq, List.map typ args)
        | Type.Tuple(a, items) -> Type.Tuple(attribute a, List.map typ items)
        | Type.Record(a, fs) -> Type.Record(attribute a, fields fs)
        | Type.ExtensibleRecord(a, n, fs) -> Type.ExtensibleRecord(attribute a, n, fields fs)
        | Type.Function(a, x, y) -> Type.Function(attribute a, typ x, typ y)
        | Type.Unit a -> Type.Unit(attribute a)

    let rec pattern (p: Pattern<JsonElement>) : Pattern<string> =
        match p with
        | WildcardPattern a -> WildcardPattern(attribute a)
        | AsPattern(a, inner, n) -> AsPattern(attribute a, pattern inner, n)
        | TuplePattern(a, items) -> TuplePattern(attribute a, List.map pattern items)
        | ConstructorPattern(a, fq, args) ->
            ConstructorPattern(attribute a, fq, List.map pattern args)
        | EmptyListPattern a -> EmptyListPattern(attribute a)
        | HeadTailPattern(a, h, t) -> HeadTailPattern(attribute a, pattern h, pattern t)
        | LiteralPattern(a, l) -> LiteralPattern(attribute a, l)
        | UnitPattern a -> UnitPattern(attribute a)

    let rec value (v: Value<JsonElement, JsonElement>) : Value<string, string> =
        let a = attribute

        match v with
        | Value.Literal(x, l) -> Value.Literal(a x, l)
        | Value.Constructor(x, fq) -> Value.Constructor(a x, fq)
        | Value.Tuple(x, items) -> Value.Tuple(a x, List.map value items)
        | Value.List(x, items) -> Value.List(a x, List.map value items)
        | Value.Record(x, fs) -> Value.Record(a x, Map.map (fun _ v -> value v) fs)
        | Value.Variable(x, n) -> Value.Variable(a x, n)
        | Value.Reference(x, fq) -> Value.Reference(a x, fq)
        | Value.Field(x, target, n) -> Value.Field(a x, value target, n)
        | Value.FieldFunction(x, n) -> Value.FieldFunction(a x, n)
        | Value.Apply(x, f, arg) -> Value.Apply(a x, value f, value arg)
        | Value.Lambda(x, p, body) -> Value.Lambda(a x, pattern p, value body)
        | Value.LetDefinition(x, n, d, inValue) ->
            Value.LetDefinition(a x, n, definition d, value inValue)
        | Value.LetRecursion(x, ds, inValue) ->
            Value.LetRecursion(a x, Map.map (fun _ d -> definition d) ds, value inValue)
        | Value.Destructure(x, p, target, inValue) ->
            Value.Destructure(a x, pattern p, value target, value inValue)
        | Value.IfThenElse(x, c, t, e) -> Value.IfThenElse(a x, value c, value t, value e)
        | Value.PatternMatch(x, subject, cases) ->
            Value.PatternMatch(
                a x,
                value subject,
                cases
                |> List.map (fun (p, v) -> pattern p, value v)
            )
        | Value.UpdateRecord(x, target, fs) ->
            Value.UpdateRecord(a x, value target, Map.map (fun _ v -> value v) fs)
        | Value.Unit x -> Value.Unit(a x)

    and definition
        (d: ValueDefinition<JsonElement, JsonElement>)
        : ValueDefinition<string, string> =
        {
            InputTypes =
                d.InputTypes
                |> List.map (fun (n, a, t) -> n, attribute a, typ t)
            OutputType = typ d.OutputType
            Body = value d.Body
        }

    let constructors (cs: Type.Constructors<JsonElement>) : Type.Constructors<string> =
        cs
        |> Map.map (fun _ args ->
            args
            |> List.map (fun (n, t) -> n, typ t)
        )

    let typeDefinition (d: Type.TypeDefinition<JsonElement>) =
        match d with
        | Type.TypeAliasDefinition(ps, t) -> Type.TypeAliasDefinition(ps, typ t)
        | Type.CustomTypeDefinition(ps, cs) ->
            Type.CustomTypeDefinition(
                ps,
                {
                    Access = cs.Access
                    Value = constructors cs.Value
                }
            )

    let typeSpecification (s: Type.TypeSpecification<JsonElement>) =
        match s with
        | Type.TypeAliasSpecification(ps, t) -> Type.TypeAliasSpecification(ps, typ t)
        | Type.OpaqueTypeSpecification ps -> Type.OpaqueTypeSpecification ps
        | Type.CustomTypeSpecification(ps, cs) -> Type.CustomTypeSpecification(ps, constructors cs)
        | Type.DerivedTypeSpecification(ps, d) ->
            Type.DerivedTypeSpecification(
                ps,
                {
                    BaseType = typ d.BaseType
                    FromBaseType = d.FromBaseType
                    ToBaseType = d.ToBaseType
                }
            )

    let documentedMap f (d: Documented<_>) =
        match d with
        | WithDocumentation(doc, v) -> WithDocumentation(doc, f v)
        | WithoutDocumentation v -> WithoutDocumentation(f v)

    let moduleSpecification
        (m: Module.ModuleSpecification<JsonElement>)
        : Module.ModuleSpecification<string> =
        {
            Types =
                m.Types
                |> Map.map (fun _ d -> documentedMap typeSpecification d)
            Values =
                m.Values
                |> Map.map (fun _ d ->
                    documentedMap
                        (fun (s: ValueSpecification<JsonElement>) -> {
                            Inputs =
                                s.Inputs
                                |> List.map (fun (n, t) -> n, typ t)
                            Output = typ s.Output
                        })
                        d
                )
            Doc = m.Doc
        }

    let moduleDefinition
        (m: Module.ModuleDefinition<JsonElement, JsonElement>)
        : Module.ModuleDefinition<string, string> =
        {
            Types =
                m.Types
                |> Map.map (fun _ c -> {
                    Access = c.Access
                    Value = documentedMap typeDefinition c.Value
                })
            Values =
                m.Values
                |> Map.map (fun _ c -> {
                    Access = c.Access
                    Value = documentedMap definition c.Value
                })
            Doc = m.Doc
        }

    let distribution (d: Distribution<JsonElement, JsonElement>) : Distribution<string, string> =
        match d with
        | Library(name, dependencies, definition) ->
            Library(
                name,
                dependencies
                |> Map.map (fun _ s ->
                    {
                        Modules =
                            s.Modules
                            |> Map.map (fun _ m -> moduleSpecification m)
                    }
                    : Package.PackageSpecification<string>
                ),
                {
                    Modules =
                        definition.Modules
                        |> Map.map (fun _ c -> {
                            Access = c.Access
                            Value = moduleDefinition c.Value
                        })
                }
            )
        | Specs(name, dependencies, specification) ->
            Specs(
                name,
                dependencies
                |> Map.map (fun _ s ->
                    {
                        Modules =
                            s.Modules
                            |> Map.map (fun _ m -> moduleSpecification m)
                    }
                    : Package.PackageSpecification<string>
                ),
                {
                    Modules =
                        specification.Modules
                        |> Map.map (fun _ m -> moduleSpecification m)
                }
            )

let private root = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..")

let private fixtures = [
    "examples/car-rental/models/car-rental-v1.json"
    "examples/car-rental/models/car-rental-v2.json"
    "examples/car-rental/models/car-rental-v3.json"
    "tests/Fixtures/MorphirIR/daily-rate-v3.json"
    "tests/Fixtures/MorphirIon/pairs/v3-daily-rate.json"
    "tests/Fixtures/MorphirIon/pairs/v3-library-nodes.json"
    "tests/Fixtures/MorphirIon/pairs/greeting-example.json"
    "tests/Fixtures/MorphirIon/pairs/rule-set-example.json"
    "tests/Fixtures/MorphirIon/pairs/direct-rules-example.json"
    "tests/Morphir.Tooling.Tests/TestData/valid-ir-v1.json"
    "tests/Morphir.Tooling.Tests/TestData/valid-ir-v2.json"
    "tests/Morphir.Tooling.Tests/TestData/valid-ir-v3.json"
]

let private profile version =
    match version with
    | 1 -> ClassicWireProfile.v1
    | 2 -> ClassicWireProfile.v2
    | 3 -> ClassicWireProfile.v3
    | other -> failtest $"Unexpected classic version {other}"

/// Reads bytes and returns the version and the normalized model, keeping the document alive while it is read.
let private readNormalized (bytes: byte array) =
    use document = JsonDocument.Parse(System.ReadOnlyMemory bytes)

    match ClassicJson.readDistribution document.RootElement with
    | Ok read -> read.Version, Normalize.distribution read.Distribution
    | Error error -> failtest $"{error.Rule} at {error.Location}: {error.Message}"

let private writeRead (bytes: byte array) =
    use document = JsonDocument.Parse(System.ReadOnlyMemory bytes)

    match ClassicJson.readDistribution document.RootElement with
    | Ok read ->
        match
            ClassicJsonWriter.writeJsonAttributes (profile read.Version) false read.Distribution
        with
        | Ok written -> read.Version, written
        | Error error -> failtest $"{error.Rule}: {error.Message}"
    | Error error -> failtest $"{error.Rule} at {error.Location}: {error.Message}"

let private typedSample =
    let n (text: string) = Name.fromString text
    let pkg = PackageName.packageName (Path.fromList [ n "acme"; n "demo" ])

    let intType =
        Type.Reference(
            (),
            {
                PackagePath =
                    PackageName.packageName (Path.fromList [ n "morphir"; Name [ "s"; "d"; "k" ] ])
                ModulePath = ModulePath.modulePath (Path.fromList [ n "basics" ])
                LocalName = n "int"
            },
            []
        )

    let body =
        Value.LetDefinition(
            intType,
            n "base",
            {
                InputTypes = []
                OutputType = intType
                Body = Value.Literal(intType, WholeNumberLiteral 60L)
            },
            Value.Variable(intType, n "base")
        )

    let definition: ValueDefinition<unit, Type<unit>> = {
        InputTypes = []
        OutputType = intType
        Body = body
    }

    let moduleDefinition: Module.ModuleDefinition<unit, Type<unit>> = {
        Types = Map.empty
        Values =
            Map.ofList [
                n "rate",
                {
                    Access = Public
                    Value = WithoutDocumentation definition
                }
            ]
        Doc = None
    }

    Library(
        pkg,
        Map.empty,
        {
            Modules =
                Map.ofList [
                    ModulePath.modulePath (Path.fromList [ n "main" ]),
                    {
                        Access = Public
                        Value = moduleDefinition
                    }
                ]
        }
    )

[<Tests>]
let tests =
    testList "ClassicJsonWriter" [
        for fixture in fixtures do
            test $"round-trips {fixture} in its own version" {
                let original = File.ReadAllBytes(Path.Combine(root, fixture))
                let version, expected = readNormalized original
                let writtenVersion, written = writeRead original
                Expect.equal writtenVersion version "version"
                let roundVersion, actual = readNormalized written
                Expect.equal roundVersion version "read-back version"
                Expect.equal actual expected "the model reads back equal"
                let _, rewritten = writeRead written
                Expect.sequenceEqual rewritten written "writing is deterministic and stable"
            }
            |> testLabel "PlatformSpecific"

        test "the version-spelling scenario" {
            let text profile =
                match ClassicJsonWriter.writeTyped profile false typedSample with
                | Ok bytes -> Encoding.UTF8.GetString bytes
                | Error error -> failtest error.Message

            let v1, v2, v3 =
                text ClassicWireProfile.v1, text ClassicWireProfile.v2, text ClassicWireProfile.v3

            Expect.stringContains v1 "\"let_definition\"" "v1 value tag"
            Expect.stringContains v1 "[\"public\"," "v1 access pair"
            Expect.stringContains v1 "\"library\"" "v1 distribution tag"
            Expect.stringContains v2 "\"let_definition\"" "v2 value tag"
            Expect.stringContains v2 "{\"access\":\"Public\"" "v2 access object"
            Expect.stringContains v2 "\"Reference\"" "v2 type tag"
            Expect.stringContains v3 "\"LetDefinition\"" "v3 value tag"
            Expect.stringContains v3 "{\"access\":\"Public\"" "v3 access object"

            for text, version in [ v1, 1; v2, 2; v3, 3 ] do
                Expect.stringStarts text $"{{\"formatVersion\":{version}," "integer formatVersion"
                let readVersion, _ = readNormalized (Encoding.UTF8.GetBytes text)
                Expect.equal readVersion version "reads back"
        }

        test "indented output uses two spaces and reads back equal" {
            match
                ClassicJsonWriter.writeTyped ClassicWireProfile.v3 true typedSample,
                ClassicJsonWriter.writeTyped ClassicWireProfile.v3 false typedSample
            with
            | Ok indented, Ok compact ->
                Expect.stringContains
                    (Encoding.UTF8.GetString indented)
                    "\n  \"distribution\""
                    "two spaces"

                Expect.equal (readNormalized indented) (readNormalized compact) "same model"
            | _ -> failtest "write failed"
        }
        |> testLabel "PlatformSpecific"

        test "a Specs distribution is refused" {
            let specs: Distribution<unit, Type<unit>> =
                Specs(
                    PackageName.packageName (Path.fromList [ Name.fromString "a" ]),
                    Map.empty,
                    { Modules = Map.empty }
                )

            match ClassicJsonWriter.writeTyped ClassicWireProfile.v3 false specs with
            | Error error -> Expect.equal error.Rule "unsupported-distribution" "rule"
            | Ok _ -> failtest "Specs written"
        }

        test "a non-finite float is refused" {
            let floatType = Type.Unit()

            let library =
                match typedSample with
                | Library(pkg, deps, definition) ->
                    let m =
                        definition.Modules
                        |> Map.toList
                        |> List.head
                        |> fst

                    let value: ValueDefinition<unit, Type<unit>> = {
                        InputTypes = []
                        OutputType = floatType
                        Body = Value.Literal(floatType, FloatLiteral nan)
                    }

                    Library(
                        pkg,
                        deps,
                        {
                            Modules =
                                Map.ofList [
                                    m,
                                    {
                                        Access = Public
                                        Value = {
                                            Types = Map.empty
                                            Values =
                                                Map.ofList [
                                                    Name.fromString "x",
                                                    {
                                                        Access = Public
                                                        Value = WithoutDocumentation value
                                                    }
                                                ]
                                            Doc = None
                                        }
                                    }
                                ]
                        }
                    )
                | specs -> specs

            match ClassicJsonWriter.writeTyped ClassicWireProfile.v3 false library with
            | Error error -> Expect.equal error.Rule "literal_out_of_range" "rule"
            | Ok _ -> failtest "NaN written"
        }
    ]
