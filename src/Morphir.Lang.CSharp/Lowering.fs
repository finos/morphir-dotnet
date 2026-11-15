// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open System.Collections.Generic
open System.Numerics
open System.Text
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Microsoft.CodeAnalysis.Operations
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.IR

/// <summary>What a name in scope stands for.</summary>
type internal Binding =
    /// <summary>A parameter or a local that is a Morphir variable.</summary>
    | Plain
    /// <summary>
    /// A local bound by a pattern on a case record: its fields are Morphir variables, and the value itself is the
    /// constructor applied to them.
    /// </summary>
    | CaseFields of constructor: FQName * fields: (string * Name * ITypeSymbol) list * caseType: ITypeSymbol

/// <summary>A call of the equality or the order of .NET on two values of a type parameter of the method.</summary>
type internal TypeParameterCall =
    /// <summary><c>a.Equals(b)</c> or <c>EqualityComparer&lt;T&gt;.Default.Equals(a, b)</c>: the type parameter and the two operands.</summary>
    | TypeParameterEquality of ITypeSymbol * IOperation * IOperation
    /// <summary><c>a.CompareTo(b)</c> or <c>Comparer&lt;T&gt;.Default.Compare(a, b)</c>: the type parameter and the two operands.</summary>
    | TypeParameterOrder of ITypeSymbol * IOperation * IOperation

/// <summary>The names in scope. C# does not allow a local to hide another, so the name is the key.</summary>
type internal Env = Map<string, Binding>

/// <summary>
/// Lowers a planned package to Morphir IR with one builder: types, patterns and values. A construct outside the subset
/// gives an error with a code <c>csharp.unsupported_&lt;construct&gt;</c>.
/// </summary>
[<Sealed>]
type internal Lowerer<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package>
    (
        builder: IIrBuilder<'Type, 'Pattern, 'Value, 'ValueDefinition, 'TypeDefinition, 'Module, 'Package>,
        plan: Plan
    ) =

    let compiled = plan.Compiled

    let fallback =
        Morphir.Compilation.Location.Create("morphir:unknown", Range.Create(Position.Create(0, 0), Position.Create(0, 0)))

    let locate (node: SyntaxNode) =
        CSharpCompiler.locationOfNode compiled node
        |> Option.defaultValue fallback

    let unsupported (slug: string) (location: Morphir.Compilation.Location) (message: string) : Checked<'a> =
        Checked.fail [
            Diagnostic.error (CSharpCodes.unsupported slug) message
            |> Diagnostic.at location
        ]

    let unsupportedReference (location: Morphir.Compilation.Location) (name: string) : Checked<'a> =
        Checked.fail [
            Diagnostic.error
                CSharpCodes.unsupportedReference
                $"The C# symbol '{name}' has no Morphir mapping. Use a type of this package or a mapped Morphir SDK type."
            |> Diagnostic.at location
        ]

    let slugOf (operation: IOperation) =
        match operation.Kind with
        | OperationKind.Throw -> "throw"
        | OperationKind.Try -> "try"
        | OperationKind.Await -> "await"
        | OperationKind.Using -> "using"
        | OperationKind.Lock -> "lock"
        | OperationKind.Branch -> "goto"
        | OperationKind.YieldReturn
        | OperationKind.YieldBreak -> "yield"
        | OperationKind.Loop -> "loop"
        | OperationKind.TypeOf -> "typeof"
        | OperationKind.NameOf -> "nameof"
        | OperationKind.ArrayCreation
        | OperationKind.ArrayElementReference -> "array"
        | OperationKind.InterpolatedString -> "interpolated_string"
        | OperationKind.Coalesce -> "null_coalescing"
        | OperationKind.ConditionalAccess -> "null_conditional"
        | OperationKind.IsType
        | OperationKind.IsPattern -> "type_test"
        | OperationKind.Increment
        | OperationKind.Decrement
        | OperationKind.SimpleAssignment
        | OperationKind.CompoundAssignment -> "mutation"
        | _ -> "expression"

    /// <summary>
    /// The words that name a construct in a message. They come from the source text, not from the name of a Roslyn
    /// enum, so a message is the same in every host: a construct that starts with a keyword is named by the keyword,
    /// and any other construct by the start of its text.
    /// </summary>
    let describe (what: string) (syntax: SyntaxNode) =
        let first = syntax.GetFirstToken()

        if SyntaxFacts.IsKeywordKind(first.Kind()) then
            $"'{first.ValueText}' {what}"
        else
            let text = System.String.Join(" ", syntax.ToString().Split([| ' '; '\t'; '\r'; '\n' |], System.StringSplitOptions.RemoveEmptyEntries))
            let shown = if text.Length > 40 then text.Substring(0, 40) + "..." else text
            $"{what} '{shown}'"

    let rune (c: char) = Rune c

    let nameOf (name: string) = Name.fromString name

    /// <summary>The Morphir names of the identifiers of each member declaration, for the variables of case fields.</summary>
    let memberNames = Dictionary<SyntaxNode, Set<Name>>()

    let namesOfMember (node: SyntaxNode) =
        let scope =
            node.AncestorsAndSelf()
            |> Seq.tryFind (fun n -> n :? MemberDeclarationSyntax)
            |> Option.defaultValue (node.SyntaxTree.GetRoot())

        match memberNames.TryGetValue scope with
        | true, names -> names
        | _ ->
            let names =
                scope.DescendantTokens()
                |> Seq.filter (fun token -> token.IsKind SyntaxKind.IdentifierToken)
                |> Seq.map (fun token -> nameOf token.ValueText)
                |> Set.ofSeq

            memberNames[scope] <- names
            names

    /// <summary>
    /// The variable bound for a field of a case record that a pattern binds to a local: the words of the local and of the
    /// field. When an identifier of the member already has that Morphir name, a number follows, so the variable does not
    /// capture a parameter or a local such as <c>mQuantity</c>.
    /// </summary>
    let caseVariable (at: SyntaxNode) (local: string) (field: string) =
        let words = Name.toList (nameOf local) @ Name.toList (nameOf field)
        let taken = namesOfMember at

        let rec pick (count: int) =
            let candidate = if count = 1 then Name words else Name(words @ [ string count ])
            if Set.contains candidate taken then pick (count + 1) else candidate

        pick 1

    // ----- types -----

    member this.Type(at: Morphir.Compilation.Location, t: ITypeSymbol) : Checked<'Type> =
        match t with
        | null -> builder.TypeUnit at
        | :? ITypeParameterSymbol as parameter -> builder.TypeVariable(at, Planning.typeParameterName parameter.Name)
        | :? IArrayTypeSymbol -> unsupported "array" at "A C# array has no Morphir type. Use a list."
        | :? INamedTypeSymbol as named ->
            if named.IsTupleType then
                named.TupleElements
                |> List.ofSeq
                |> Checked.traverse (fun element -> this.Type(at, element.Type))
                |> Checked.bind (fun elements -> builder.TypeTuple(at, elements))
            elif named.SpecialType = SpecialType.System_Void then
                builder.TypeUnit at
            elif named.OriginalDefinition.ToDisplayString() = "Morphir.SDK.CSharp.Unit" then
                builder.TypeUnit at
            elif named.TypeKind = TypeKind.Delegate && named.Name = "Func" then
                // Func<A, B, R> is the curried function A -> B -> R.
                let arguments = List.ofSeq named.TypeArguments

                this.Function(at, List.take (arguments.Length - 1) arguments, List.last arguments)
            elif plan.ConstructorNames.ContainsKey named.OriginalDefinition && not (isNull named.BaseType) then
                // A case record is a constructor of its closed record: as a type it is the custom type.
                this.Type(at, named.BaseType)
            elif plan.TypeNames.ContainsKey named.OriginalDefinition then
                let fq, _ = plan.TypeNames[named.OriginalDefinition]

                named.TypeArguments
                |> List.ofSeq
                |> Checked.traverse (fun argument -> this.Type(at, argument))
                |> Checked.bind (fun arguments -> builder.TypeReference(at, fq, arguments))
            else
                match SdkMapping.tryType named with
                | Some(fq, order) ->
                    let arguments = List.ofSeq named.TypeArguments

                    order
                    |> List.map (fun index -> arguments[index])
                    |> Checked.traverse (fun argument -> this.Type(at, argument))
                    |> Checked.bind (fun lowered -> builder.TypeReference(at, fq, lowered))
                | None -> unsupportedReference at (named.ToDisplayString())
        | other -> unsupportedReference at (other.ToDisplayString())

    /// <summary>The curried function type from the given argument types to a result type.</summary>
    member this.Function(at: Morphir.Compilation.Location, arguments: ITypeSymbol list, result: ITypeSymbol) : Checked<'Type> =
        let rec build (remaining: ITypeSymbol list) =
            match remaining with
            | [] -> this.Type(at, result)
            | first :: rest ->
                check {
                    let! argument = this.Type(at, first)
                    and! tail = build rest
                    return! builder.TypeFunction(at, argument, tail)
                }

        build arguments

    // ----- patterns -----

    member private this.CaseOf(t: ITypeSymbol) =
        match t with
        | :? INamedTypeSymbol as named ->
            match plan.ConstructorNames.TryGetValue named.OriginalDefinition with
            | true, (fq, case) -> Some(fq, case)
            | _ -> None
        | _ -> None

    member private this.LiteralOf(at: Morphir.Compilation.Location, value: obj, t: ITypeSymbol) : Checked<LiteralValue> =
        match value with
        | :? int as i -> Checked.pass (LiteralValue.Integer(BigInteger i))
        | :? double as d -> Checked.pass (LiteralValue.Float d)
        | :? decimal as d -> Checked.pass (LiteralValue.Decimal d)
        | :? string as s -> Checked.pass (LiteralValue.String s)
        | :? char as c -> Checked.pass (LiteralValue.Char(rune c))
        | :? bool as b -> Checked.pass (LiteralValue.Bool b)
        | _ -> unsupported "literal" at $"The constant of type '{t.ToDisplayString()}' has no Morphir literal."

    /// <summary>The pattern of a constant: a literal, or an enum member as a constructor with no arguments.</summary>
    member private this.ConstantPattern(at: Morphir.Compilation.Location, value: IOperation, inputType: ITypeSymbol) : Checked<'Pattern> =
        // An enum member has a constant value too, so the enum test comes first: the member is a constructor, not its number.
        let isEnum = not (isNull value.Type) && value.Type.TypeKind = TypeKind.Enum

        if not isEnum && value.ConstantValue.HasValue && not (isNull value.ConstantValue.Value) then
            check {
                let! t = this.Type(at, inputType)
                let! literal = this.LiteralOf(at, value.ConstantValue.Value, inputType)
                return! builder.PatternLiteral(at, t, literal)
            }
        elif isEnum then
            match value with
            | :? IFieldReferenceOperation as field ->
                match plan.ConstructorNames.TryGetValue field.Field.OriginalDefinition with
                | true, (fq, _) ->
                    check {
                        let! t = this.Type(at, inputType)
                        return! builder.PatternConstructor(at, t, fq, [])
                    }
                | _ -> unsupportedReference at (field.Field.ToDisplayString())
            | _ -> unsupported "pattern" at "The constant pattern is not a literal or an enum member."
        else
            unsupported "pattern" at "The constant pattern is not a literal or an enum member."

    member this.Pattern(env: Env, pattern: IPatternOperation) : Checked<'Pattern * Env> =
        let at = locate pattern.Syntax
        let inferred () = this.Type(at, pattern.NarrowedType)

        match pattern with
        | :? IDiscardPatternOperation ->
            check {
                let! t = this.Type(at, pattern.InputType)
                let! p = builder.PatternWildcard(at, t)
                return p, env
            }

        | :? IDeclarationPatternOperation as declaration ->
            let matched = declaration.MatchedType

            match this.CaseOf matched, declaration.DeclaredSymbol with
            | Some(fq, case), symbol ->
                let local = if isNull symbol then "_" else symbol.Name

                let fields = [ for _, member', fieldType in case.Fields -> member', caseVariable pattern.Syntax local member', fieldType ]

                check {
                    let! t = this.Type(at, matched)

                    let! wildcards =
                        fields
                        |> Checked.traverse (fun (_, variable, fieldType) ->
                            check {
                                let! ft = this.Type(at, fieldType)

                                if isNull symbol then
                                    let! w = builder.PatternWildcard(at, ft)
                                    return w
                                else
                                    let! w = builder.PatternWildcard(at, ft)
                                    let! v = builder.PatternAs(at, ft, w, variable)
                                    return v
                            })

                    let! ctor = builder.PatternConstructor(at, t, fq, wildcards)

                    let env' =
                        if isNull symbol then env
                        else Map.add local (CaseFields(fq, fields, matched)) env

                    return ctor, env'
                }
            | None, symbol when not (isNull symbol) ->
                // `var x` or a declaration of the input type: the whole value is bound to a name.
                check {
                    let! t = this.Type(at, pattern.InputType)
                    let! w = builder.PatternWildcard(at, t)
                    let! bound = builder.PatternAs(at, t, w, nameOf symbol.Name)
                    return bound, Map.add symbol.Name Plain env
                }
            | None, _ ->
                unsupported "pattern" at $"The declaration pattern on '{matched.ToDisplayString()}' is not a case of a custom type."

        | :? ITypePatternOperation as typePattern ->
            match this.CaseOf typePattern.MatchedType with
            | Some(fq, case) ->
                check {
                    let! t = this.Type(at, typePattern.MatchedType)

                    let! wildcards =
                        case.Fields
                        |> Checked.traverse (fun (_, _, fieldType) ->
                            check {
                                let! ft = this.Type(at, fieldType)
                                return! builder.PatternWildcard(at, ft)
                            })

                    let! ctor = builder.PatternConstructor(at, t, fq, wildcards)
                    return ctor, env
                }
            | None -> unsupported "pattern" at $"The type pattern '{typePattern.MatchedType.ToDisplayString()}' is not a case of a custom type."

        | :? IConstantPatternOperation as constant ->
            this.ConstantPattern(at, constant.Value, pattern.InputType) |> Checked.map (fun p -> p, env)

        | :? IRecursivePatternOperation as recursive when recursive.NarrowedType.IsTupleType ->
            // A positional pattern on a tuple.
            let rec walk (env: Env) (patterns: IPatternOperation list) (done': 'Pattern list) : Checked<'Pattern list * Env> =
                match patterns with
                | [] -> Checked.pass (List.rev done', env)
                | first :: rest ->
                    this.Pattern(env, first)
                    |> Checked.bind (fun (lowered, env') -> walk env' rest (lowered :: done'))

            check {
                let! t = this.Type(at, recursive.NarrowedType)
                let! elements, env' = walk env (List.ofSeq recursive.DeconstructionSubpatterns) []
                let! p = builder.PatternTuple(at, t, elements)
                return p, env'
            }

        | :? IRecursivePatternOperation as recursive ->
            match this.CaseOf recursive.MatchedType with
            | Some(fq, case) ->
                let rec walk (env: Env) (patterns: IPatternOperation list) (done': 'Pattern list) : Checked<'Pattern list * Env> =
                    match patterns with
                    | [] -> Checked.pass (List.rev done', env)
                    | first :: rest ->
                        this.Pattern(env, first)
                        |> Checked.bind (fun (lowered, env') -> walk env' rest (lowered :: done'))

                let positional = List.ofSeq recursive.DeconstructionSubpatterns

                let subpatterns: Checked<IPatternOperation list> =
                    if not positional.IsEmpty then
                        Checked.pass positional
                    else
                        // Property subpatterns: each named field takes its pattern, the others take a wildcard.
                        let byName = Dictionary<string, IPatternOperation>()

                        for property in recursive.PropertySubpatterns do
                            match property.Member with
                            | :? IMemberReferenceOperation as reference -> byName[reference.Member.Name] <- property.Pattern
                            | _ -> ()

                        Checked.pass [ for _, member', _ in case.Fields do
                                           match byName.TryGetValue member' with
                                           | true, p -> yield p
                                           | _ -> yield null ]

                subpatterns
                |> Checked.bind (fun subpatterns ->
                    let rec lower (env: Env) (index: int) (items: IPatternOperation list) (done': 'Pattern list) =
                        match items with
                        | [] -> Checked.pass (List.rev done', env)
                        | item :: rest ->
                            let _, _, fieldType = List.item index case.Fields

                            let lowered =
                                if isNull item then
                                    this.Type(at, fieldType)
                                    |> Checked.bind (fun ft -> builder.PatternWildcard(at, ft))
                                    |> Checked.map (fun w -> w, env)
                                else
                                    this.Pattern(env, item)

                            lowered |> Checked.bind (fun (lowered, env') -> lower env' (index + 1) rest (lowered :: done'))

                    check {
                        let! t = this.Type(at, recursive.MatchedType)
                        let! arguments, env' = lower env 0 subpatterns []
                        let! p = builder.PatternConstructor(at, t, fq, arguments)
                        return p, env'
                    })
            | None -> unsupported "pattern" at $"The recursive pattern on '{recursive.MatchedType.ToDisplayString()}' is not a case of a custom type."

        | _ -> unsupported "pattern" at "This kind of pattern is not in the Morphir subset."

    // ----- values -----

    member private this.Literal(at: Morphir.Compilation.Location, value: obj, t: ITypeSymbol) : Checked<'Value> =
        check {
            let! literal = this.LiteralOf(at, value, t)
            let! inferred = this.Type(at, t)
            return! builder.Literal(at, inferred, literal)
        }

    member private this.Reference(at: Morphir.Compilation.Location, fq: FQName, parameters: ITypeSymbol list, result: ITypeSymbol) : Checked<'Value> =
        check {
            let! inferred = this.Function(at, parameters, result)
            return! builder.Reference(at, inferred, fq)
        }

    /// <summary>Applies a function value to arguments one at a time, with the type that remains after each.</summary>
    member private this.Apply
        (at: Morphir.Compilation.Location, fn: 'Value, parameters: ITypeSymbol list, result: ITypeSymbol, arguments: 'Value list)
        : Checked<'Value> =
        let rec go (current: 'Value) (remaining: ITypeSymbol list) (arguments: 'Value list) =
            match arguments, remaining with
            | [], _ -> Checked.pass current
            | argument :: rest, _ :: tail ->
                this.Function(at, tail, result)
                |> Checked.bind (fun inferred -> builder.Apply(at, inferred, current, argument))
                |> Checked.bind (fun applied -> go applied tail rest)
            | _ -> unsupported "arity" at "A call has more arguments than its target has parameters."

        go fn parameters arguments

    member private this.FieldValue(at, env: Env, target: IOperation, name: Name, memberName: string, t: ITypeSymbol) : Checked<'Value> =
        match target with
        | :? ILocalReferenceOperation as local when (Map.tryFind local.Local.Name env |> Option.exists (function CaseFields _ -> true | _ -> false)) ->
            match Map.find local.Local.Name env with
            | CaseFields(_, fields, _) ->
                match fields |> List.tryFind (fun (m, _, _) -> m = memberName) with
                | Some(_, variable, fieldType) ->
                    this.Type(at, fieldType)
                    |> Checked.bind (fun inferred -> builder.Variable(at, inferred, variable))
                | None -> unsupportedReference at memberName
            | Plain -> unsupportedReference at memberName
        | _ ->
            check {
                let! subject = this.Value(env, target)
                let! inferred = this.Type(at, t)
                return! builder.Field(at, inferred, subject, name)
            }

    member this.Value(env: Env, operation: IOperation) : Checked<'Value> =
        let at = locate operation.Syntax

        // A reference to a constant of the package keeps its name; every other constant is its value.
        let isPackageConstant =
            match operation with
            | :? IFieldReferenceOperation as field -> plan.ValueNames.ContainsKey field.Field.OriginalDefinition
            | _ -> false

        let isEnumMember =
            match operation with
            | :? IFieldReferenceOperation as field -> field.Field.ContainingType.TypeKind = TypeKind.Enum
            | _ -> false

        if operation.ConstantValue.HasValue && not (isNull operation.ConstantValue.Value) && not isPackageConstant && not isEnumMember then
            this.Literal(at, operation.ConstantValue.Value, operation.Type)
        else
            match operation with
            | :? IParameterReferenceOperation as parameter -> this.Variable(env, at, parameter.Parameter.Name, parameter.Parameter.Type)
            | :? ILocalReferenceOperation as local -> this.Variable(env, at, local.Local.Name, local.Local.Type)

            | :? IFieldReferenceOperation as field when isEnumMember ->
                match plan.ConstructorNames.TryGetValue field.Field.OriginalDefinition with
                | true, (fq, _) ->
                    this.Type(at, field.Type)
                    |> Checked.bind (fun inferred -> builder.Constructor(at, inferred, fq))
                | _ -> unsupportedReference at (field.Field.ToDisplayString())

            | :? IFieldReferenceOperation as field when isPackageConstant ->
                this.Reference(at, plan.ValueNames[field.Field.OriginalDefinition], [], field.Type)

            | :? IPropertyReferenceOperation as property when not (isNull property.Instance) ->
                let owner = property.Property.ContainingType.OriginalDefinition

                let isPackageRecord =
                    match plan.TypeNames.TryGetValue owner with
                    | true, (_, { Kind = RecordAlias _ }) -> true
                    | _ -> plan.ConstructorNames.ContainsKey owner

                if isPackageRecord then
                    this.FieldValue(at, env, property.Instance, nameOf property.Property.Name, property.Property.Name, property.Type)
                else
                    unsupportedReference at (property.Property.ToDisplayString())

            | :? IConversionOperation as conversion -> this.Conversion(env, at, conversion)
            | :? IBinaryOperation as binary -> this.Binary(env, at, binary)
            | :? IUnaryOperation as unary -> this.Unary(env, at, unary)
            | :? IInvocationOperation as invocation -> this.Invocation(env, at, invocation)
            | :? IObjectCreationOperation as creation -> this.Creation(env, at, creation)
            | :? ITupleOperation as tuple ->
                check {
                    let! elements = tuple.Elements |> List.ofSeq |> Checked.traverse (fun e -> this.Value(env, e))
                    let! inferred = this.Type(at, tuple.Type)
                    return! builder.Tuple(at, inferred, elements)
                }
            | :? IConditionalOperation as conditional when not (isNull conditional.WhenFalse) && not (isNull conditional.Type) ->
                check {
                    let! condition = this.Value(env, conditional.Condition)
                    and! whenTrue = this.Value(env, conditional.WhenTrue)
                    and! whenFalse = this.Value(env, conditional.WhenFalse)
                    let! inferred = this.Type(at, conditional.Type)
                    return! builder.IfThenElse(at, inferred, condition, whenTrue, whenFalse)
                }
            | :? ISwitchExpressionOperation as switch -> this.Switch(env, at, switch)
            | :? IWithOperation as with' ->
                let assignments =
                    with'.Initializer.Initializers
                    |> Seq.choose (function
                        | :? ISimpleAssignmentOperation as a ->
                            match a.Target with
                            | :? IMemberReferenceOperation as m -> Some(nameOf m.Member.Name, a.Value)
                            | _ -> None
                        | _ -> None)
                    |> List.ofSeq

                check {
                    let! target = this.Value(env, with'.Operand)

                    let! fields =
                        assignments
                        |> Checked.traverse (fun (name, value) ->
                            this.Value(env, value) |> Checked.map (fun lowered -> name, lowered))

                    let! inferred = this.Type(at, with'.Type)
                    return! builder.UpdateRecord(at, inferred, target, fields)
                }
            | :? IAnonymousFunctionOperation as lambda -> this.Lambda(env, at, lambda)
            | :? IDelegateCreationOperation as creation ->
                match creation.Target with
                | :? IAnonymousFunctionOperation as lambda -> this.Lambda(env, at, lambda)
                | _ -> unsupported "method_group" at "A method group conversion is not in the Morphir subset. Write a lambda."
            | :? ICollectionExpressionOperation as collection ->
                check {
                    let! items =
                        collection.Elements
                        |> List.ofSeq
                        |> Checked.traverse (fun element ->
                            match element with
                            | :? ISpreadOperation -> unsupported "spread" at "A spread element is not in the Morphir subset."
                            | _ -> this.Value(env, element))

                    let! inferred = this.Type(at, collection.Type)
                    return! builder.List(at, inferred, items)
                }
            | :? IParenthesizedOperation as parenthesized -> this.Value(env, parenthesized.Operand)
            | _ ->
                let construct = describe "expression" operation.Syntax
                unsupported (slugOf operation) at $"The C# {construct} is not in the Morphir subset."

    member private this.Variable(env: Env, at, name: string, t: ITypeSymbol) : Checked<'Value> =
        match Map.tryFind name env with
        | Some(CaseFields(fq, fields, caseType)) ->
            // The whole case value: the constructor applied to the field variables.
            check {
                let! ctorType = this.Function(at, [ for _, _, fieldType in fields -> fieldType ], caseType)
                let! constructor = builder.Constructor(at, ctorType, fq)

                let! arguments =
                    fields
                    |> Checked.traverse (fun (_, variable, fieldType) ->
                        this.Type(at, fieldType)
                        |> Checked.bind (fun inferred -> builder.Variable(at, inferred, variable)))

                return! this.Apply(at, constructor, [ for _, _, fieldType in fields -> fieldType ], caseType, arguments)
            }
        | _ ->
            this.Type(at, t)
            |> Checked.bind (fun inferred -> builder.Variable(at, inferred, nameOf name))

    member private this.Conversion(env: Env, at, conversion: IConversionOperation) : Checked<'Value> =
        let source = conversion.Operand
        let target = conversion.Type
        let sourceType = source.Type

        if conversion.Conversion.IsIdentity || conversion.Conversion.IsReference then
            this.Value(env, source)
        elif
            conversion.Conversion.IsNumeric
            && SdkMapping.operandOf sourceType = Operand.Int
            && SdkMapping.operandOf target = Operand.Float
        then
            check {
                let! operand = this.Value(env, source)
                let! fn = this.Reference(at, SdkMapping.sdk "Basics" "toFloat", [ sourceType ], target)
                return! this.Apply(at, fn, [ sourceType ], target, [ operand ])
            }
        elif conversion.Conversion.IsNullable || conversion.Conversion.IsUserDefined || conversion.Conversion.IsNumeric then
            unsupported "conversion" at $"The conversion from '{sourceType.ToDisplayString()}' to '{target.ToDisplayString()}' is not in the Morphir subset."
        else
            this.Value(env, source)

    member private this.Binary(env: Env, at, binary: IBinaryOperation) : Checked<'Value> =
        let kind = binary.OperatorKind
        let left = binary.LeftOperand
        let right = binary.RightOperand

        match kind with
        | BinaryOperatorKind.ConditionalAnd ->
            check {
                let! condition = this.Value(env, left)
                and! whenTrue = this.Value(env, right)
                let! t = this.Type(at, binary.Type)
                let! falseValue = builder.Literal(at, t, LiteralValue.Bool false)
                return! builder.IfThenElse(at, t, condition, whenTrue, falseValue)
            }
        | BinaryOperatorKind.ConditionalOr ->
            check {
                let! condition = this.Value(env, left)
                and! whenFalse = this.Value(env, right)
                let! t = this.Type(at, binary.Type)
                let! trueValue = builder.Literal(at, t, LiteralValue.Bool true)
                return! builder.IfThenElse(at, t, condition, trueValue, whenFalse)
            }
        | _ ->
            let operand = SdkMapping.operandOf left.Type

            match SdkMapping.tryBinary kind operand with
            | None ->
                unsupportedReference at $"{left.Type.ToDisplayString()} {SdkMapping.operatorText kind} {right.Type.ToDisplayString()}"
            | Some fq ->
                let arguments =
                    // C# `a % b` is Morphir `remainderBy b a`.
                    if kind = BinaryOperatorKind.Remainder then [ right; left ] else [ left; right ]

                let parameters = [ for a in arguments -> a.Type ]

                check {
                    let! lowered = arguments |> Checked.traverse (fun a -> this.Value(env, a))
                    let! fn = this.Reference(at, fq, parameters, binary.Type)
                    return! this.Apply(at, fn, parameters, binary.Type, lowered)
                }

    member private this.Unary(env: Env, at, unary: IUnaryOperation) : Checked<'Value> =
        match SdkMapping.tryUnary unary.OperatorKind (SdkMapping.operandOf unary.Operand.Type) with
        | None -> unsupportedReference at $"unary operator on {unary.Operand.Type.ToDisplayString()}"
        | Some fq ->
            check {
                let! operand = this.Value(env, unary.Operand)
                let! fn = this.Reference(at, fq, [ unary.Operand.Type ], unary.Type)
                return! this.Apply(at, fn, [ unary.Operand.Type ], unary.Type, [ operand ])
            }

    member private this.Invocation(env: Env, at, invocation: IInvocationOperation) : Checked<'Value> =
        let target = invocation.TargetMethod
        // The operation gives the arguments in the order of the source. A named argument can come before an earlier
        // parameter, so the arguments are put in the order of the parameters.
        let ordinal (a: IArgumentOperation) =
            match a.Parameter with
            | null -> System.Int32.MaxValue
            | parameter -> parameter.Ordinal

        let arguments = [ for a in invocation.Arguments |> Seq.sortBy ordinal -> a.Value ]
        // The types of this call: a generic method has its type arguments here, so `Id(3)` has the type Int, not T.
        let parameters = [ for p in target.Parameters -> p.Type ]
        let result = target.ReturnType

        let lowerArguments () =
            arguments |> Checked.traverse (fun a -> this.Value(env, a))

        if target.MethodKind = MethodKind.DelegateInvoke then
            check {
                let! fn = this.Value(env, invocation.Instance)
                let! lowered = lowerArguments ()
                return! this.Apply(at, fn, [ for p in target.Parameters -> p.Type ], target.ReturnType, lowered)
            }
        elif plan.ValueNames.ContainsKey target.OriginalDefinition then
            check {
                let! fn = this.Reference(at, plan.ValueNames[target.OriginalDefinition], parameters, result)
                let! lowered = lowerArguments ()
                return! this.Apply(at, fn, parameters, result, lowered)
            }
        else
            match SdkMapping.tryMethod target with
            | Some(fq, _) ->
                check {
                    let! fn = this.Reference(at, fq, parameters, result)
                    let! lowered = lowerArguments ()
                    return! this.Apply(at, fn, parameters, result, lowered)
                }
            | None ->
                match this.TypeParameterCall invocation with
                | Some(TypeParameterEquality(typeParameter, a, b)) ->
                    check {
                        let! fn = this.Reference(at, SdkMapping.sdk "Basics" "equal", [ typeParameter; typeParameter ], result)
                        let! left = this.Value(env, a)
                        let! right = this.Value(env, b)
                        return! this.Apply(at, fn, [ typeParameter; typeParameter ], result, [ left; right ])
                    }
                | Some(TypeParameterOrder(typeParameter, a, b)) -> this.OrderAsInt(env, at, typeParameter, a, b, result)
                | None -> unsupportedReference at (target.ToDisplayString())

    /// <summary>
    /// The equality or the order of two values of a type parameter of the method (EQL-2): <c>a.Equals(b)</c> and
    /// <c>EqualityComparer&lt;T&gt;.Default.Equals(a, b)</c> are equality; <c>a.CompareTo(b)</c> and
    /// <c>Comparer&lt;T&gt;.Default.Compare(a, b)</c> are order. A call on values of another type is not one of these:
    /// <c>double.Equals</c>, for example, makes NaN equal to NaN.
    /// </summary>
    member private this.TypeParameterCall(invocation: IInvocationOperation) : TypeParameterCall option =
        let target = invocation.TargetMethod

        let rec unwrap (operation: IOperation) =
            match operation with
            | :? IConversionOperation as conversion when conversion.IsImplicit -> unwrap conversion.Operand
            | _ -> operation

        let methodParameter (operation: IOperation) =
            match unwrap operation with
            | null -> None
            | operand ->
                match operand.Type with
                | :? ITypeParameterSymbol as parameter when parameter.TypeParameterKind = TypeParameterKind.Method -> Some(parameter :> ITypeSymbol, operand)
                | _ -> None

        let arguments = [ for a in invocation.Arguments -> a.Value ]

        let defaultOf (typeName: string) =
            not (isNull invocation.Instance)
            && target.ContainingType.ContainingNamespace.ToDisplayString() = "System.Collections.Generic"
            && target.ContainingType.MetadataName = typeName

        let pair make (left: IOperation) (right: IOperation) =
            match methodParameter left, methodParameter right with
            | Some(a, leftOperand), Some(b, rightOperand) when SymbolEqualityComparer.Default.Equals(a, b) -> Some(make (a, leftOperand, rightOperand))
            | _ -> None

        match target.Name, arguments with
        | "Equals", [ a; b ] when target.IsStatic |> not && defaultOf "EqualityComparer`1" -> pair TypeParameterEquality a b
        | "Equals", [ other ] when not target.IsStatic && not (isNull invocation.Instance) -> pair TypeParameterEquality invocation.Instance other
        | "Compare", [ a; b ] when not target.IsStatic && defaultOf "Comparer`1" -> pair TypeParameterOrder a b
        | "CompareTo", [ other ] when not target.IsStatic && not (isNull invocation.Instance) -> pair TypeParameterOrder invocation.Instance other
        | _ -> None

    /// <summary>
    /// The C# order of two values as an Int: <c>Basics.compare</c> matched to -1 for <c>LT</c>, 0 for <c>EQ</c> and 1 for
    /// <c>GT</c>. The .NET contract of <c>CompareTo</c> gives only the sign, so these three values are one of its results.
    /// </summary>
    member private this.OrderAsInt(env: Env, at, typeParameter: ITypeSymbol, a: IOperation, b: IOperation, result: ITypeSymbol) : Checked<'Value> =
        match compiled.Compilation.GetTypeByMetadataName "Morphir.SDK.CSharp.Order" with
        | null -> unsupportedReference at "Morphir.SDK.CSharp.Order"
        | orderSymbol ->
            check {
                let! fn = this.Reference(at, SdkMapping.sdk "Basics" "compare", [ typeParameter; typeParameter ], orderSymbol)
                let! left = this.Value(env, a)
                let! right = this.Value(env, b)
                let! order = this.Apply(at, fn, [ typeParameter; typeParameter ], orderSymbol, [ left; right ])
                let! orderType = this.Type(at, orderSymbol)
                let! intType = this.Type(at, result)

                let! arms =
                    [ "LT", -1; "EQ", 0; "GT", 1 ]
                    |> Checked.traverse (fun (case, value) ->
                        check {
                            let! pattern = builder.PatternConstructor(at, orderType, SdkMapping.sdk "Basics" case, [])
                            let! literal = builder.Literal(at, intType, LiteralValue.Integer(BigInteger value))
                            return pattern, literal
                        })

                return! builder.PatternMatch(at, intType, order, arms)
            }

    /// <summary>
    /// The value of each field of a record creation, in the order of the fields. A constructor argument gives the field
    /// with the name of its parameter, and an object initializer then sets a field again. A field with no value is refused.
    /// </summary>
    member private this.FieldValues(at, creation: IObjectCreationOperation, typeName: string, fields: string list) : Checked<(string * IOperation) list> =
        let given = Dictionary<string, IOperation>(System.StringComparer.Ordinal)

        for argument in creation.Arguments do
            match argument.Parameter with
            | null -> ()
            | parameter -> given[parameter.Name] <- argument.Value

        let initializers =
            match creation.Initializer with
            | null -> []
            | initializer -> List.ofSeq initializer.Initializers

        let unknown =
            initializers
            |> List.tryPick (fun operation ->
                match operation with
                | :? ISimpleAssignmentOperation as assignment ->
                    match assignment.Target with
                    | :? IMemberReferenceOperation as reference ->
                        given[reference.Member.Name] <- assignment.Value
                        None
                    | _ -> Some operation
                | _ -> Some operation)

        match unknown with
        | Some operation -> unsupported "record_initializer" (locate operation.Syntax) $"The initializer of '{typeName}' sets something that is not a field."
        | None ->
            match fields |> List.tryFind (fun field -> not (given.ContainsKey field)) with
            | Some missing ->
                unsupported
                    "record_initializer"
                    at
                    $"The record '{typeName}' is created without the field '{missing}'. A Morphir record value gives every field."
            | None -> Checked.pass [ for field in fields -> field, given[field] ]

    member private this.Creation(env: Env, at, creation: IObjectCreationOperation) : Checked<'Value> =
        let created = creation.Type

        match this.CaseOf created with
        | Some(fq, case) ->
            let parameters = [ for _, _, fieldType in case.Fields -> fieldType ]

            check {
                let! values = this.FieldValues(at, creation, created.Name, [ for _, member', _ in case.Fields -> member' ])
                let! constructorType = this.Function(at, parameters, created)
                let! constructor = builder.Constructor(at, constructorType, fq)
                let! lowered = values |> Checked.traverse (fun (_, value) -> this.Value(env, value))
                return! this.Apply(at, constructor, parameters, created, lowered)
            }
        | None ->
            match created with
            | :? INamedTypeSymbol as named when plan.TypeNames.ContainsKey named.OriginalDefinition ->
                match snd plan.TypeNames[named.OriginalDefinition] with
                | { Kind = RecordAlias(fields, _) } ->
                    check {
                        let! values = this.FieldValues(at, creation, created.Name, [ for _, member', _ in fields -> member' ])

                        let! lowered =
                            values
                            |> Checked.traverse (fun (member', value) ->
                                this.Value(env, value) |> Checked.map (fun v -> nameOf member', v))

                        let! inferred = this.Type(at, created)
                        return! builder.Record(at, inferred, lowered)
                    }
                | _ -> unsupportedReference at (created.ToDisplayString())
            | _ -> unsupportedReference at (created.ToDisplayString())

    member private this.Switch(env: Env, at, switch: ISwitchExpressionOperation) : Checked<'Value> =
        check {
            let! subject = this.Value(env, switch.Value)

            let! arms =
                switch.Arms
                |> List.ofSeq
                |> Checked.traverse (fun arm ->
                    if not (isNull arm.Guard) then
                        unsupported "guard" (locate arm.Syntax) "A 'when' guard on a switch arm is not in the Morphir subset."
                    else
                        check {
                            let! pattern, armEnv = this.Pattern(env, arm.Pattern)
                            let! value = this.Value(armEnv, arm.Value)
                            return pattern, value
                        })

            let! inferred = this.Type(at, switch.Type)
            return! builder.PatternMatch(at, inferred, subject, arms)
        }

    member private this.Lambda(env: Env, at, lambda: IAnonymousFunctionOperation) : Checked<'Value> =
        let symbol = lambda.Symbol
        let parameters = List.ofSeq symbol.Parameters
        let env' = parameters |> List.fold (fun e p -> Map.add p.Name Plain e) env

        let rec wrap (remaining: IParameterSymbol list) (body: 'Value) =
            match remaining with
            | [] -> Checked.pass body
            | parameter :: rest ->
                // The lambda of the first parameter is the outermost.
                check {
                    let! inner = wrap rest body
                    let! parameterType = this.Type(at, parameter.Type)
                    let! wildcard = builder.PatternWildcard(at, parameterType)
                    let! pattern = builder.PatternAs(at, parameterType, wildcard, nameOf parameter.Name)

                    let types = [ for p in rest -> p.Type ]
                    let! inferred = this.Function(at, parameter.Type :: types, symbol.ReturnType)
                    return! builder.Lambda(at, inferred, pattern, inner)
                }

        let body =
            match lambda.Body with
            | null -> unsupported "lambda" at "The lambda has no body."
            | block -> this.Statements(env', symbol.ReturnType, List.ofSeq block.Operations, at)

        body |> Checked.bind (wrap parameters)

    /// <summary>
    /// Lowers a list of statements that ends in a return: declarations become lets, deconstructions become destructures,
    /// ifs become conditionals and switch statements become pattern matches. A list that ends before a return is refused
    /// at <c>endAt</c>, the end of the block that holds it.
    /// </summary>
    member this.Statements(env: Env, result: ITypeSymbol, statements: IOperation list, endAt: Morphir.Compilation.Location) : Checked<'Value> =
        match statements with
        | [] -> unsupported "missing_return" endAt "A method body must end with a return."
        | first :: rest ->
            let at = locate first.Syntax

            match first with
            | :? IReturnOperation as return' when not (isNull return'.ReturnedValue) -> this.Value(env, return'.ReturnedValue)
            | :? IBlockOperation as block -> this.Statements(env, result, List.ofSeq block.Operations @ rest, endAt)
            | :? IVariableDeclarationGroupOperation as group ->
                let declarators = [ for declaration in group.Declarations do yield! declaration.Declarators ]

                let rec bind (env: Env) (items: IVariableDeclaratorOperation list) : Checked<'Value> =
                    match items with
                    | [] -> this.Statements(env, result, rest, endAt)
                    | declarator :: tail ->
                        let local = declarator.Symbol
                        let dat = locate declarator.Syntax

                        match declarator.Initializer with
                        | null -> unsupported "uninitialized_local" dat "A local variable needs an initial value."
                        | initializer ->
                            check {
                                let! value = this.Value(env, initializer.Value)
                                let! localType = this.Type(dat, local.Type)
                                let! definition = builder.ValueDefinition(dat, [], localType, value)
                                let! inValue = bind (Map.add local.Name Plain env) tail
                                let! inferred = this.Type(dat, result)
                                return! builder.LetDefinition(dat, inferred, nameOf local.Name, definition, inValue)
                            }

                bind env declarators
            | :? IExpressionStatementOperation as statement when (statement.Operation :? IDeconstructionAssignmentOperation) ->
                this.Deconstruction(env, at, result, statement.Operation :?> IDeconstructionAssignmentOperation, rest, endAt)
            | :? IConditionalOperation as conditional ->
                let branch (operation: IOperation) = this.Statements(env, result, [ operation ], locate operation.Syntax)

                check {
                    let! condition = this.Value(env, conditional.Condition)
                    let! whenTrue = branch conditional.WhenTrue

                    let! whenFalse =
                        match conditional.WhenFalse with
                        | null -> this.Statements(env, result, rest, endAt)
                        | other -> branch other

                    let! inferred = this.Type(at, result)
                    return! builder.IfThenElse(at, inferred, condition, whenTrue, whenFalse)
                }
            | :? ISwitchOperation as switch -> this.SwitchStatement(env, at, result, switch, rest, endAt)
            | _ ->
                let construct = describe "statement" first.Syntax
                unsupported (slugOf first) at $"The C# {construct} is not in the Morphir subset."

    /// <summary>
    /// A deconstruction declaration such as <c>var (a, b) = t;</c>: a destructure of the tuple with a tuple pattern, and the
    /// statements after it as the body. Only a tuple can be deconstructed: the <c>Deconstruct</c> method of a record has
    /// no Morphir pattern.
    /// </summary>
    member private this.Deconstruction
        (env: Env, at, result: ITypeSymbol, deconstruction: IDeconstructionAssignmentOperation, rest: IOperation list, endAt)
        : Checked<'Value> =
        let rec target (env: Env) (operation: IOperation) (valueType: ITypeSymbol) : Checked<'Pattern * Env> =
            let tat = locate operation.Syntax

            match operation with
            | :? IDeclarationExpressionOperation as declaration -> target env declaration.Expression valueType
            | :? IDiscardOperation ->
                this.Type(tat, valueType)
                |> Checked.bind (fun t -> builder.PatternWildcard(tat, t))
                |> Checked.map (fun p -> p, env)
            | :? ILocalReferenceOperation as local when local.IsDeclaration ->
                if not (SymbolEqualityComparer.Default.Equals(local.Local.Type, valueType)) then
                    unsupported
                        "conversion"
                        tat
                        $"The conversion from '{valueType.ToDisplayString()}' to '{local.Local.Type.ToDisplayString()}' is not in the Morphir subset."
                else
                    check {
                        let! t = this.Type(tat, valueType)
                        let! w = builder.PatternWildcard(tat, t)
                        let! p = builder.PatternAs(tat, t, w, nameOf local.Local.Name)
                        return p, Map.add local.Local.Name Plain env
                    }
            | :? ITupleOperation as tuple when valueType.IsTupleType ->
                let elementTypes = [ for element in (valueType :?> INamedTypeSymbol).TupleElements -> element.Type ]

                let rec walk (env: Env) (items: (IOperation * ITypeSymbol) list) (done': 'Pattern list) =
                    match items with
                    | [] -> Checked.pass (List.rev done', env)
                    | (item, itemType) :: tail -> target env item itemType |> Checked.bind (fun (p, env') -> walk env' tail (p :: done'))

                check {
                    let! t = this.Type(tat, valueType)
                    let! elements, env' = walk env (List.zip (List.ofSeq tuple.Elements) elementTypes) []
                    let! p = builder.PatternTuple(tat, t, elements)
                    return p, env'
                }
            | _ ->
                unsupported
                    "deconstruction"
                    tat
                    $"The deconstruction of '{valueType.ToDisplayString()}' is not in the Morphir subset. Only a tuple can be deconstructed."

        let value = deconstruction.Value

        if isNull value.Type || not value.Type.IsTupleType then
            let shown = if isNull value.Type then "this value" else value.Type.ToDisplayString()

            unsupported
                "deconstruction"
                at
                $"The deconstruction of '{shown}' is not in the Morphir subset. Only a tuple can be deconstructed: read the fields of a record by name."
        else
            check {
                let! subject = this.Value(env, value)
                let! pattern, env' = target env deconstruction.Target value.Type
                let! inValue = this.Statements(env', result, rest, endAt)
                let! inferred = this.Type(at, result)
                return! builder.Destructure(at, inferred, pattern, subject, inValue)
            }

    /// <summary>
    /// A <c>switch</c> statement whose sections each end with a return: a pattern match. The arms keep the order of the
    /// cases, and the <c>default</c> section is the last arm, because C# tests it last. With no <c>default</c>, the
    /// statements after the switch are the last arm.
    /// </summary>
    member private this.SwitchStatement
        (env: Env, at, result: ITypeSymbol, switch: ISwitchOperation, rest: IOperation list, endAt)
        : Checked<'Value> =
        let inputType = switch.Value.Type

        let wildcard (wat: Morphir.Compilation.Location) =
            this.Type(wat, inputType) |> Checked.bind (fun t -> builder.PatternWildcard(wat, t))

        let returns (section: ISwitchCaseOperation) =
            match Seq.tryLast section.Body with
            | Some(:? IBranchOperation) -> false
            | _ -> true

        let arm (section: ISwitchCaseOperation) (clause: ICaseClauseOperation) : Checked<'Pattern * 'Value> =
            let sat = locate section.Syntax
            let cat = locate clause.Syntax

            let body (armEnv: Env) =
                if returns section then
                    this.Statements(armEnv, result, List.ofSeq section.Body, sat)
                else
                    unsupported "missing_return" sat "Each section of a 'switch' statement must end with a return."

            match clause with
            | :? IDefaultCaseClauseOperation ->
                check {
                    let! p = wildcard cat
                    let! value = body env
                    return p, value
                }
            | :? IPatternCaseClauseOperation as patternClause when not (isNull patternClause.Guard) ->
                unsupported "guard" cat "A 'when' guard on a switch case is not in the Morphir subset."
            | :? IPatternCaseClauseOperation as patternClause ->
                check {
                    let! p, armEnv = this.Pattern(env, patternClause.Pattern)
                    let! value = body armEnv
                    return p, value
                }
            | :? ISingleValueCaseClauseOperation as single ->
                check {
                    let! p = this.ConstantPattern(cat, single.Value, inputType)
                    let! value = body env
                    return p, value
                }
            | _ -> unsupported "pattern" cat "This kind of switch case is not in the Morphir subset."

        let clauses = [ for section in switch.Cases do for clause in section.Clauses -> section, clause ]
        let isDefault (_, clause: ICaseClauseOperation) = clause :? IDefaultCaseClauseOperation

        let last: Checked<('Pattern * 'Value) list> =
            match clauses |> List.filter isDefault with
            | [] when List.isEmpty rest -> unsupported "missing_return" endAt "A method body must end with a return."
            | [] ->
                check {
                    let! p = wildcard at
                    let! value = this.Statements(env, result, rest, endAt)
                    return [ p, value ]
                }
            | defaults -> defaults |> Checked.traverse (fun (section, clause) -> arm section clause)

        check {
            let! subject = this.Value(env, switch.Value)
            and! cases = clauses |> List.filter (isDefault >> not) |> Checked.traverse (fun (section, clause) -> arm section clause)
            and! last = last
            let! inferred = this.Type(at, result)
            return! builder.PatternMatch(at, inferred, subject, cases @ last)
        }

    // ----- definitions -----

    member private this.Access(access: Access) = access

    /// <summary>The definition of a static method or a constant.</summary>
    member this.ValueDefinition(planned: PlannedValue) : Checked<'ValueDefinition> =
        let at = planned.Location

        match planned.Symbol, planned.Syntax with
        | (:? IMethodSymbol as method'), (:? MethodDeclarationSyntax as declaration) ->
            let model = compiled.Compilation.GetSemanticModel(declaration.SyntaxTree, false)

            let parameters = List.ofSeq method'.Parameters
            let env = parameters |> List.fold (fun e p -> Map.add p.Name Plain e) Map.empty

            let body: Checked<'Value> =
                match declaration.ExpressionBody with
                | null ->
                    match model.GetOperation(declaration) with
                    | :? IMethodBodyOperation as methodBody when not (isNull methodBody.BlockBody) ->
                        let endAt =
                            CSharpCompiler.locationOfToken compiled declaration.Body.CloseBraceToken
                            |> Option.defaultValue at

                        this.Statements(env, method'.ReturnType, List.ofSeq methodBody.BlockBody.Operations, endAt)
                    | _ -> unsupported "method_body" at "The method has no body that Morphir can read."
                | _ ->
                    match model.GetOperation(declaration) with
                    | :? IMethodBodyOperation as methodBody when not (isNull methodBody.ExpressionBody) ->
                        this.Statements(env, method'.ReturnType, List.ofSeq methodBody.ExpressionBody.Operations, at)
                    | _ -> unsupported "method_body" at "The method has no body that Morphir can read."

            // Each type is located at its own syntax in the declaration, not at the whole method.
            let typeLocation (syntax: SyntaxNode) = if isNull syntax then at else locate syntax

            let parameterLocations =
                [ for parameter in declaration.ParameterList.Parameters -> typeLocation parameter.Type ]

            let returnAt = typeLocation declaration.ReturnType

            check {
                let! inputs =
                    List.zip parameters parameterLocations
                    |> Checked.traverse (fun (p, parameterAt) ->
                        this.Type(parameterAt, p.Type) |> Checked.map (fun t -> nameOf p.Name, t))

                let! output = this.Type(returnAt, method'.ReturnType)
                let! lowered = body
                return! builder.ValueDefinition(at, inputs, output, lowered)
            }
        | (:? IFieldSymbol as field), (:? VariableDeclaratorSyntax as declarator) ->
            let model = compiled.Compilation.GetSemanticModel(declarator.SyntaxTree, false)

            match declarator.Initializer with
            | null -> unsupported "constant" at "The constant has no value that Morphir can read."
            | initializer ->
                match model.GetOperation(initializer.Value) with
                | null -> unsupported "constant" at "The constant has no value that Morphir can read."
                | operation ->
                    let typeAt =
                        match declarator.Parent with
                        | :? VariableDeclarationSyntax as declaration when not (isNull declaration.Type) -> locate declaration.Type
                        | _ -> at

                    check {
                        let! value = this.Value(Map.empty, operation)
                        let! t = this.Type(typeAt, field.Type)
                        return! builder.ValueDefinition(at, [], t, value)
                    }
        | _ -> unsupported "member" at "This member is not a static method or a constant."

    /// <summary>The definition of a planned type.</summary>
    member this.TypeDefinition(planned: PlannedType) : Checked<'TypeDefinition> =
        let at = planned.Location

        let fieldsOf (fields: (Name * string * ITypeSymbol) list) (locations: Morphir.Compilation.Location list) =
            List.zip fields locations
            |> Checked.traverse (fun ((name, _, t), fieldAt) ->
                this.Type(fieldAt, t) |> Checked.map (fun lowered -> name, lowered))

        match planned.Kind with
        | RecordAlias(fields, locations) ->
            fieldsOf fields locations
            |> Checked.bind (fun lowered -> builder.TypeRecord(at, lowered))
            |> Checked.bind (fun body -> builder.TypeAlias(at, planned.Parameters, body))
        | CustomType cases ->
            cases
            |> Checked.traverse (fun case ->
                fieldsOf case.Fields case.FieldLocations
                |> Checked.map (fun arguments -> ({ Name = case.Name; Arguments = arguments }: ConstructorPlan<'Type>)))
            |> Checked.bind (fun constructors ->
                builder.CustomType(at, planned.Parameters, Access.Public, constructors))

    member this.Module(planned: PlannedModule) : Checked<'Module> =
        check {
            let! types =
                planned.Types
                |> Checked.traverse (fun t ->
                    this.TypeDefinition t
                    |> Checked.map (fun definition ->
                        ({
                            Name = t.Name
                            Access = t.Access
                            Documentation = None
                            Definition = definition
                            Location = t.Location
                        }: Member<'TypeDefinition>)))

            let! values =
                planned.Values
                |> Checked.traverse (fun v ->
                    this.ValueDefinition v
                    |> Checked.map (fun definition ->
                        ({
                            Name = v.Name
                            Access = v.Access
                            Documentation = None
                            Definition = definition
                            Location = v.Location
                        }: Member<'ValueDefinition>)))

            return!
                builder.Module(
                    {
                        Path = planned.Path
                        Access = planned.Access
                        Documentation = None
                        Location = planned.Location
                    },
                    types,
                    values
                )
        }

    member this.Package() : Checked<'Package> =
        plan.Modules
        |> Checked.traverse this.Module
        |> Checked.bind (fun modules -> builder.Package({ Package = plan.Package; Documentation = None }, modules))

/// <summary>The lowering of a planned package as a builder program, for any registered IR family.</summary>
[<Sealed>]
type internal PackageLowering(plan: Plan) =
    interface IBuilderProgram with
        member _.Run(builder) = Lowerer(builder, plan).Package()
