namespace Morphir.Json.Classic

open System
open System.Globalization
open System.Runtime.CompilerServices
open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Distribution
open Morphir.IR.Classic.Documented

/// <summary>Why a classic IR JSON document could not be read.</summary>
type ClassicJsonError = {
    /// <summary>A stable rule identifier, such as <c>unsupported-version</c>.</summary>
    Rule: string
    /// <summary>The JSON location of the problem.</summary>
    Location: string
    /// <summary>A readable description.</summary>
    Message: string
}

/// <summary>A complete classic IR document. Attributes stay JSON values.</summary>
type ClassicDocument = {
    /// <summary>The classic major version: 1, 2 or 3.</summary>
    Version: int
    /// <summary>The distribution.</summary>
    Distribution: Distribution<JsonElement, JsonElement>
}

exception internal ClassicJsonFailure of ClassicJsonError

/// What the decoder needs besides the document: the classic major version, and whether it reads for F#
/// generation. Generation applies the stricter name rule it always has: the public reader accepts words that
/// start with a digit, such as <c>["arg", "1"]</c>, which upstream models use, and generation refuses them.
/// Generation also keeps its older literal reading: a decimal string is parsed with no exponent, a decimal
/// that needs rounding is rounded, and a literal of the wrong kind raises where it always did.
type internal ClassicContext = { Version: int; ForGeneration: bool }

/// Hand-written decoding of classic IR JSON. It raises <see cref="ClassicJsonFailure"/>, which the public
/// reader turns into a result. It uses no runtime reflection.
module internal ClassicJsonDecoder =
    let fail rule location message =
        raise (
            ClassicJsonFailure
                {
                    Rule = rule
                    Location = location
                    Message = message
                }
        )

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

        // A JSON string never reads as null.
        nonNull (element.GetString())

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
            fail
                "decode"
                location
                ("Missing item "
                 + i.ToString(CultureInfo.InvariantCulture)
                 + ".")

        values[i]

    let shape location count element =
        let values = arr location element

        if
            values.Length
            <> count
        then
            fail
                "decode"
                location
                ("Expected "
                 + count.ToString(CultureInfo.InvariantCulture)
                 + " array items, found "
                 + values.Length.ToString(CultureInfo.InvariantCulture)
                 + ".")

    let name (ctx: ClassicContext) location element =
        let words =
            arr location element
            |> List.map (text location)

        if
            List.isEmpty words
            || words
               |> List.exists (fun word ->
                   String.IsNullOrEmpty word
                   || (ctx.ForGeneration
                       && not (Char.IsLower word[0]))
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

    let path (ctx: ClassicContext) location element =
        let names =
            arr location element
            |> List.map (name ctx location)

        if List.isEmpty names then
            fail "invalid-name" location "Paths must contain at least one name."

        Path.fromList names

    let fq (ctx: ClassicContext) location element =
        shape location 3 element

        {
            PackagePath = PackageName.packageName (path ctx location (item location 0 element))
            ModulePath = ModulePath.modulePath (path ctx location (item location 1 element))
            LocalName = name ctx location (item location 2 element)
        }

    let private snake (value: string) =
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

    let private tagged (ctx: ClassicContext) family location element =
        let actual =
            item location 0 element
            |> text location

        fun expected -> actual = tag ctx.Version family expected

    let private indexed (location: string) (i: int) =
        location
        + "["
        + i.ToString(CultureInfo.InvariantCulture)
        + "]"

    let private map location decodeKey decodeValue element =
        arr location element
        |> List.mapi (fun i entry ->
            let here = indexed location i
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

    let private access (ctx: ClassicContext) location decoder element =
        let accessElement, content =
            if ctx.Version = 1 then
                shape location 2 element
                item location 0 element, item location 1 element
            else
                prop location "access" element, prop location "value" element

        let accessText = text location accessElement

        let access =
            if accessText = (if ctx.Version = 1 then "public" else "Public") then
                Public
            elif accessText = (if ctx.Version = 1 then "private" else "Private") then
                Private
            else
                fail "decode" location $"Invalid access '{accessText}'."

        {
            Access = access
            Value = decoder location content
        }

    /// A value and its optional documentation. Generation has always refused a null doc, while the v3
    /// schema allows one, so the caller chooses.
    let private documented allowNullDoc location decoder (element: JsonElement) =
        let mutable doc = Unchecked.defaultof<JsonElement>

        if
            element.ValueKind = JsonValueKind.Object
            && element.TryGetProperty("doc", &doc)
        then
            if
                allowNullDoc
                && doc.ValueKind = JsonValueKind.Null
            then
                WithoutDocumentation(decoder location (prop location "value" element))
            else
                WithDocumentation(
                    text location doc,
                    decoder location (prop location "value" element)
                )
        else
            WithoutDocumentation(decoder location element)

    let unwrap documented =
        match documented with
        | WithDocumentation(_, v)
        | WithoutDocumentation v -> v

    let rec typ (ctx: ClassicContext) location element : Type<JsonElement> =
        RuntimeHelpers.EnsureSufficientExecutionStack()
        let is = tagged ctx "type" location element
        let at i = item location i element
        let attributes = at 1
        let nested i = typ ctx (indexed location i) (at i)

        let fields i =
            arr location (at i)
            |> List.map (fun field -> {
                Name = name ctx location (prop location "name" field)
                Type = typ ctx location (prop location "tpe" field)
            })

        if is "Variable" then
            shape location 3 element
            Type.Variable(attributes, name ctx location (at 2))
        elif is "Reference" then
            shape location 4 element

            Type.Reference(
                attributes,
                fq ctx location (at 2),
                arr location (at 3)
                |> List.map (typ ctx location)
            )
        elif is "Tuple" then
            shape location 3 element

            Type.Tuple(
                attributes,
                arr location (at 2)
                |> List.map (typ ctx location)
            )
        elif is "Record" then
            shape location 3 element
            Type.Record(attributes, fields 2)
        elif is "ExtensibleRecord" then
            shape location 4 element
            Type.ExtensibleRecord(attributes, name ctx location (at 2), fields 3)
        elif is "Function" then
            shape location 4 element
            Type.Function(attributes, nested 2, nested 3)
        elif is "Unit" then
            shape location 2 element
            Type.Unit attributes
        else
            fail "decode" location "Unknown or version-incompatible type tag."

    let private expectKind location kind what (data: JsonElement) =
        if
            data.ValueKind
            <> kind
        then
            fail "decode" location $"Expected {what}."

    /// A decimal read for the public reader. A string must follow the decimal grammar. Neither form is
    /// rounded: a decimal that does not fit exactly is out of range.
    let private exactDecimal location (data: JsonElement) =
        let lexeme =
            match data.ValueKind with
            | JsonValueKind.String ->
                let text = nonNull (data.GetString())

                match V4.DecimalLexeme.create text with
                | Ok _ -> text
                | Error message -> fail "decode" location message
            | JsonValueKind.Number -> data.GetRawText()
            | _ -> fail "decode" location "Expected a decimal."

        let mutable number = 0m

        if
            Decimal.TryParse(
                lexeme,
                NumberStyles.Number
                ||| NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                &number
            )
            && V4.DecimalLexeme.holdsExactly lexeme number
        then
            number
        else
            fail "literal_out_of_range" location "The decimal does not fit the classic literal."

    let private literal (ctx: ClassicContext) location element =
        shape location 2 element
        let is = tagged ctx "literal" location element
        let data = item location 1 element

        if is "BoolLiteral" then
            if not ctx.ForGeneration then
                if
                    data.ValueKind
                    <> JsonValueKind.True
                    && data.ValueKind
                       <> JsonValueKind.False
                then
                    fail "decode" location "Expected a boolean."

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
            let mutable number = 0L

            if ctx.ForGeneration then
                if
                    data.ValueKind = JsonValueKind.Number
                    && not (data.TryGetInt64(&number))
                then
                    fail
                        "literal_out_of_range"
                        location
                        "The whole number does not fit a 64-bit integer."

                WholeNumberLiteral(data.GetInt64())
            else
                expectKind location JsonValueKind.Number "a whole number" data

                if data.TryGetInt64(&number) then
                    WholeNumberLiteral number
                elif
                    data.GetRawText().IndexOfAny [| '.'; 'e'; 'E' |]
                    >= 0
                then
                    // An integer is written with digits alone. Another spelling is not one.
                    fail "decode" location "Expected a whole number written as an integer."
                else
                    fail
                        "literal_out_of_range"
                        location
                        "The whole number does not fit a 64-bit integer."
        elif is "FloatLiteral" then
            if not ctx.ForGeneration then
                expectKind location JsonValueKind.Number "a float" data

            FloatLiteral(data.GetDouble())
        elif is "DecimalLiteral" then
            if ctx.ForGeneration then
                DecimalLiteral(
                    if data.ValueKind = JsonValueKind.String then
                        Decimal.Parse(text location data, CultureInfo.InvariantCulture)
                    else
                        data.GetDecimal()
                )
            else
                DecimalLiteral(exactDecimal location data)
        else
            fail "decode" location "Unknown or version-incompatible literal tag."

    let rec private pattern (ctx: ClassicContext) location element : Pattern<JsonElement> =
        RuntimeHelpers.EnsureSufficientExecutionStack()
        let is = tagged ctx "pattern" location element
        let at i = item location i element
        let a = at 1
        let p i = pattern ctx location (at i)

        if is "WildcardPattern" then
            shape location 2 element
            WildcardPattern a
        elif is "AsPattern" then
            shape location 4 element
            AsPattern(a, p 2, name ctx location (at 3))
        elif is "TuplePattern" then
            shape location 3 element

            TuplePattern(
                a,
                arr location (at 2)
                |> List.map (pattern ctx location)
            )
        elif is "ConstructorPattern" then
            shape location 4 element

            ConstructorPattern(
                a,
                fq ctx location (at 2),
                arr location (at 3)
                |> List.map (pattern ctx location)
            )
        elif is "EmptyListPattern" then
            shape location 2 element
            EmptyListPattern a
        elif is "HeadTailPattern" then
            shape location 4 element
            HeadTailPattern(a, p 2, p 3)
        elif is "LiteralPattern" then
            shape location 3 element
            LiteralPattern(a, literal ctx location (at 2))
        elif is "UnitPattern" then
            shape location 2 element
            UnitPattern a
        else
            fail "decode" location "Unknown or version-incompatible pattern tag."

    let rec private value (ctx: ClassicContext) location element : Value<JsonElement, JsonElement> =
        RuntimeHelpers.EnsureSufficientExecutionStack()
        let is = tagged ctx "value" location element
        let at i = item location i element
        let a = at 1
        let v i = value ctx (indexed location i) (at i)

        let vs i =
            arr location (at i)
            |> List.map (value ctx location)

        let fields i =
            map location (name ctx) (value ctx) (at i)

        if is "Literal" then
            shape location 3 element
            Value.Literal(a, literal ctx location (at 2))
        elif is "Constructor" then
            shape location 3 element
            Value.Constructor(a, fq ctx location (at 2))
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
            Value.Variable(a, name ctx location (at 2))
        elif is "Reference" then
            shape location 3 element
            Value.Reference(a, fq ctx location (at 2))
        elif is "Field" then
            shape location 4 element
            Value.Field(a, v 2, name ctx location (at 3))
        elif is "FieldFunction" then
            shape location 3 element
            Value.FieldFunction(a, name ctx location (at 2))
        elif is "Apply" then
            shape location 4 element
            Value.Apply(a, v 2, v 3)
        elif is "Lambda" then
            shape location 4 element
            Value.Lambda(a, pattern ctx location (at 2), v 3)
        elif is "LetDefinition" then
            shape location 5 element
            Value.LetDefinition(a, name ctx location (at 2), definition ctx location (at 3), v 4)
        elif is "LetRecursion" then
            shape location 4 element
            Value.LetRecursion(a, map location (name ctx) (definition ctx) (at 2), v 3)
        elif is "Destructure" then
            shape location 5 element
            Value.Destructure(a, pattern ctx location (at 2), v 3, v 4)
        elif is "IfThenElse" then
            shape location 5 element
            Value.IfThenElse(a, v 2, v 3, v 4)
        elif is "PatternMatch" then
            shape location 4 element

            let cases =
                arr location (at 3)
                |> List.map (fun pair ->
                    shape location 2 pair

                    pattern ctx location (item location 0 pair),
                    value ctx location (item location 1 pair)
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

    and private definition
        (ctx: ClassicContext)
        location
        element
        : ValueDefinition<JsonElement, JsonElement> =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        {
            InputTypes =
                arr location (prop location "inputTypes" element)
                |> List.map (fun input ->
                    shape location 3 input

                    name ctx location (item location 0 input),
                    item location 1 input,
                    typ ctx location (item location 2 input)
                )
            OutputType = typ ctx location (prop location "outputType" element)
            Body =
                value
                    ctx
                    (location
                     + ".body")
                    (prop location "body" element)
        }

    let private constructors (ctx: ClassicContext) location =
        map
            location
            (name ctx)
            (fun location args ->
                arr location args
                |> List.map (fun arg ->
                    shape location 2 arg

                    name ctx location (item location 0 arg),
                    typ ctx location (item location 1 arg)
                )
            )

    let private typeDefinition
        (ctx: ClassicContext)
        location
        element
        : Type.TypeDefinition<JsonElement> =
        shape location 3 element
        let is = tagged ctx "type" location element

        let parameters =
            arr location (item location 1 element)
            |> List.map (name ctx location)

        if is "TypeAliasDefinition" then
            Type.TypeAliasDefinition(parameters, typ ctx location (item location 2 element))
        elif is "CustomTypeDefinition" then
            Type.CustomTypeDefinition(
                parameters,
                access ctx location (constructors ctx) (item location 2 element)
            )
        else
            fail "decode" location "Unknown or version-incompatible type definition."

    let private optionalDoc location (element: JsonElement) =
        let mutable doc = Unchecked.defaultof<JsonElement>

        if
            element.TryGetProperty("doc", &doc)
            && doc.ValueKind
               <> JsonValueKind.Null
        then
            Some(text location doc)
        else
            None

    let private moduleDefinition
        forGeneration
        (ctx: ClassicContext)
        location
        element
        : Module.ModuleDefinition<JsonElement, JsonElement> =
        let allowNullDoc = not forGeneration

        {
            Types =
                map
                    (location
                     + ".types")
                    (name ctx)
                    (fun location ->
                        access
                            ctx
                            location
                            (fun location -> documented allowNullDoc location (typeDefinition ctx))
                    )
                    (prop location "types" element)
            Values =
                map
                    (location
                     + ".values")
                    (name ctx)
                    (fun location ->
                        access
                            ctx
                            location
                            (fun location -> documented allowNullDoc location (definition ctx))
                    )
                    (prop location "values" element)
            Doc = if forGeneration then None else optionalDoc location element
        }

    let private typeSpecification
        (ctx: ClassicContext)
        location
        element
        : Type.TypeSpecification<JsonElement> =
        let is = tagged ctx "type" location element

        let parameters () =
            arr location (item location 1 element)
            |> List.map (name ctx location)

        if is "TypeAliasSpecification" then
            shape location 3 element
            Type.TypeAliasSpecification(parameters (), typ ctx location (item location 2 element))
        elif is "OpaqueTypeSpecification" then
            shape location 2 element
            Type.OpaqueTypeSpecification(parameters ())
        elif is "CustomTypeSpecification" then
            shape location 3 element

            Type.CustomTypeSpecification(
                parameters (),
                constructors ctx location (item location 2 element)
            )
        elif is "DerivedTypeSpecification" then
            shape location 3 element
            let details = item location 2 element

            Type.DerivedTypeSpecification(
                parameters (),
                {
                    BaseType = typ ctx location (prop location "baseType" details)
                    FromBaseType = fq ctx location (prop location "fromBaseType" details)
                    ToBaseType = fq ctx location (prop location "toBaseType" details)
                }
            )
        else
            fail "decode" location "Unknown or version-incompatible type specification."

    let private valueSpecification
        (ctx: ClassicContext)
        location
        element
        : ValueSpecification<JsonElement> =
        {
            Inputs =
                arr location (prop location "inputs" element)
                |> List.map (fun input ->
                    shape location 2 input

                    name ctx location (item location 0 input),
                    typ ctx location (item location 1 input)
                )
            Output = typ ctx location (prop location "output" element)
        }

    let private moduleSpecification
        (ctx: ClassicContext)
        location
        element
        : Module.ModuleSpecification<JsonElement> =
        {
            Types =
                map
                    (location
                     + ".types")
                    (name ctx)
                    (fun location -> documented true location (typeSpecification ctx))
                    (prop location "types" element)
            Values =
                map
                    (location
                     + ".values")
                    (name ctx)
                    (fun location -> documented true location (valueSpecification ctx))
                    (prop location "values" element)
            Doc = optionalDoc location element
        }

    /// Module lists keep version 1's objects, and later versions' pairs.
    let private modulesOf (ctx: ClassicContext) location key decodeModule (modules: JsonElement) =
        if ctx.Version = 1 then
            arr location modules
            |> List.mapi (fun i entry ->
                let here = indexed location i

                ModulePath.modulePath (path ctx here (prop here "name" entry)),
                decodeModule here (prop here key entry)
            )
            |> List.fold
                (fun result (path, data) ->
                    if Map.containsKey path result then
                        fail "duplicate-key" "$" "Duplicate module."

                    Map.add path data result
                )
                Map.empty
        else
            map
                location
                (fun location e -> ModulePath.modulePath (path ctx location e))
                decodeModule
                modules

    let private packageSpecification
        (ctx: ClassicContext)
        location
        element
        : Package.PackageSpecification<JsonElement> =
        {
            Modules =
                modulesOf
                    ctx
                    (location
                     + ".modules")
                    "spec"
                    (moduleSpecification ctx)
                    (prop location "modules" element)
        }

    let private dependencies (ctx: ClassicContext) element =
        map
            "$.distribution[2]"
            (fun location e -> PackageName.packageName (path ctx location e))
            (packageSpecification ctx)
            element

    let private packageDefinition
        forGeneration
        (ctx: ClassicContext)
        element
        : Package.PackageDefinition<JsonElement, JsonElement> =
        {
            Modules =
                modulesOf
                    ctx
                    "$.distribution[3].modules"
                    "def"
                    (fun location -> access ctx location (moduleDefinition forGeneration ctx))
                    (prop "$.distribution[3]" "modules" element)
        }

    let private libraryTag version =
        if version = 1 then "library" else "Library"

    /// A release string's minor component, for v3 only.
    let private releaseMinor (release: string) =
        match release.Split('.') with
        | [| "3"; minor; patch |] when
            (minor = "0"
             || minor = "1")
            && not (String.IsNullOrEmpty patch)
            && patch
               |> Seq.forall Char.IsAsciiDigit
            && (patch = "0"
                || patch[0]
                   <> '0')
            ->
            Some(int minor)
        | _ -> None

    let private unsupportedVersion () =
        fail
            "unsupported-version"
            "$.formatVersion"
            "Expected a classic IR version: integer 1, 2 or 3, or release 3.0.x or 3.1.x."

    /// Reads a complete document, including dependency specifications.
    let document (root: JsonElement) : ClassicDocument =
        let mutable element = Unchecked.defaultof<JsonElement>
        let mutable number = 0

        if
            root.ValueKind
            <> JsonValueKind.Object
            || not (root.TryGetProperty("formatVersion", &element))
        then
            unsupportedVersion ()

        let version, minor =
            if
                element.ValueKind = JsonValueKind.Number
                && element.TryGetInt32(&number)
                && number
                   >= 1
                && number
                   <= 3
            then
                number, 0
            elif element.ValueKind = JsonValueKind.String then
                match releaseMinor (nonNull (element.GetString())) with
                | Some minor -> 3, minor
                | None -> unsupportedVersion ()
            else
                unsupportedVersion ()

        let ctx = {
            Version = version
            ForGeneration = false
        }

        let distribution = prop "$" "distribution" root
        shape "$.distribution" 4 distribution
        let kind = text "$.distribution[0]" (item "$" 0 distribution)

        let package =
            PackageName.packageName (path ctx "$.distribution[1]" (item "$" 1 distribution))

        if kind = libraryTag version then
            {
                Version = version
                Distribution =
                    Library(
                        package,
                        dependencies ctx (item "$" 2 distribution),
                        packageDefinition false ctx (item "$" 3 distribution)
                    )
            }
        elif
            kind = "Specs"
            && version = 3
        then
            if minor < 1 then
                fail
                    "specs_before_3_1"
                    "$.formatVersion"
                    "A Specs distribution needs formatVersion 3.1.0 or later."

            {
                Version = version
                Distribution =
                    Specs(
                        package,
                        dependencies ctx (item "$" 2 distribution),
                        packageSpecification ctx "$.distribution[3]" (item "$" 3 distribution)
                    )
            }
        else
            fail
                "unsupported-distribution"
                "$.distribution"
                "The distribution kind is not one this document's version allows."

    /// Reads what F# generation needs: an integer version and a Library. Dependency specifications
    /// supply no executable definitions, so only their shape is checked and resolution rejects what
    /// they do not cover.
    let libraryForGeneration (root: JsonElement) =
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
            <> libraryTag version
        then
            fail
                "unsupported-distribution"
                "$.distribution"
                "Only classic Library JSON versions 1, 2 and 3 are supported; document trees are not supported."

        let ctx = {
            Version = version
            ForGeneration = true
        }

        let package =
            PackageName.packageName (path ctx "$.distribution[1]" (item "$" 1 distribution))

        map
            "$.distribution[2]"
            (fun location e -> path ctx location e)
            (fun location e ->
                prop location "modules" e
                |> arr location
                |> ignore
            )
            (item "$" 2 distribution)
        |> ignore

        version, package, packageDefinition true ctx (item "$" 3 distribution)

/// <summary>A complete classic IR document with typed attributes, the form the classic writer writes.</summary>
type TypedClassicDocument = {
    /// <summary>The classic major version: 1, 2 or 3.</summary>
    Version: int
    /// <summary>The distribution. Type attributes are empty; each value attribute is the value's type.</summary>
    Distribution: Distribution<unit, Type<unit>>
}

/// <summary>Reads complete classic IR JSON documents.</summary>
[<RequireQualifiedAccess>]
module ClassicJson =
    /// <summary>
    /// Reads a v1, v2 or v3 Library, or a Specs distribution at 3.1.0, including dependency specifications.
    /// </summary>
    /// <remarks>The result keeps no reference to the document that holds <paramref name="root"/>.</remarks>
    let readDistribution (root: JsonElement) : Result<ClassicDocument, ClassicJsonError> =
        try
            Result.Ok(ClassicJsonDecoder.document (root.Clone()))
        with
        | ClassicJsonFailure error -> Result.Error error
        | :? InvalidOperationException as error ->
            Result.Error
                {
                    Rule = "decode"
                    Location = "$"
                    Message = error.Message
                }
        | :? FormatException as error ->
            Result.Error
                {
                    Rule = "decode"
                    Location = "$"
                    Message = error.Message
                }
        | :? InsufficientExecutionStackException ->
            Result.Error
                {
                    Rule = "depth_limit_exceeded"
                    Location = "$"
                    Message = "The document is nested too deeply to read."
                }

    /// <summary>
    /// Reads a classic document into the typed form that <c>ClassicJsonWriter.writeTyped</c> writes: type
    /// attributes become unit, and each value attribute is read as the value's type. With the classic writers,
    /// a model read at one classic version can be written at another without a change of family.
    /// </summary>
    let readTypedDistribution (root: JsonElement) : Result<TypedClassicDocument, ClassicJsonError> =
        readDistribution root
        |> Result.bind (fun document ->
            let context = { Version = document.Version; ForGeneration = false }

            let valueType (attribute: JsonElement) =
                Morphir.IR.Classic.Attributes.mapType ignore (ClassicJsonDecoder.typ context "$.attributes" attribute)

            try
                Result.Ok
                    {
                        Version = document.Version
                        Distribution =
                            Morphir.IR.Classic.Attributes.mapDistribution ignore valueType document.Distribution
                    }
            with ClassicJsonFailure error ->
                Result.Error error)
