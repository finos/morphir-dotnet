namespace Morphir.CodeGeneration.FSharp

open System
open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Documented

module internal Decode =
    type Model = {
        Version: int
        Package: PackageName
        Definition: Package.PackageDefinition<JsonElement, JsonElement>
    }

    let fail category location message =
        raise (GenerationFailure(category, location, message))

    let arr location (element: JsonElement) =
        if
            element.ValueKind
            <> JsonValueKind.Array
        then
            fail "decode" location "Expected an array."

        element.EnumerateArray()
        |> Seq.toList

    let text location (element: JsonElement) =
        if
            element.ValueKind
            <> JsonValueKind.String
        then
            fail "decode" location "Expected a string."

        element.GetString()

    let prop location (name: string) (element: JsonElement) =
        let mutable result = Unchecked.defaultof<JsonElement>

        if
            element.ValueKind
            <> JsonValueKind.Object
            || not (element.TryGetProperty(name, &result))
        then
            fail "decode" location $"Missing required field '{name}'."

        result

    let item location i element =
        let values = arr location element

        if
            i
            >= values.Length
        then
            fail "decode" location $"Missing item {i}."

        values[i]

    let shape location count element =
        let values = arr location element

        if
            values.Length
            <> count
        then
            fail "decode" location $"Expected {count} array items, found {values.Length}."

    let name location element =
        let words =
            arr location element
            |> List.map (text location)

        if
            List.isEmpty words
            || words
               |> List.exists (fun word ->
                   String.IsNullOrEmpty word
                   || not (Char.IsLower word[0])
                   || word
                      |> Seq.exists (fun c ->
                          not (
                              c >= 'a'
                              && c <= 'z'
                              || c >= '0'
                                 && c <= '9'
                          )
                      )
               )
        then
            fail "invalid-name" location "Names must contain nonempty lowercase alphanumeric words."

        Name words

    let path location element =
        let names =
            arr location element
            |> List.map (name location)

        if List.isEmpty names then
            fail "invalid-name" location "Paths must contain at least one name."

        Path.fromList names

    let fq location element =
        shape location 3 element

        {
            PackagePath = PackageName.packageName (path location (item location 0 element))
            ModulePath = ModulePath.modulePath (path location (item location 1 element))
            LocalName = name location (item location 2 element)
        }

    let snake (value: string) =
        value
        |> Seq.mapi (fun i c ->
            (if
                 Char.IsUpper c
                 && i > 0
             then
                 "_"
             else
                 "")
            + string (Char.ToLowerInvariant c)
        )
        |> String.concat ""

    let tag version family expected =
        if
            version = 1
            || version = 2
               && (family = "value"
                   || family = "pattern"
                   || family = "literal")
        then
            snake expected
        else
            expected

    let tagged version family location element =
        let actual =
            item location 0 element
            |> text location

        fun expected -> actual = tag version family expected

    let map location decodeKey decodeValue element =
        arr location element
        |> List.mapi (fun i entry ->
            let here = $"{location}[{i}]"
            shape here 2 entry
            decodeKey here (item here 0 entry), decodeValue here (item here 1 entry)
        )
        |> List.fold
            (fun result (key, value) ->
                if Map.containsKey key result then
                    fail "duplicate-key" location "Duplicate dictionary key."

                Map.add key value result
            )
            Map.empty

    let access version location decoder element =
        let accessElement, content =
            if version = 1 then
                shape location 2 element
                item location 0 element, item location 1 element
            else
                prop location "access" element, prop location "value" element

        let accessText = text location accessElement

        let access =
            if accessText = (if version = 1 then "public" else "Public") then
                Public
            elif accessText = (if version = 1 then "private" else "Private") then
                Private
            else
                fail "decode" location $"Invalid access '{accessText}'."

        {
            Access = access
            Value = decoder location content
        }

    let documented location decoder (element: JsonElement) =
        let mutable doc = Unchecked.defaultof<JsonElement>

        if
            element.ValueKind = JsonValueKind.Object
            && element.TryGetProperty("doc", &doc)
        then
            WithDocumentation(text location doc, decoder location (prop location "value" element))
        else
            WithoutDocumentation(decoder location element)

    let unwrap documented =
        match documented with
        | WithDocumentation(_, v)
        | WithoutDocumentation v -> v

    let rec typ version location element : Type<JsonElement> =
        let is = tagged version "type" location element
        let at i = item location i element
        let attributes = at 1
        let nested i = typ version ($"{location}[{i}]") (at i)

        let fields i =
            arr location (at i)
            |> List.map (fun field -> {
                Name = name location (prop location "name" field)
                Type = typ version location (prop location "tpe" field)
            })

        if is "Variable" then
            shape location 3 element
            Type.Variable(attributes, name location (at 2))
        elif is "Reference" then
            shape location 4 element

            Type.Reference(
                attributes,
                fq location (at 2),
                arr location (at 3)
                |> List.map (typ version location)
            )
        elif is "Tuple" then
            shape location 3 element

            Type.Tuple(
                attributes,
                arr location (at 2)
                |> List.map (typ version location)
            )
        elif is "Record" then
            shape location 3 element
            Type.Record(attributes, fields 2)
        elif is "ExtensibleRecord" then
            shape location 4 element
            Type.ExtensibleRecord(attributes, name location (at 2), fields 3)
        elif is "Function" then
            shape location 4 element
            Type.Function(attributes, nested 2, nested 3)
        elif is "Unit" then
            shape location 2 element
            Type.Unit attributes
        else
            fail "decode" location "Unknown or version-incompatible type tag."

    let literal version location element =
        shape location 2 element
        let is = tagged version "literal" location element
        let data = item location 1 element

        if is "BoolLiteral" then
            BoolLiteral(data.GetBoolean())
        elif is "CharLiteral" then
            let value = text location data

            if
                value.Length
                <> 1
            then
                fail "decode" location "Expected one UTF-16 character."

            CharLiteral value[0]
        elif is "StringLiteral" then
            StringLiteral(text location data)
        elif is "WholeNumberLiteral" then
            WholeNumberLiteral(data.GetInt64())
        elif is "FloatLiteral" then
            FloatLiteral(data.GetDouble())
        elif is "DecimalLiteral" then
            DecimalLiteral(
                if data.ValueKind = JsonValueKind.String then
                    Decimal.Parse(text location data, Globalization.CultureInfo.InvariantCulture)
                else
                    data.GetDecimal()
            )
        else
            fail "decode" location "Unknown or version-incompatible literal tag."

    let rec pattern version location element : Pattern<JsonElement> =
        let is = tagged version "pattern" location element
        let at i = item location i element
        let a = at 1
        let p i = pattern version location (at i)

        if is "WildcardPattern" then
            shape location 2 element
            WildcardPattern a
        elif is "AsPattern" then
            shape location 4 element
            AsPattern(a, p 2, name location (at 3))
        elif is "TuplePattern" then
            shape location 3 element

            TuplePattern(
                a,
                arr location (at 2)
                |> List.map (pattern version location)
            )
        elif is "ConstructorPattern" then
            shape location 4 element

            ConstructorPattern(
                a,
                fq location (at 2),
                arr location (at 3)
                |> List.map (pattern version location)
            )
        elif is "EmptyListPattern" then
            shape location 2 element
            EmptyListPattern a
        elif is "HeadTailPattern" then
            shape location 4 element
            HeadTailPattern(a, p 2, p 3)
        elif is "LiteralPattern" then
            shape location 3 element
            LiteralPattern(a, literal version location (at 2))
        elif is "UnitPattern" then
            shape location 2 element
            UnitPattern a
        else
            fail "decode" location "Unknown or version-incompatible pattern tag."

    let rec value version location element : Value<JsonElement, JsonElement> =
        let is = tagged version "value" location element
        let at i = item location i element
        let a = at 1

        let v i =
            value version ($"{location}[{i}]") (at i)

        let vs i =
            arr location (at i)
            |> List.map (value version location)

        let fields i =
            map location name (value version) (at i)

        if is "Literal" then
            shape location 3 element
            Value.Literal(a, literal version location (at 2))
        elif is "Constructor" then
            shape location 3 element
            Value.Constructor(a, fq location (at 2))
        elif is "Tuple" then
            shape location 3 element
            Value.Tuple(a, vs 2)
        elif is "List" then
            shape location 3 element
            Value.List(a, vs 2)
        elif is "Record" then
            shape location 3 element
            Value.Record(a, fields 2)
        elif is "Variable" then
            shape location 3 element
            Value.Variable(a, name location (at 2))
        elif is "Reference" then
            shape location 3 element
            Value.Reference(a, fq location (at 2))
        elif is "Field" then
            shape location 4 element
            Value.Field(a, v 2, name location (at 3))
        elif is "FieldFunction" then
            shape location 3 element
            Value.FieldFunction(a, name location (at 2))
        elif is "Apply" then
            shape location 4 element
            Value.Apply(a, v 2, v 3)
        elif is "Lambda" then
            shape location 4 element
            Value.Lambda(a, pattern version location (at 2), v 3)
        elif is "LetDefinition" then
            shape location 5 element
            Value.LetDefinition(a, name location (at 2), definition version location (at 3), v 4)
        elif is "LetRecursion" then
            shape location 4 element
            Value.LetRecursion(a, map location name (definition version) (at 2), v 3)
        elif is "Destructure" then
            shape location 5 element
            Value.Destructure(a, pattern version location (at 2), v 3, v 4)
        elif is "IfThenElse" then
            shape location 5 element
            Value.IfThenElse(a, v 2, v 3, v 4)
        elif is "PatternMatch" then
            shape location 4 element

            let cases =
                arr location (at 3)
                |> List.map (fun pair ->
                    shape location 2 pair

                    pattern version location (item location 0 pair),
                    value version location (item location 1 pair)
                )

            Value.PatternMatch(a, v 2, cases)
        elif is "UpdateRecord" then
            shape location 4 element
            Value.UpdateRecord(a, v 2, fields 3)
        elif is "Unit" then
            shape location 2 element
            Value.Unit a
        else
            fail "decode" location "Unknown or version-incompatible value tag."

    and definition version location element : ValueDefinition<JsonElement, JsonElement> = {
        InputTypes =
            arr location (prop location "inputTypes" element)
            |> List.map (fun input ->
                shape location 3 input

                name location (item location 0 input),
                item location 1 input,
                typ version location (item location 2 input)
            )
        OutputType = typ version location (prop location "outputType" element)
        Body =
            value
                version
                (location
                 + ".body")
                (prop location "body" element)
    }

    let typeDefinition version location element : Type.TypeDefinition<JsonElement> =
        shape location 3 element
        let is = tagged version "type" location element

        let parameters =
            arr location (item location 1 element)
            |> List.map (name location)

        if is "TypeAliasDefinition" then
            Type.TypeAliasDefinition(parameters, typ version location (item location 2 element))
        elif is "CustomTypeDefinition" then
            let constructors location =
                map
                    location
                    name
                    (fun location args ->
                        arr location args
                        |> List.map (fun arg ->
                            shape location 2 arg

                            name location (item location 0 arg),
                            typ version location (item location 1 arg)
                        )
                    )

            Type.CustomTypeDefinition(
                parameters,
                access version location constructors (item location 2 element)
            )
        else
            fail "decode" location "Unknown or version-incompatible type definition."

    let moduleDefinition
        version
        location
        element
        : Module.ModuleDefinition<JsonElement, JsonElement> =
        {
            Types =
                map
                    (location
                     + ".types")
                    name
                    (fun location ->
                        access
                            version
                            location
                            (fun location -> documented location (typeDefinition version))
                    )
                    (prop location "types" element)
            Values =
                map
                    (location
                     + ".values")
                    name
                    (fun location ->
                        access
                            version
                            location
                            (fun location -> documented location (definition version))
                    )
                    (prop location "values" element)
            Doc = None
        }

    let model (root: JsonElement) =
        let mutable element = Unchecked.defaultof<JsonElement>
        let mutable version = 0

        if
            root.ValueKind
            <> JsonValueKind.Object
            || not (root.TryGetProperty("formatVersion", &element))
            || element.ValueKind
               <> JsonValueKind.Number
            || not (element.TryGetInt32(&version))
            || version < 1
            || version > 3
        then
            fail
                "unsupported-version"
                "$.formatVersion"
                "Expected an explicit integer classic IR version 1, 2 or 3."

        let distribution = prop "$" "distribution" root
        shape "$.distribution" 4 distribution

        if
            text "$.distribution[0]" (item "$" 0 distribution)
            <> (if version = 1 then "library" else "Library")
        then
            fail
                "unsupported-distribution"
                "$.distribution"
                "Only classic Library JSON versions 1, 2 and 3 are supported; document trees are not supported."

        let package =
            PackageName.packageName (path "$.distribution[1]" (item "$" 1 distribution))
        // Dependency specifications never supply executable definitions. Validate their dictionary shape;
        // references to anything outside this package or the SDK are rejected by resolution.
        map
            "$.distribution[2]"
            (fun location e -> path location e)
            (fun location e ->
                prop location "modules" e
                |> arr location
                |> ignore
            )
            (item "$" 2 distribution)
        |> ignore

        let packageElement = item "$" 3 distribution
        let moduleElements = prop "$.distribution[3]" "modules" packageElement

        let modules =
            if version = 1 then
                arr "$.distribution[3].modules" moduleElements
                |> List.mapi (fun i entry ->
                    let location = $"$.distribution[3].modules[{i}]"

                    ModulePath.modulePath (path location (prop location "name" entry)),
                    access version location (moduleDefinition version) (prop location "def" entry)
                )
                |> List.fold
                    (fun result (key, data) ->
                        if Map.containsKey key result then
                            fail "duplicate-key" "$" "Duplicate module."

                        Map.add key data result
                    )
                    Map.empty
            else
                map
                    "$.distribution[3].modules"
                    (fun location e -> ModulePath.modulePath (path location e))
                    (fun location -> access version location (moduleDefinition version))
                    moduleElements

        {
            Version = version
            Package = package
            Definition = { Modules = modules }
        }
