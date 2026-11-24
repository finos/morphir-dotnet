namespace Morphir.Ion

open System
open System.Globalization
open System.Numerics
open System.Runtime.CompilerServices
open Morphir.IR
open Morphir.IR.V4

/// Builds the canonical Ion elements of an IR v4 distribution: the inverse of <see cref="V4IonDistribution"/>.
/// Member order follows the draft and the reference writer. Every empty optional list is omitted.
module internal V4IonWriter =
    let private refuse code message : 'a =
        raise (
            IonIrFailure
                {
                    Code = code
                    Message = message
                    Path = None
                    Location = None
                }
        )

    let nameText (name: Name) = CanonicalNames.nameToString name
    let pathText (path: Path) = CanonicalNames.pathToString path
    let fqText (name: FQName) = CanonicalNames.fqNameToString name

    let packageText (PackageName path) = pathText path
    let moduleText (ModulePath path) = pathText path

    /// Members whose value is present, in order.
    let members (entries: (string * IonText option) list) =
        entries
        |> List.choose (fun (name, value) -> value |> Option.map (fun item -> name, item))

    /// An optional list member: absent when the list is empty.
    let listMember (items: IonText list) =
        if List.isEmpty items then None else Some(List items)

    let textMember (value: string option) = value |> Option.map Text

    let private typeParams (parameters: Name list) =
        parameters
        |> List.map (nameText >> Text)
        |> listMember

    // Document payloads.

    let private jsonInteger (lexeme: string) =
        let unsigned = if lexeme.StartsWith '-' then lexeme.Substring 1 else lexeme

        unsigned.Length > 0
        && Seq.forall Char.IsAsciiDigit unsigned
        && (unsigned.Length = 1 || unsigned[0] <> '0')

    /// The Ion decimal that a JSON number lexeme spells, when the reader's lexeme rule gives the lexeme back.
    let private decimalOf (lexeme: string) =
        let negative = lexeme.StartsWith '-'
        let unsigned = if negative then lexeme.Substring 1 else lexeme
        let exponentAt = unsigned.IndexOfAny [| 'e'; 'E' |]

        let mantissa, exponent =
            if exponentAt < 0 then
                unsigned, Some 0
            else
                let mutable parsed = 0

                unsigned.Substring(0, exponentAt),
                if
                    Int32.TryParse(
                        unsigned.Substring(exponentAt + 1),
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        &parsed
                    )
                then
                    Some parsed
                else
                    None

        match exponent with
        | None -> None
        // A positive exponent has no decimal whose lexeme gives it back in the reference CLI, which writes it as text.
        | Some exponent when exponent > 0 -> None
        | Some exponent ->
            let pointAt = mantissa.IndexOf '.'

            let digits, fraction =
                if pointAt < 0 then
                    mantissa, 0
                else
                    mantissa.Remove(pointAt, 1),
                    mantissa.Length
                    - pointAt
                    - 1

            if digits = "" || not (Seq.forall Char.IsAsciiDigit digits) then
                None
            else
                let coefficient = BigInteger.Parse(digits, CultureInfo.InvariantCulture)

                let value: IonDecimal = {
                    Coefficient = if negative then BigInteger.Negate coefficient else coefficient
                    Scale = fraction - exponent
                    IsNegativeZero = negative && coefficient.IsZero
                }

                if V4IonTypes.decimalLexeme value = lexeme then
                    // The Ion text of the same decimal: the mantissa with a point, then d for the exponent mark.
                    let sign = if negative then "-" else ""
                    let point = if pointAt < 0 then "." else ""

                    let power =
                        if exponentAt < 0 then
                            ""
                        else
                            "d"
                            + unsigned.Substring(exponentAt + 1)

                    Some(DecimalText(sign + mantissa + point + power))
                else
                    None

    /// A JSON number as Ion: a 64-bit int, a decimal that keeps the lexeme, or <c>number::"lexeme"</c>.
    let private documentNumber (lexeme: string) =
        if jsonInteger lexeme then
            let number = BigInteger.Parse(lexeme, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)

            // An integer beyond 64 bits stays text, so that a reader whose Ion int has 64 bits keeps it.
            if
                number.ToString(CultureInfo.InvariantCulture) = lexeme
                && number >= BigInteger Int64.MinValue
                && number <= BigInteger Int64.MaxValue
            then
                Integer number
            else
                Annotated([ "number" ], Text lexeme)
        else
            match decimalOf lexeme with
            | Some text -> text
            | None -> Annotated([ "number" ], Text lexeme)

    let rec document (value: DocumentValue) : IonText =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        match value with
        | DocumentNull -> Null
        | DocumentBool flag -> Bool flag
        | DocumentNumber lexeme -> documentNumber lexeme
        | DocumentString text -> Text text
        | DocumentArray items -> List(List.map document items)
        | DocumentObject entries ->
            Struct(
                OrderedMap.toList entries
                |> List.map (fun (name, item) -> name, document item)
            )

    let private source (location: SourceLocation) =
        let number (value: int) = Integer(BigInteger value)

        Struct [
            "startLine", number location.StartLine
            "startColumn", number location.StartColumn
            "endLine", number location.EndLine
            "endColumn", number location.EndColumn
        ]

    // Types.

    let private typeAttributeMembers (attributes: TypeAttributes) =
        members [
            "source", attributes.Source |> Option.map source
            "constraints", attributes.Constraints |> Option.map document
            "extensions", attributes.Extensions |> Option.map document
        ]

    let rec typ (value: Type) : IonText =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        let expanded head (own: (string * IonText) list) attributes =
            Annotated([ head ], Struct(own @ typeAttributeMembers attributes))

        let isPlain (attributes: TypeAttributes) = attributes = TypeAttributes.empty

        match value with
        | Type.Variable(attributes, name) when isPlain attributes -> Text(nameText name)
        | Type.Variable(attributes, name) -> expanded "variable" [ "name", Text(nameText name) ] attributes
        | Type.Reference(attributes, name, []) when isPlain attributes -> Text(fqText name)
        | Type.Reference(attributes, name, arguments) ->
            expanded
                "reference"
                (members [
                    "name", Some(Text(fqText name))
                    "arguments", listMember (List.map typ arguments)
                ])
                attributes
        | Type.Tuple(attributes, elements) when isPlain attributes ->
            Annotated([ "tuple" ], List(List.map typ elements))
        | Type.Tuple(attributes, elements) ->
            expanded "tuple" [ "elements", List(List.map typ elements) ] attributes
        | Type.Record(attributes, fields) -> expanded "record" [ "fields", namedTypes fields ] attributes
        | Type.ExtensibleRecord(attributes, variable, fields) ->
            expanded
                "extensibleRecord"
                [
                    "variable", Text(nameText variable)
                    "fields", namedTypes fields
                ]
                attributes
        | Type.Function(attributes, parameter, result) ->
            expanded
                "function"
                [
                    "parameterType", typ parameter
                    "returnType", typ result
                ]
                attributes
        | Type.Unit attributes -> expanded "unit" [] attributes

    /// <c>[ { name, type } ]</c>.
    and namedTypes (entries: (Name * Type) list) =
        List(
            entries
            |> List.map (fun (name, item) ->
                Struct [
                    "name", Text(nameText name)
                    "type", typ item
                ]
            )
        )

    /// A struct keyed by parameter name, in parameter order, or nothing when there is none.
    let keyedTypes (entries: (Name * Type) list) =
        match entries with
        | [] -> None
        | _ ->
            Some(
                Struct(
                    entries
                    |> List.map (fun (name, item) -> nameText name, typ item)
                )
            )

    let private holeReason (reason: HoleReason) =
        match reason with
        | UnresolvedReference target ->
            Annotated([ "unresolvedReference" ], Struct [ "target", Text(fqText target) ])
        | DeletedDuringRefactor txId -> Annotated([ "deletedDuringRefactor" ], Struct [ "txId", Text txId ])
        | TypeMismatch(expected, found) ->
            Annotated(
                [ "typeMismatch" ],
                Struct [
                    "expected", Text expected
                    "found", Text found
                ]
            )

    let incompleteness (value: Incompleteness) =
        match value with
        | Incompleteness.Draft -> Symbol "draft"
        | Incompleteness.Hole(reason, partial) ->
            Annotated(
                [ "hole" ],
                Struct(
                    members [
                        "reason", Some(holeReason reason)
                        "partialBody", partial |> Option.map typ
                    ]
                )
            )

    // Values and patterns.

    let private valueAttributeMembers (attributes: ValueAttributes) =
        members [
            "source", attributes.Source |> Option.map source
            "inferredType", attributes.InferredType |> Option.map typ
            "extensions", attributes.Extensions |> Option.map document
        ]

    /// <c>(head)</c>, with the attribute struct after the head when there are attributes.
    let private node (head: string) (attributes: ValueAttributes) (operands: IonText list) =
        match valueAttributeMembers attributes with
        | [] -> Sexp(Symbol head :: operands)
        | found -> Sexp(Symbol head :: Struct found :: operands)

    let private plain (attributes: ValueAttributes) = attributes = ValueAttributes.empty

    /// The integers a 128-bit Ion int holds, the widest the reference CLI reads. A wider integer is written as its
    /// digits, <c>(int "digits")</c>, which every reader takes.
    let private int128Bound = BigInteger.Pow(BigInteger 2, 127)

    let private fitsIonInt (number: BigInteger) =
        number >= -int128Bound
        && number < int128Bound

    let private integerOperand (number: BigInteger) =
        if fitsIonInt number then
            Integer number
        else
            Text(number.ToString(CultureInfo.InvariantCulture))

    let private floatLexeme (value: FloatValue) =
        match value.Lexeme with
        | Some lexeme -> lexeme
        | None -> value.Value.ToString("R", CultureInfo.InvariantCulture)

    /// A literal in its shorthand when there are no attributes, and in its S-expression otherwise.
    let private literal (attributes: ValueAttributes) (value: Literal) =
        match value with
        | BoolLiteral flag when plain attributes -> Bool flag
        | BoolLiteral flag -> node "bool" attributes [ Bool flag ]
        | IntegerLiteral number when plain attributes && fitsIonInt number -> Integer number
        | IntegerLiteral number -> node "int" attributes [ integerOperand number ]
        | FloatLiteral number when plain attributes && number.Lexeme.IsNone -> Float number.Value
        | FloatLiteral number -> node "float" attributes [ Text(floatLexeme number) ]
        | StringLiteral text -> node "string" attributes [ Text text ]
        | CharLiteral text -> node "char" attributes [ Text text ]
        | DecimalLiteral lexeme -> node "decimal" attributes [ Text(DecimalLexeme.value lexeme) ]
        | DocumentLiteral payload -> node "document" attributes [ document payload ]

    let rec pattern (value: Pattern) : IonText =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        match value with
        | WildcardPattern attributes when plain attributes -> Symbol "_"
        | WildcardPattern attributes -> node "wildcard" attributes []
        | AsPattern(attributes, WildcardPattern inner, name) when plain attributes && plain inner ->
            Symbol(nameText name)
        | AsPattern(attributes, inner, name) -> node "as" attributes [ pattern inner; Symbol(nameText name) ]
        | TuplePattern(attributes, items) -> node "tuple" attributes (List.map pattern items)
        | ConstructorPattern(attributes, name, items) ->
            node "constructor" attributes (Symbol(fqText name) :: List.map pattern items)
        | EmptyListPattern attributes when plain attributes -> List []
        | EmptyListPattern attributes -> node "emptyList" attributes []
        | HeadTailPattern(attributes, head, tail) -> node "headTail" attributes [ pattern head; pattern tail ]
        | LiteralPattern(_, DocumentLiteral _) ->
            refuse "unsupported_node" "A document literal cannot appear in a pattern."
        | LiteralPattern(attributes, found) -> literal attributes found
        | UnitPattern attributes when plain attributes -> Sexp []
        | UnitPattern attributes -> node "unit" attributes []

    let rec value (element: Value) : IonText =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        let fields (entries: (Name * Value) list) =
            entries
            |> List.map (fun (name, item) -> Sexp [ Symbol(nameText name); value item ])

        match element with
        | Value.Literal(attributes, found) -> literal attributes found
        | Value.Constructor(attributes, name) -> node "constructor" attributes [ Symbol(fqText name) ]
        | Value.Tuple(attributes, items) -> node "tuple" attributes (List.map value items)
        | Value.List(attributes, items) -> node "list" attributes (List.map value items)
        | Value.Record(attributes, entries) -> node "record" attributes (fields entries)
        | Value.Variable(attributes, name) when plain attributes -> Symbol(nameText name)
        | Value.Variable(attributes, name) -> node "variable" attributes [ Symbol(nameText name) ]
        | Value.Reference(attributes, name) -> node "ref" attributes [ Symbol(fqText name) ]
        | Value.Field(attributes, target, name) -> node "field" attributes [ value target; Symbol(nameText name) ]
        | Value.FieldFunction(attributes, name) -> node "fieldFunction" attributes [ Symbol(nameText name) ]
        | Value.Apply(attributes, func, argument) -> node "apply" attributes [ value func; value argument ]
        | Value.Lambda(attributes, parameter, body) -> node "lambda" attributes [ pattern parameter; value body ]
        | Value.LetDefinition(attributes, name, definition, body) ->
            node "let" attributes [ Symbol(nameText name); letBinding definition; value body ]
        | Value.LetRecursion(attributes, definitions, body) ->
            let bindings =
                OrderedMap.toList definitions
                |> List.map (fun (name, definition) -> Sexp [ Symbol(nameText name); letBinding definition ])

            node "letrec" attributes [ List bindings; value body ]
        | Value.Destructure(attributes, bound, source, body) ->
            node "destructure" attributes [ pattern bound; value source; value body ]
        | Value.IfThenElse(attributes, condition, thenBranch, elseBranch) ->
            node "if" attributes [ value condition; value thenBranch; value elseBranch ]
        | Value.PatternMatch(attributes, scrutinee, cases) ->
            node
                "match"
                attributes
                (value scrutinee
                 :: (cases
                     |> List.map (fun (case, body) -> List [ pattern case; value body ])))
        | Value.UpdateRecord(attributes, target, entries) -> node "update" attributes (value target :: fields entries)
        | Value.Unit attributes when plain attributes -> Sexp []
        | Value.Unit attributes -> node "unit" attributes []
        | Value.Hole(attributes, reason, expected) ->
            node "hole" attributes (holeReason reason :: (expected |> Option.map typ |> Option.toList))

    /// A let binding: <c>{ inputTypes, outputType, body }</c>. Ion holds only an expression body with no doc there.
    and private letBinding (definition: Documented<ValueDefinition>) =
        match definition.Doc, definition.Value.Body with
        | Some _, _ ->
            refuse "unsupported_node" "A let binding has no documentation in the Ion format."
        | None, ExpressionBody(output, body) ->
            Struct(
                members [
                    "inputTypes", keyedTypes definition.Value.InputTypes
                    "outputType", Some(typ output)
                    "body", Some(value body)
                ]
            )
        | None, _ -> refuse "unsupported_node" "A let binding holds an expression body in the Ion format."

    // Definitions and specifications.

    let private accessName (access: Access) =
        match access with
        | Public -> "public"
        | Private -> "private"

    let private constructors (entries: Constructors) =
        OrderedMap.toList entries
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

    /// The members a path cannot supply come first: <c>name</c> is left out in a tree file that the path names.
    let private named (name: Name option) =
        "name", name |> Option.map (nameText >> Text)

    let typeDefinition
        (name: Name option)
        (entry: AccessControlled<Documented<TypeDefinition>>)
        : IonText =
        let access = accessName entry.Access
        let doc = "doc", textMember entry.Value.Doc

        match entry.Value.Value with
        | TypeAliasDefinition(parameters, expression) ->
            Annotated(
                [ access; "def"; "alias"; "type" ],
                Struct(
                    members [
                        named name
                        "typeParams", typeParams parameters
                        "typeExp", Some(typ expression)
                        doc
                    ]
                )
            )
        | CustomTypeDefinition(parameters, constructorAccess, entries) ->
            Annotated(
                [ access; "def"; "custom"; "type" ],
                Struct(
                    members [
                        named name
                        "typeParams", typeParams parameters
                        "access", Some(Symbol(accessName constructorAccess))
                        "constructors", constructors entries
                        doc
                    ]
                )
            )
        | IncompleteTypeDefinition(parameters, state, partial) ->
            Annotated(
                [ access; "def"; "incomplete"; "type" ],
                Struct(
                    members [
                        named name
                        "typeParams", typeParams parameters
                        "incompleteness", Some(incompleteness state)
                        "partialTypeExp", partial |> Option.map typ
                        doc
                    ]
                )
            )

    let private nativeHint (hint: NativeHint) =
        match hint with
        | Arithmetic -> Symbol "arithmetic"
        | Comparison -> Symbol "comparison"
        | StringOp -> Symbol "stringOp"
        | CollectionOp -> Symbol "collectionOp"
        | PlatformSpecific platform -> Annotated([ "platformSpecific" ], Struct [ "platform", Text platform ])

    let valueDefinition
        (name: Name option)
        (entry: AccessControlled<Documented<ValueDefinition>>)
        : IonText =
        let access = accessName entry.Access
        let definition = entry.Value.Value
        let inputs = "inputTypes", keyedTypes definition.InputTypes
        let doc = "doc", textMember entry.Value.Doc

        let kind, body =
            match definition.Body with
            | ExpressionBody(output, body) ->
                [ "value" ],
                [
                    "outputType", Some(typ output)
                    "body", Some(value body)
                ]
            | NativeBody(output, info) ->
                [ "native"; "value" ],
                [
                    "outputType", Some(typ output)
                    "hint", Some(nativeHint info.Hint)
                    "description", textMember info.Description
                ]
            | ExternalBody(output, info) ->
                if List.isEmpty info.Externals then
                    refuse "unsupported_node" "An external body needs at least one binding."

                [ "external"; "value" ],
                [
                    "outputType", Some(typ output)
                    "externals",
                    Some(
                        List(
                            info.Externals
                            |> List.map (fun binding ->
                                Struct [
                                    "targetPlatform", Text binding.TargetPlatform
                                    "externalName", Text binding.ExternalName
                                ]
                            )
                        )
                    )
                    "body", info.Fallback |> Option.map value
                ]
            | IncompleteBody(output, state, partial) ->
                [ "incomplete"; "value" ],
                [
                    "outputType", output |> Option.map typ
                    "incompleteness", Some(incompleteness state)
                    "body", partial |> Option.map value
                ]

        Annotated(
            access :: "def" :: kind,
            Struct(
                members (
                    [ named name; inputs ]
                    @ body
                    @ [ doc ]
                )
            )
        )

    // Morphir annotations.

    let private annotationArgument (argument: AnnotationArgument) =
        match argument with
        | PositionalArgument item -> value item
        | NamedArgument(name, item) ->
            Struct [
                "name", Text(nameText name)
                "value", value item
            ]

    /// The compact string when it means the same annotation, and the structured form otherwise.
    let private annotation (entry: Annotation) =
        let name = fqText entry.Name

        match entry.Arguments with
        | [] -> Text name
        | [ PositionalArgument(Value.Literal(attributes, StringLiteral text)) ] when
            plain attributes
            && text <> ""
            ->
            Text(name + ":" + text)
        | arguments ->
            Struct(
                members [
                    "name", Some(Text name)
                    "arguments", listMember (List.map annotationArgument arguments)
                ]
            )

    let annotations (entries: Annotation list) =
        "annotations", listMember (List.map annotation entries)

    let typeSpecification (name: Name option) (entry: Documented<TypeSpecification>) : IonText =
        let doc = "doc", textMember entry.Doc

        let kind, fields =
            match entry.Value with
            | TypeAliasSpecification(stated, parameters, expression) ->
                "alias",
                [
                    "typeParams", typeParams parameters
                    "typeExp", Some(typ expression)
                    annotations stated
                ]
            | OpaqueTypeSpecification(stated, parameters) ->
                "opaque", [ "typeParams", typeParams parameters; annotations stated ]
            | CustomTypeSpecification(stated, parameters, entries) ->
                "custom",
                [
                    "typeParams", typeParams parameters
                    "constructors", constructors entries
                    annotations stated
                ]
            | DerivedTypeSpecification(stated, parameters, baseType, fromBase, toBase) ->
                "derived",
                [
                    "typeParams", typeParams parameters
                    "baseType", Some(typ baseType)
                    "fromBaseType", Some(Text(fqText fromBase))
                    "toBaseType", Some(Text(fqText toBase))
                    annotations stated
                ]

        Annotated(
            [ "public"; "spec"; kind; "type" ],
            Struct(
                members (
                    named name
                    :: fields
                    @ [ doc ]
                )
            )
        )

    let valueSpecification (name: Name option) (entry: Documented<ValueSpecification>) : IonText =
        Annotated(
            [ "public"; "spec"; "value" ],
            Struct(
                members [
                    named name
                    annotations entry.Value.Annotations
                    "inputs", keyedTypes entry.Value.Inputs
                    "output", Some(typ entry.Value.Output)
                    "doc", textMember entry.Doc
                ]
            )
        )

    /// <summary>
    /// A module definition. <paramref name="inline"/> puts its types and values inside it, as a single file does; a tree
    /// writes them to their own files.
    /// </summary>
    let moduleDefinition
        (name: ModulePath option)
        (inline': bool)
        (entry: AccessControlled<ModuleDefinition>)
        : IonText =
        let definition = entry.Value

        let types, values =
            if inline' then
                OrderedMap.toList definition.Types
                |> List.map (fun (key, item) -> typeDefinition (Some key) item)
                |> listMember,
                OrderedMap.toList definition.Values
                |> List.map (fun (key, item) -> valueDefinition (Some key) item)
                |> listMember
            else
                None, None

        Annotated(
            [ accessName entry.Access; "def"; "module" ],
            Struct(
                members [
                    "name", name |> Option.map (moduleText >> Text)
                    "doc", textMember definition.Doc
                    "types", types
                    "values", values
                ]
            )
        )

    let moduleSpecification (name: ModulePath option) (inline': bool) (entry: ModuleSpecification) : IonText =
        let types, values =
            if inline' then
                OrderedMap.toList entry.Types
                |> List.map (fun (key, item) -> typeSpecification (Some key) item)
                |> listMember,
                OrderedMap.toList entry.Values
                |> List.map (fun (key, item) -> valueSpecification (Some key) item)
                |> listMember
            else
                None, None

        Annotated(
            [ "module"; "spec" ],
            Struct(
                members [
                    "name", name |> Option.map (moduleText >> Text)
                    annotations entry.Annotations
                    "doc", textMember entry.Doc
                    "types", types
                    "values", values
                ]
            )
        )

    let private entryPointKind (kind: EntryPointKind) =
        match kind with
        | Main -> "main"
        | Command -> "command"
        | Handler -> "handler"
        | Job -> "job"
        | Policy -> "policy"

    let private entryPoints (entries: (string * EntryPoint) list) =
        match entries with
        | [] -> None
        | _ ->
            Some(
                Struct(
                    entries
                    |> List.map (fun (key, entry) ->
                        key,
                        Struct(
                            members [
                                "target", Some(Text(fqText entry.Target))
                                "kind", Some(Symbol(entryPointKind entry.Kind))
                                "doc", textMember entry.Doc
                            ]
                        )
                    )
                )
            )

    /// The <c>morphir::</c> header. A tree's manifest adds its path budget.
    let header (pathBudget: int option) (distribution: Distribution) =
        let kind, package, points =
            match distribution with
            | Library(package, _, _) -> "library", package, []
            | Specs(package, _, _) -> "specs", package, []
            | Application(package, _, _, points) -> "application", package, points

        Annotated(
            [ "morphir" ],
            Struct(
                members [
                    "ionVersion", Some(Text IonIrFormat.IonVersion)
                    "formatVersion", Some(Text "4.0.0")
                    "kind", Some(Symbol kind)
                    "packageName", Some(Text(packageText package))
                    "entryPoints", entryPoints points
                    "pathBudget", pathBudget |> Option.map (BigInteger >> Integer)
                ]
            )
        )

    /// The dependencies as the manifest names them, or as a single file holds them with their modules inline.
    let dependencies (inline': bool) (distribution: Distribution) =
        let package (head: string list) (name: PackageName) (modules: IonText list) =
            Annotated(
                head,
                Struct(
                    members [
                        "name", Some(Text(packageText name))
                        "modules", (if inline' then listMember modules else None)
                    ]
                )
            )

        let specification (name, spec: PackageSpecification) =
            package
                [ "package"; "spec" ]
                name
                (OrderedMap.toList spec.Modules
                 |> List.map (fun (key, item) -> moduleSpecification (Some key) true item))

        match distribution with
        | Library(_, deps, _)
        | Specs(_, deps, _) -> OrderedMap.toList deps |> List.map specification
        | Application(_, deps, _, _) ->
            OrderedMap.toList deps
            |> List.map (fun (name, definition) ->
                package
                    [ "package"; "def" ]
                    name
                    (OrderedMap.toList definition.Modules
                     |> List.map (fun (key, item) -> moduleDefinition (Some key) true item))
            )

    /// The top-level values of a single-file datagram: the header, the dependencies, the modules with their members
    /// inline, and the footer.
    let datagram (distribution: Distribution) : IonText list =
        let modules =
            match distribution with
            | Library(_, _, definition)
            | Application(_, _, definition, _) ->
                OrderedMap.toList definition.Modules
                |> List.map (fun (key, item) -> moduleDefinition (Some key) true item)
            | Specs(_, _, specification) ->
                OrderedMap.toList specification.Modules
                |> List.map (fun (key, item) -> moduleSpecification (Some key) true item)

        header None distribution
        :: dependencies true distribution
        @ modules
        @ [ IonIrFormat.footer ]
