namespace Morphir.Ion

open System
open System.Globalization
open System.Numerics
open System.Runtime.CompilerServices
open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Distribution
open Morphir.IR.Classic.Documented
open Morphir.Ion.V4IonWriter

/// Builds the canonical Ion elements of a v3 distribution: the inverse of <see cref="ClassicIonDistribution"/>. A v3
/// type has no Ion attribute, so its attribute must be the empty object. A value or pattern attribute must be the
/// classic JSON of a type, which Ion writes as <c>{ inferredType }</c>, or <c>[]</c>, which Ion leaves out.
module internal ClassicIonWriter =
    let private refuse message : 'a =
        raise (
            IonIrFailure
                {
                    Code = "unrepresentable_attribute"
                    Message = message
                    Path = None
                    Location = None
                }
        )

    let private isEmptyObject (element: JsonElement) =
        element.ValueKind = JsonValueKind.Object
        && Seq.isEmpty (element.EnumerateObject())

    let private isEmptyArray (element: JsonElement) =
        element.ValueKind = JsonValueKind.Array
        && element.GetArrayLength() = 0

    let private checkTypeAttribute (attribute: JsonElement) =
        if not (isEmptyObject attribute) then
            refuse "A v3 type attribute has no Ion spelling unless it is the empty object."

    // Reads the classic JSON of a type, as ClassicIonTypes.typeAsAttribute writes it.

    exception private NotAType

    let private items (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Array then
            raise NotAType

        [ for item in element.EnumerateArray() -> item ]

    let private words (element: JsonElement) =
        items element
        |> List.map (fun word ->
            if word.ValueKind <> JsonValueKind.String then
                raise NotAType

            word.GetString() |> nonNull
        )
        |> Name

    let private path (element: JsonElement) = items element |> List.map words |> Path

    let private fqName (element: JsonElement) =
        match items element with
        | [ package; modulePath; local ] -> {
            PackagePath = PackageName(path package)
            ModulePath = ModulePath(path modulePath)
            LocalName = words local
          }
        | _ -> raise NotAType

    let rec private typeOf (element: JsonElement) : Type<JsonElement> =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        let fields (element: JsonElement) =
            items element
            |> List.map (fun field ->
                if field.ValueKind <> JsonValueKind.Object then
                    raise NotAType

                match field.TryGetProperty "name", field.TryGetProperty "tpe" with
                | (true, name), (true, tpe) -> { Name = words name; Type = typeOf tpe }
                | _ -> raise NotAType
            )

        match items element with
        | tag :: attribute :: rest when tag.ValueKind = JsonValueKind.String && isEmptyObject attribute ->
            match tag.GetString(), rest with
            | "Variable", [ name ] -> Type.Variable(attribute, words name)
            | "Reference", [ name; arguments ] ->
                Type.Reference(attribute, fqName name, items arguments |> List.map typeOf)
            | "Tuple", [ elements ] -> Type.Tuple(attribute, items elements |> List.map typeOf)
            | "Record", [ entries ] -> Type.Record(attribute, fields entries)
            | "ExtensibleRecord", [ variable; entries ] ->
                Type.ExtensibleRecord(attribute, words variable, fields entries)
            | "Function", [ parameter; result ] -> Type.Function(attribute, typeOf parameter, typeOf result)
            | "Unit", [] -> Type.Unit attribute
            | _ -> raise NotAType
        | _ -> raise NotAType

    /// The type a value or pattern attribute holds, or nothing for the attribute <c>[]</c> that Ion leaves out.
    let private inferred (attribute: JsonElement) : Type<JsonElement> option =
        if isEmptyArray attribute then
            None
        else
            let found =
                try
                    Some(typeOf attribute)
                with NotAType ->
                    None

            match found with
            | Some found when JsonElement.DeepEquals(ClassicIonTypes.typeAsAttribute found, attribute) -> Some found
            | _ ->
                refuse
                    "A v3 value or pattern attribute has an Ion spelling only when it is the classic JSON of a type, or []."

    // Types.

    let rec typ (value: Type<JsonElement>) : IonText =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        let fieldList (fields: Field<JsonElement> list) =
            IonText.List(
                fields
                |> List.map (fun field ->
                    Struct [
                        "name", Text(nameText field.Name)
                        "type", typ field.Type
                    ]
                )
            )

        match value with
        | Type.Variable(attribute, name) ->
            checkTypeAttribute attribute
            Text(nameText name)
        | Type.Reference(attribute, name, []) ->
            checkTypeAttribute attribute
            Text(fqText name)
        | Type.Reference(attribute, name, arguments) ->
            checkTypeAttribute attribute

            Annotated(
                [ "reference" ],
                Struct [
                    "name", Text(fqText name)
                    "arguments", IonText.List(List.map typ arguments)
                ]
            )
        | Type.Tuple(attribute, elements) ->
            checkTypeAttribute attribute
            Annotated([ "tuple" ], IonText.List(List.map typ elements))
        | Type.Record(attribute, fields) ->
            checkTypeAttribute attribute
            Annotated([ "record" ], Struct [ "fields", fieldList fields ])
        | Type.ExtensibleRecord(attribute, variable, fields) ->
            checkTypeAttribute attribute

            Annotated(
                [ "extensibleRecord" ],
                Struct [
                    "variable", Text(nameText variable)
                    "fields", fieldList fields
                ]
            )
        | Type.Function(attribute, parameter, result) ->
            checkTypeAttribute attribute

            Annotated(
                [ "function" ],
                Struct [
                    "parameterType", typ parameter
                    "returnType", typ result
                ]
            )
        | Type.Unit attribute ->
            checkTypeAttribute attribute
            Annotated([ "unit" ], Struct [])

    let private typeParams (parameters: Name list) =
        parameters
        |> List.map (nameText >> Text)
        |> listMember

    let private constructors (entries: Type.Constructors<JsonElement>) =
        Map.toList entries
        |> List.map (fun (name, args) ->
            Struct(
                members [
                    "name", Some(Text(nameText name))
                    "args",
                    listMember (
                        args
                        |> List.map (fun (argument, item) ->
                            Struct [
                                "name", Text(nameText argument)
                                "type", typ item
                            ]
                        )
                    )
                ]
            )
        )
        |> listMember

    // Literals, patterns and values.

    let private literalOperand (value: Literal) =
        match value with
        | BoolLiteral flag -> "bool", Bool flag
        | CharLiteral c -> "char", Text(string c)
        | StringLiteral text -> "string", Text text
        | WholeNumberLiteral number -> "int", Integer(BigInteger number)
        | FloatLiteral number ->
            if not (Double.IsFinite number) then
                raise (
                    IonIrFailure
                        {
                            Code = "literal_out_of_range"
                            Message = "The classic literal holds finite floats."
                            Path = None
                            Location = None
                        }
                )

            "float", Float number
        | DecimalLiteral number -> "decimal", Text(number.ToString(CultureInfo.InvariantCulture))

    /// <c>(head { inferredType } operands)</c>, or the bare head when the attribute is <c>[]</c>.
    let private node (head: string) (attribute: JsonElement) (operands: IonText list) =
        match inferred attribute with
        | Some found -> Sexp(Symbol head :: Struct [ "inferredType", typ found ] :: operands)
        | None -> Sexp(Symbol head :: operands)

    let private omitted (attribute: JsonElement) = (inferred attribute).IsNone

    let private literal (attribute: JsonElement) (value: Literal) =
        match value, omitted attribute with
        | BoolLiteral flag, true -> Bool flag
        | WholeNumberLiteral number, true -> Integer(BigInteger number)
        | FloatLiteral number, true when Double.IsFinite number -> Float number
        | _ ->
            let head, operand = literalOperand value
            node head attribute [ operand ]

    let rec pattern (value: Pattern<JsonElement>) : IonText =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        match value with
        | WildcardPattern attribute when omitted attribute -> Symbol "_"
        | WildcardPattern attribute -> node "wildcard" attribute []
        | AsPattern(attribute, WildcardPattern inner, name) when omitted attribute && omitted inner ->
            Symbol(nameText name)
        | AsPattern(attribute, inner, name) -> node "as" attribute [ pattern inner; Symbol(nameText name) ]
        | TuplePattern(attribute, items) -> node "tuple" attribute (List.map pattern items)
        | ConstructorPattern(attribute, name, items) ->
            node "constructor" attribute (Symbol(fqText name) :: List.map pattern items)
        | EmptyListPattern attribute when omitted attribute -> IonText.List []
        | EmptyListPattern attribute -> node "emptyList" attribute []
        | HeadTailPattern(attribute, head, tail) -> node "headTail" attribute [ pattern head; pattern tail ]
        | LiteralPattern(attribute, found) -> literal attribute found
        | UnitPattern attribute when omitted attribute -> Sexp []
        | UnitPattern attribute -> node "unit" attribute []

    let rec value (element: Value<JsonElement, JsonElement>) : IonText =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        let fields (entries: Map<Name, Value<JsonElement, JsonElement>>) =
            Map.toList entries
            |> List.map (fun (name, item) -> Sexp [ Symbol(nameText name); value item ])

        match element with
        | Value.Literal(attribute, found) -> literal attribute found
        | Value.Constructor(attribute, name) -> node "constructor" attribute [ Symbol(fqText name) ]
        | Value.Tuple(attribute, items) -> node "tuple" attribute (List.map value items)
        | Value.List(attribute, items) -> node "list" attribute (List.map value items)
        | Value.Record(attribute, entries) -> node "record" attribute (fields entries)
        | Value.Variable(attribute, name) when omitted attribute -> Symbol(nameText name)
        | Value.Variable(attribute, name) -> node "variable" attribute [ Symbol(nameText name) ]
        | Value.Reference(attribute, name) -> node "ref" attribute [ Symbol(fqText name) ]
        | Value.Field(attribute, target, name) -> node "field" attribute [ value target; Symbol(nameText name) ]
        | Value.FieldFunction(attribute, name) -> node "fieldFunction" attribute [ Symbol(nameText name) ]
        | Value.Apply(attribute, func, argument) -> node "apply" attribute [ value func; value argument ]
        | Value.Lambda(attribute, parameter, body) -> node "lambda" attribute [ pattern parameter; value body ]
        | Value.LetDefinition(attribute, name, definition, body) ->
            node "let" attribute [ Symbol(nameText name); binding definition; value body ]
        | Value.LetRecursion(attribute, definitions, body) ->
            let bindings =
                Map.toList definitions
                |> List.map (fun (name, definition) -> Sexp [ Symbol(nameText name); binding definition ])

            node "letrec" attribute [ IonText.List bindings; value body ]
        | Value.Destructure(attribute, bound, source, body) ->
            node "destructure" attribute [ pattern bound; value source; value body ]
        | Value.IfThenElse(attribute, condition, thenBranch, elseBranch) ->
            node "if" attribute [ value condition; value thenBranch; value elseBranch ]
        | Value.PatternMatch(attribute, scrutinee, cases) ->
            node
                "match"
                attribute
                (value scrutinee
                 :: (cases
                     |> List.map (fun (case, body) -> IonText.List [ pattern case; value body ])))
        | Value.UpdateRecord(attribute, target, entries) -> node "update" attribute (value target :: fields entries)
        | Value.Unit attribute when omitted attribute -> Sexp []
        | Value.Unit attribute -> node "unit" attribute []

    /// <c>[ { name, type, inferredType } ]</c>. The inferred type is left out when it is the stated type.
    and private inputTypes (inputs: (Name * JsonElement * Type<JsonElement>) list) =
        inputs
        |> List.map (fun (name, attribute, declared) ->
            let stated =
                match inferred attribute with
                | Some found when not (JsonElement.DeepEquals(ClassicIonTypes.typeAsAttribute declared, attribute)) ->
                    Some(typ found)
                | Some _ -> None
                | None -> refuse "A v3 input attribute must be the classic JSON of a type."

            Struct(
                members [
                    "name", Some(Text(nameText name))
                    "type", Some(typ declared)
                    "inferredType", stated
                ]
            )
        )
        |> listMember

    /// A let binding: <c>{ inputTypes, outputType, body }</c>.
    and private binding (definition: ValueDefinition<JsonElement, JsonElement>) =
        Struct(
            members [
                "inputTypes", inputTypes definition.InputTypes
                "outputType", Some(typ definition.OutputType)
                "body", Some(value definition.Body)
            ]
        )

    // Definitions and specifications.

    let private accessName (access: Access) =
        match access with
        | Public -> "public"
        | Private -> "private"

    /// A v3 doc: the classic model holds an absent doc as the empty one.
    let private docOf (documented: Documented<'t>) =
        match documented with
        | WithDocumentation(doc, _) when doc <> "" -> Some(Text doc)
        | WithDocumentation _
        | WithoutDocumentation _ -> None

    let private moduleMember (scope: ModulePath option) = "module", scope |> Option.map (moduleText >> Text)

    let private named (name: Name option) =
        "name", name |> Option.map (nameText >> Text)

    /// A top-level type definition: <c>module</c>, <c>name</c>, then the members of its kind.
    let typeDefinition
        (scope: ModulePath option)
        (name: Name option)
        (entry: AccessControlled<Documented<Type.TypeDefinition<JsonElement>>>)
        : IonText =
        let access = accessName entry.Access
        let doc = "doc", docOf entry.Value

        match Documented.value entry.Value with
        | Type.TypeAliasDefinition(parameters, expression) ->
            Annotated(
                [ access; "def"; "alias"; "type" ],
                Struct(
                    members [
                        moduleMember scope
                        named name
                        "typeExp", Some(typ expression)
                        "typeParams", typeParams parameters
                        doc
                    ]
                )
            )
        | Type.CustomTypeDefinition(parameters, controlled) ->
            Annotated(
                [ access; "def"; "custom"; "type" ],
                Struct(
                    members [
                        moduleMember scope
                        named name
                        "access", Some(Symbol(accessName controlled.Access))
                        "constructors", constructors controlled.Value
                        "typeParams", typeParams parameters
                        doc
                    ]
                )
            )

    let valueDefinition
        (scope: ModulePath option)
        (name: Name option)
        (entry: AccessControlled<Documented<ValueDefinition<JsonElement, JsonElement>>>)
        : IonText =
        let definition = Documented.value entry.Value

        Annotated(
            [ accessName entry.Access; "def"; "value" ],
            Struct(
                members [
                    moduleMember scope
                    named name
                    "outputType", Some(typ definition.OutputType)
                    "body", Some(value definition.Body)
                    "inputTypes", inputTypes definition.InputTypes
                    "doc", docOf entry.Value
                ]
            )
        )

    let typeSpecification (name: Name option) (entry: Documented<Type.TypeSpecification<JsonElement>>) : IonText =
        let kind, fields =
            match Documented.value entry with
            | Type.OpaqueTypeSpecification parameters -> "opaque", [ "typeParams", typeParams parameters ]
            | Type.TypeAliasSpecification(parameters, expression) ->
                "alias",
                [
                    "typeExp", Some(typ expression)
                    "typeParams", typeParams parameters
                ]
            | Type.CustomTypeSpecification(parameters, entries) ->
                "custom",
                [
                    "constructors", constructors entries
                    "typeParams", typeParams parameters
                ]
            | Type.DerivedTypeSpecification(parameters, details) ->
                "derived",
                [
                    "baseType", Some(typ details.BaseType)
                    "fromBaseType", Some(Text(fqText details.FromBaseType))
                    "toBaseType", Some(Text(fqText details.ToBaseType))
                    "typeParams", typeParams parameters
                ]

        Annotated(
            [ "public"; "spec"; kind; "type" ],
            Struct(
                members (
                    named name
                    :: fields
                    @ [ "doc", docOf entry ]
                )
            )
        )

    let valueSpecification (name: Name option) (entry: Documented<ValueSpecification<JsonElement>>) : IonText =
        let specification = Documented.value entry

        Annotated(
            [ "public"; "spec"; "value" ],
            Struct(
                members [
                    named name
                    "inputs",
                    specification.Inputs
                    |> List.map (fun (input, item) ->
                        Struct [
                            "name", Text(nameText input)
                            "type", typ item
                        ]
                    )
                    |> listMember
                    "output", Some(typ specification.Output)
                    "doc", docOf entry
                ]
            )
        )

    /// A module definition's own element. Its members are top-level elements that name the module.
    let moduleDefinition (name: ModulePath option) (entry: AccessControlled<Module.ModuleDefinition<JsonElement, JsonElement>>) =
        Annotated(
            [ accessName entry.Access; "def"; "module" ],
            Struct(
                members [
                    "name", name |> Option.map (moduleText >> Text)
                    "doc", textMember entry.Value.Doc
                ]
            )
        )

    let moduleSpecification (name: ModulePath option) (inline': bool) (entry: Module.ModuleSpecification<JsonElement>) =
        let types, values =
            if inline' then
                Map.toList entry.Types
                |> List.map (fun (key, item) -> typeSpecification (Some key) item)
                |> listMember,
                Map.toList entry.Values
                |> List.map (fun (key, item) -> valueSpecification (Some key) item)
                |> listMember
            else
                None, None

        Annotated(
            [ "module"; "spec" ],
            Struct(
                members [
                    "name", name |> Option.map (moduleText >> Text)
                    "doc", textMember entry.Doc
                    "types", types
                    "values", values
                ]
            )
        )

    /// The format version a v3 header states: <c>3.0.0</c> for a library and <c>3.1.0</c> for specs.
    let formatVersion (distribution: Distribution<JsonElement, JsonElement>) =
        match distribution with
        | Library _ -> "3.0.0"
        | Specs _ -> "3.1.0"

    let header (pathBudget: int option) (distribution: Distribution<JsonElement, JsonElement>) =
        let kind, package =
            match distribution with
            | Library(package, _, _) -> "library", package
            | Specs(package, _, _) -> "specs", package

        Annotated(
            [ "morphir" ],
            Struct(
                members [
                    "ionVersion", Some(Text IonIrFormat.IonVersion)
                    "formatVersion", Some(Text(formatVersion distribution))
                    "kind", Some(Symbol kind)
                    "packageName", Some(Text(packageText package))
                    "pathBudget", pathBudget |> Option.map (BigInteger >> Integer)
                ]
            )
        )

    /// The dependencies: <c>package::spec</c> with its modules inline in a single file, and only its name in a manifest.
    let dependencies (inline': bool) (distribution: Distribution<JsonElement, JsonElement>) =
        Distribution.dependencies distribution
        |> Map.toList
        |> List.map (fun (name, spec) ->
            let modules =
                if inline' then
                    Map.toList spec.Modules
                    |> List.map (fun (key, item) -> moduleSpecification (Some key) true item)
                    |> listMember
                else
                    None

            Annotated(
                [ "package"; "spec" ],
                Struct(
                    members [
                        "name", Some(Text(packageText name))
                        "modules", modules
                    ]
                )
            )
        )

    /// The top-level values of a single-file datagram. A library writes each module, then its types and values as
    /// top-level elements that name it; a specs distribution writes each module with its members inline.
    let datagram (distribution: Distribution<JsonElement, JsonElement>) : IonText list =
        let modules =
            match distribution with
            | Library(_, _, definition) ->
                Map.toList definition.Modules
                |> List.collect (fun (key, entry) ->
                    moduleDefinition (Some key) entry
                    :: (Map.toList entry.Value.Types
                        |> List.map (fun (name, item) -> typeDefinition (Some key) (Some name) item))
                    @ (Map.toList entry.Value.Values
                       |> List.map (fun (name, item) -> valueDefinition (Some key) (Some name) item))
                )
            | Specs(_, _, specification) ->
                Map.toList specification.Modules
                |> List.map (fun (key, item) -> moduleSpecification (Some key) true item)

        header None distribution
        :: dependencies true distribution
        @ modules
        @ [ IonIrFormat.footer ]
