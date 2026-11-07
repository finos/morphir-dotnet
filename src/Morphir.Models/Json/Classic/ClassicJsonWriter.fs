namespace Morphir.Json.Classic

open System
open System.Buffers
open System.Globalization
open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Distribution
open Morphir.IR.Classic.Documented

/// <summary>How a classic wire version spells a tag.</summary>
[<RequireQualifiedAccess>]
type ClassicTagStyle =
    /// <summary><c>LetDefinition</c>.</summary>
    | PascalCase
    /// <summary><c>let_definition</c>.</summary>
    | SnakeCase

/// <summary>How a classic wire version writes an access-controlled value.</summary>
[<RequireQualifiedAccess>]
type ClassicAccessStyle =
    /// <summary><c>["public", value]</c> (version 1).</summary>
    | Pair
    /// <summary><c>{"access": "Public", "value": value}</c> (versions 2 and 3).</summary>
    | Object

/// <summary>How a classic wire version writes a module list.</summary>
[<RequireQualifiedAccess>]
type ClassicModuleListStyle =
    /// <summary><c>[{"name": path, "def": module}]</c> (version 1).</summary>
    | Records
    /// <summary><c>[[path, module]]</c> (versions 2 and 3).</summary>
    | Pairs

/// <summary>How a classic wire version writes a module without documentation.</summary>
[<RequireQualifiedAccess>]
type ClassicModuleDocStyle =
    /// <summary>The <c>doc</c> member is left out (versions 1 and 2).</summary>
    | OmitWhenAbsent
    /// <summary><c>"doc": null</c> (version 3, as the upstream kit, writer and schema have it).</summary>
    | NullWhenAbsent

/// <summary>
/// The wire rules of one classic format version (IRW-1). It is data with no behavior; the writer reads it.
/// Each of versions 1, 2 and 3 is one value.
/// </summary>
type ClassicWireProfile = {
    /// <summary>The integer <c>formatVersion</c> the writer writes.</summary>
    FormatVersion: int
    /// <summary>The spelling of type, type definition and type specification tags.</summary>
    TypeTags: ClassicTagStyle
    /// <summary>The spelling of value, pattern and literal tags.</summary>
    ValueTags: ClassicTagStyle
    /// <summary>The access wrapper.</summary>
    Access: ClassicAccessStyle
    /// <summary>The module list shape.</summary>
    Modules: ClassicModuleListStyle
    /// <summary>The distribution tag of a library.</summary>
    LibraryTag: string
    /// <summary>How a module without documentation is written.</summary>
    ModuleDocs: ClassicModuleDocStyle
}

/// <summary>The classic wire profiles.</summary>
[<RequireQualifiedAccess>]
module ClassicWireProfile =
    /// <summary>Classic format version 1.</summary>
    let v1 = {
        FormatVersion = 1
        TypeTags = ClassicTagStyle.SnakeCase
        ValueTags = ClassicTagStyle.SnakeCase
        Access = ClassicAccessStyle.Pair
        Modules = ClassicModuleListStyle.Records
        LibraryTag = "library"
        ModuleDocs = ClassicModuleDocStyle.OmitWhenAbsent
    }

    /// <summary>Classic format version 2.</summary>
    let v2 = {
        FormatVersion = 2
        TypeTags = ClassicTagStyle.PascalCase
        ValueTags = ClassicTagStyle.SnakeCase
        Access = ClassicAccessStyle.Object
        Modules = ClassicModuleListStyle.Pairs
        LibraryTag = "Library"
        ModuleDocs = ClassicModuleDocStyle.OmitWhenAbsent
    }

    /// <summary>Classic format version 3.</summary>
    let v3 = {
        FormatVersion = 3
        TypeTags = ClassicTagStyle.PascalCase
        ValueTags = ClassicTagStyle.PascalCase
        Access = ClassicAccessStyle.Object
        Modules = ClassicModuleListStyle.Pairs
        LibraryTag = "Library"
        ModuleDocs = ClassicModuleDocStyle.NullWhenAbsent
    }

/// <summary>Why a classic document was not written.</summary>
type ClassicWriteError = {
    /// <summary>A stable rule name.</summary>
    Rule: string
    /// <summary>Where the problem is, as a JSON path into the document being written.</summary>
    Location: string
    /// <summary>What is wrong.</summary>
    Message: string
}

exception private ClassicWriteFailure of ClassicWriteError

/// <summary>
/// Writes classic library distributions as JSON for one wire profile. The attribute encoders decide how
/// attributes appear, so the same writer serves typed models and models read from JSON.
/// </summary>
[<RequireQualifiedAccess>]
module ClassicJsonWriter =

    let private fail rule location message =
        raise (
            ClassicWriteFailure
                {
                    Rule = rule
                    Location = location
                    Message = message
                }
        )

    let private snake (value: string) =
        let builder = Text.StringBuilder()

        value
        |> String.iteri (fun i c ->
            if
                Char.IsUpper c
                && i > 0
            then
                builder.Append '_'
                |> ignore

            builder.Append(Char.ToLowerInvariant c)
            |> ignore
        )

        builder.ToString()

    type private Writer<'ta, 'va> = {
        Profile: ClassicWireProfile
        Json: Utf8JsonWriter
        TypeAttribute: 'ta -> Utf8JsonWriter -> unit
        ValueAttribute: 'va -> Utf8JsonWriter -> unit
    }

    let private tag style (name: string) =
        match style with
        | ClassicTagStyle.PascalCase -> name
        | ClassicTagStyle.SnakeCase -> snake name

    let private typeTag (w: Writer<_, _>) name =
        w.Json.WriteStringValue(tag w.Profile.TypeTags name)

    let private valueTag (w: Writer<_, _>) name =
        w.Json.WriteStringValue(tag w.Profile.ValueTags name)

    let private array (w: Writer<_, _>) (body: unit -> unit) =
        w.Json.WriteStartArray()
        body ()
        w.Json.WriteEndArray()

    let private name (w: Writer<_, _>) (Name words) =
        array
            w
            (fun () ->
                words
                |> List.iter w.Json.WriteStringValue
            )

    let private path (w: Writer<_, _>) (Path names) =
        array
            w
            (fun () ->
                names
                |> List.iter (name w)
            )

    let private packageName w (PackageName p) = path w p

    let private modulePath w (ModulePath p) = path w p

    let private fqName (w: Writer<_, _>) (fq: FQName) =
        array
            w
            (fun () ->
                packageName w fq.PackagePath
                modulePath w fq.ModulePath
                name w fq.LocalName
            )

    let private pairs
        (w: Writer<_, _>)
        (writeKey: 'k -> unit)
        (writeValue: 'v -> unit)
        (entries: ('k * 'v) seq)
        =
        array
            w
            (fun () ->
                for key, value in entries do
                    array
                        w
                        (fun () ->
                            writeKey key
                            writeValue value
                        )
            )

    let private access
        (w: Writer<_, _>)
        (writeValue: 'v -> unit)
        (controlled: AccessControlled<'v>)
        =
        match w.Profile.Access with
        | ClassicAccessStyle.Pair ->
            array
                w
                (fun () ->
                    w.Json.WriteStringValue(
                        match controlled.Access with
                        | Public -> "public"
                        | Private -> "private"
                    )

                    writeValue controlled.Value
                )
        | ClassicAccessStyle.Object ->
            w.Json.WriteStartObject()

            w.Json.WriteString(
                "access",
                match controlled.Access with
                | Public -> "Public"
                | Private -> "Private"
            )

            w.Json.WritePropertyName "value"
            writeValue controlled.Value
            w.Json.WriteEndObject()

    let private documented (w: Writer<_, _>) (writeValue: 'v -> unit) (value: Documented<'v>) =
        match value with
        | WithoutDocumentation inner -> writeValue inner
        | WithDocumentation(doc, inner) ->
            w.Json.WriteStartObject()
            w.Json.WriteString("doc", doc)
            w.Json.WritePropertyName "value"
            writeValue inner
            w.Json.WriteEndObject()

    let rec private typ (w: Writer<'ta, _>) (t: Type<'ta>) =
        let open' (name: string) (a: 'ta) =
            w.Json.WriteStartArray()
            typeTag w name
            w.TypeAttribute a w.Json

        let fields (fields: Field<'ta> list) =
            array
                w
                (fun () ->
                    for field in fields do
                        w.Json.WriteStartObject()
                        w.Json.WritePropertyName "name"
                        name w field.Name
                        w.Json.WritePropertyName "tpe"
                        typ w field.Type
                        w.Json.WriteEndObject()
                )

        match t with
        | Type.Variable(a, n) ->
            open' "Variable" a
            name w n
        | Type.Reference(a, fq, args) ->
            open' "Reference" a
            fqName w fq

            array
                w
                (fun () ->
                    args
                    |> List.iter (typ w)
                )
        | Type.Tuple(a, elements) ->
            open' "Tuple" a

            array
                w
                (fun () ->
                    elements
                    |> List.iter (typ w)
                )
        | Type.Record(a, fs) ->
            open' "Record" a
            fields fs
        | Type.ExtensibleRecord(a, variable, fs) ->
            open' "ExtensibleRecord" a
            name w variable
            fields fs
        | Type.Function(a, argument, result) ->
            open' "Function" a
            typ w argument
            typ w result
        | Type.Unit a -> open' "Unit" a

        w.Json.WriteEndArray()

    let private literal (w: Writer<_, _>) location (l: Literal) =
        array
            w
            (fun () ->
                match l with
                | BoolLiteral value ->
                    valueTag w "BoolLiteral"
                    w.Json.WriteBooleanValue value
                | CharLiteral value ->
                    valueTag w "CharLiteral"
                    w.Json.WriteStringValue(string value)
                | StringLiteral value ->
                    valueTag w "StringLiteral"
                    w.Json.WriteStringValue value
                | WholeNumberLiteral value ->
                    valueTag w "WholeNumberLiteral"
                    w.Json.WriteNumberValue value
                | FloatLiteral value ->
                    if not (Double.IsFinite value) then
                        fail
                            "literal_out_of_range"
                            location
                            "A classic float literal must be finite."

                    valueTag w "FloatLiteral"
                    w.Json.WriteNumberValue value
                | DecimalLiteral value ->
                    valueTag w "DecimalLiteral"
                    w.Json.WriteStringValue(value.ToString CultureInfo.InvariantCulture)
            )

    let rec private pattern (w: Writer<_, 'va>) location (p: Pattern<'va>) =
        let open' (name: string) (a: 'va) =
            w.Json.WriteStartArray()
            valueTag w name
            w.ValueAttribute a w.Json

        match p with
        | WildcardPattern a -> open' "WildcardPattern" a
        | AsPattern(a, nested, n) ->
            open' "AsPattern" a
            pattern w location nested
            name w n
        | TuplePattern(a, elements) ->
            open' "TuplePattern" a

            array
                w
                (fun () ->
                    elements
                    |> List.iter (pattern w location)
                )
        | ConstructorPattern(a, fq, args) ->
            open' "ConstructorPattern" a
            fqName w fq

            array
                w
                (fun () ->
                    args
                    |> List.iter (pattern w location)
                )
        | EmptyListPattern a -> open' "EmptyListPattern" a
        | HeadTailPattern(a, head, tail) ->
            open' "HeadTailPattern" a
            pattern w location head
            pattern w location tail
        | LiteralPattern(a, l) ->
            open' "LiteralPattern" a
            literal w location l
        | UnitPattern a -> open' "UnitPattern" a

        w.Json.WriteEndArray()

    let rec private value (w: Writer<'ta, 'va>) location (v: Value<'ta, 'va>) =
        let open' (name: string) (a: 'va) =
            w.Json.WriteStartArray()
            valueTag w name
            w.ValueAttribute a w.Json

        let values (items: Value<'ta, 'va> list) =
            array
                w
                (fun () ->
                    items
                    |> List.iter (value w location)
                )

        let fields (fs: Map<Name, Value<'ta, 'va>>) =
            pairs w (name w) (value w location) (Map.toSeq fs)

        match v with
        | Value.Literal(a, l) ->
            open' "Literal" a
            literal w location l
        | Value.Constructor(a, fq) ->
            open' "Constructor" a
            fqName w fq
        | Value.Tuple(a, items) ->
            open' "Tuple" a
            values items
        | Value.List(a, items) ->
            open' "List" a
            values items
        | Value.Record(a, fs) ->
            open' "Record" a
            fields fs
        | Value.Variable(a, n) ->
            open' "Variable" a
            name w n
        | Value.Reference(a, fq) ->
            open' "Reference" a
            fqName w fq
        | Value.Field(a, target, n) ->
            open' "Field" a
            value w location target
            name w n
        | Value.FieldFunction(a, n) ->
            open' "FieldFunction" a
            name w n
        | Value.Apply(a, fn, argument) ->
            open' "Apply" a
            value w location fn
            value w location argument
        | Value.Lambda(a, argument, body) ->
            open' "Lambda" a
            pattern w location argument
            value w location body
        | Value.LetDefinition(a, n, definition, inValue) ->
            open' "LetDefinition" a
            name w n
            valueDefinition w location definition
            value w location inValue
        | Value.LetRecursion(a, definitions, inValue) ->
            open' "LetRecursion" a
            pairs w (name w) (valueDefinition w location) (Map.toSeq definitions)
            value w location inValue
        | Value.Destructure(a, p, target, inValue) ->
            open' "Destructure" a
            pattern w location p
            value w location target
            value w location inValue
        | Value.IfThenElse(a, condition, thenBranch, elseBranch) ->
            open' "IfThenElse" a
            value w location condition
            value w location thenBranch
            value w location elseBranch
        | Value.PatternMatch(a, subject, cases) ->
            open' "PatternMatch" a
            value w location subject
            pairs w (pattern w location) (value w location) cases
        | Value.UpdateRecord(a, target, fs) ->
            open' "UpdateRecord" a
            value w location target
            fields fs
        | Value.Unit a -> open' "Unit" a

        w.Json.WriteEndArray()

    and private valueDefinition
        (w: Writer<'ta, 'va>)
        location
        (definition: ValueDefinition<'ta, 'va>)
        =
        w.Json.WriteStartObject()
        w.Json.WritePropertyName "inputTypes"

        array
            w
            (fun () ->
                for n, a, t in definition.InputTypes do
                    array
                        w
                        (fun () ->
                            name w n
                            w.ValueAttribute a w.Json
                            typ w t
                        )
            )

        w.Json.WritePropertyName "outputType"
        typ w definition.OutputType
        w.Json.WritePropertyName "body"

        value
            w
            (location
             + ".body")
            definition.Body

        w.Json.WriteEndObject()

    let private constructors (w: Writer<'ta, _>) (cs: Type.Constructors<'ta>) =
        pairs
            w
            (name w)
            (fun (args: (Name * Type<'ta>) list) ->
                array
                    w
                    (fun () ->
                        for n, t in args do
                            array
                                w
                                (fun () ->
                                    name w n
                                    typ w t
                                )
                    )
            )
            (Map.toSeq cs)

    let private parameters (w: Writer<_, _>) (ps: Name list) =
        array
            w
            (fun () ->
                ps
                |> List.iter (name w)
            )

    let private typeDefinition (w: Writer<'ta, _>) (definition: Type.TypeDefinition<'ta>) =
        array
            w
            (fun () ->
                match definition with
                | Type.TypeAliasDefinition(ps, t) ->
                    typeTag w "TypeAliasDefinition"
                    parameters w ps
                    typ w t
                | Type.CustomTypeDefinition(ps, cs) ->
                    typeTag w "CustomTypeDefinition"
                    parameters w ps
                    access w (constructors w) cs
            )

    let private typeSpecification (w: Writer<'ta, _>) (specification: Type.TypeSpecification<'ta>) =
        array
            w
            (fun () ->
                match specification with
                | Type.TypeAliasSpecification(ps, t) ->
                    typeTag w "TypeAliasSpecification"
                    parameters w ps
                    typ w t
                | Type.OpaqueTypeSpecification ps ->
                    typeTag w "OpaqueTypeSpecification"
                    parameters w ps
                | Type.CustomTypeSpecification(ps, cs) ->
                    typeTag w "CustomTypeSpecification"
                    parameters w ps
                    constructors w cs
                | Type.DerivedTypeSpecification(ps, details) ->
                    typeTag w "DerivedTypeSpecification"
                    parameters w ps
                    w.Json.WriteStartObject()
                    w.Json.WritePropertyName "baseType"
                    typ w details.BaseType
                    w.Json.WritePropertyName "fromBaseType"
                    fqName w details.FromBaseType
                    w.Json.WritePropertyName "toBaseType"
                    fqName w details.ToBaseType
                    w.Json.WriteEndObject()
            )

    let private valueSpecification (w: Writer<'ta, _>) (specification: ValueSpecification<'ta>) =
        w.Json.WriteStartObject()
        w.Json.WritePropertyName "inputs"

        array
            w
            (fun () ->
                for n, t in specification.Inputs do
                    array
                        w
                        (fun () ->
                            name w n
                            typ w t
                        )
            )

        w.Json.WritePropertyName "output"
        typ w specification.Output
        w.Json.WriteEndObject()

    let private moduleDoc (w: Writer<_, _>) (doc: string option) =
        match doc, w.Profile.ModuleDocs with
        | Some text, _ -> w.Json.WriteString("doc", text)
        | None, ClassicModuleDocStyle.NullWhenAbsent -> w.Json.WriteNull "doc"
        | None, ClassicModuleDocStyle.OmitWhenAbsent -> ()

    let private moduleSpecification
        (w: Writer<'ta, _>)
        (specification: Module.ModuleSpecification<'ta>)
        =
        w.Json.WriteStartObject()
        w.Json.WritePropertyName "types"
        pairs w (name w) (documented w (typeSpecification w)) (Map.toSeq specification.Types)
        w.Json.WritePropertyName "values"
        pairs w (name w) (documented w (valueSpecification w)) (Map.toSeq specification.Values)
        moduleDoc w specification.Doc
        w.Json.WriteEndObject()

    let private moduleDefinition
        (w: Writer<'ta, 'va>)
        location
        (definition: Module.ModuleDefinition<'ta, 'va>)
        =
        w.Json.WriteStartObject()
        w.Json.WritePropertyName "types"
        pairs w (name w) (access w (documented w (typeDefinition w))) (Map.toSeq definition.Types)
        w.Json.WritePropertyName "values"

        pairs
            w
            (name w)
            (fun entry ->
                access
                    w
                    (documented
                        w
                        (valueDefinition
                            w
                            (location
                             + ".values")))
                    entry
            )
            (Map.toSeq definition.Values)

        moduleDoc w definition.Doc
        w.Json.WriteEndObject()

    let private modules
        (w: Writer<_, _>)
        (key: string)
        (writeModule: 'm -> unit)
        (entries: (ModulePath * 'm) seq)
        =
        match w.Profile.Modules with
        | ClassicModuleListStyle.Pairs -> pairs w (modulePath w) writeModule entries
        | ClassicModuleListStyle.Records ->
            array
                w
                (fun () ->
                    for path, m in entries do
                        w.Json.WriteStartObject()
                        w.Json.WritePropertyName "name"
                        modulePath w path
                        w.Json.WritePropertyName key
                        writeModule m
                        w.Json.WriteEndObject()
                )

    let private packageSpecification
        (w: Writer<'ta, _>)
        (specification: Package.PackageSpecification<'ta>)
        =
        w.Json.WriteStartObject()
        w.Json.WritePropertyName "modules"
        modules w "spec" (moduleSpecification w) (Map.toSeq specification.Modules)
        w.Json.WriteEndObject()

    let private packageDefinition
        (w: Writer<'ta, 'va>)
        (definition: Package.PackageDefinition<'ta, 'va>)
        =
        w.Json.WriteStartObject()
        w.Json.WritePropertyName "modules"

        modules
            w
            "def"
            (access w (moduleDefinition w "$.distribution[3].modules"))
            (Map.toSeq definition.Modules)

        w.Json.WriteEndObject()

    /// <summary>
    /// Writes a library distribution with <paramref name="profile"/>. Members and lists are written in a fixed
    /// order, so equal models give equal bytes. A <c>Specs</c> distribution and a non-finite float literal are
    /// refused.
    /// </summary>
    let write
        (profile: ClassicWireProfile)
        (indented: bool)
        (typeAttribute: 'ta -> Utf8JsonWriter -> unit)
        (valueAttribute: 'va -> Utf8JsonWriter -> unit)
        (distribution: Distribution<'ta, 'va>)
        : Result<byte array, ClassicWriteError> =
        match distribution with
        | Specs _ ->
            Error
                {
                    Rule = "unsupported-distribution"
                    Location = "$.distribution"
                    Message =
                        "Only a Library distribution can be written; a Specs distribution needs format 3.1.0."
                }
        | Library(package, dependencies, definition) ->
            let buffer = ArrayBufferWriter<byte>()

            try
                use json =
                    new Utf8JsonWriter(
                        buffer,
                        JsonWriterOptions(Indented = indented, IndentSize = 2)
                    )

                let w = {
                    Profile = profile
                    Json = json
                    TypeAttribute = typeAttribute
                    ValueAttribute = valueAttribute
                }

                json.WriteStartObject()
                json.WriteNumber("formatVersion", profile.FormatVersion)
                json.WritePropertyName "distribution"

                array
                    w
                    (fun () ->
                        json.WriteStringValue profile.LibraryTag
                        packageName w package
                        pairs w (packageName w) (packageSpecification w) (Map.toSeq dependencies)
                        packageDefinition w definition
                    )

                json.WriteEndObject()
                json.Flush()
                Ok(buffer.WrittenSpan.ToArray())
            with ClassicWriteFailure error ->
                Error error

    /// <summary>Writes attributes that came from JSON as they are, for models read with <c>ClassicJson</c>.</summary>
    let writeJsonAttributes
        profile
        indented
        (distribution: Distribution<JsonElement, JsonElement>)
        =
        let raw (element: JsonElement) (json: Utf8JsonWriter) = element.WriteTo json
        write profile indented raw raw distribution

    /// <summary>
    /// Writes a typed model: type attributes are empty objects, and each value attribute is the value's type.
    /// </summary>
    let writeTyped profile indented (distribution: Distribution<unit, Type<unit>>) =
        let empty () (json: Utf8JsonWriter) =
            json.WriteStartObject()
            json.WriteEndObject()

        let typed (t: Type<unit>) (json: Utf8JsonWriter) =
            let inner = {
                Profile = profile
                Json = json
                TypeAttribute = empty
                ValueAttribute = (fun () _ -> ())
            }

            typ inner t

        write profile indented empty typed distribution
