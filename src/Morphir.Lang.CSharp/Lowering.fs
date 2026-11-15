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

    let rune (c: char) = Rune c

    let nameOf (name: string) = Name.fromString name

    /// <summary>Variables bound for the fields of a case record that a pattern binds to a local.</summary>
    let caseVariable (local: string) (field: string) =
        Name(Name.toList (nameOf local) @ Name.toList (nameOf field))

    // ----- types -----

    member this.Type(at: Morphir.Compilation.Location, t: ITypeSymbol) : Checked<'Type> =
        match t with
        | null -> builder.TypeUnit at
        | :? ITypeParameterSymbol as parameter -> builder.TypeVariable(at, nameOf parameter.Name)
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

                check {
                    let! t = this.Type(at, matched)

                    let! wildcards =
                        case.Fields
                        |> Checked.traverse (fun (name, member', fieldType) ->
                            check {
                                let! ft = this.Type(at, fieldType)

                                if isNull symbol then
                                    let! w = builder.PatternWildcard(at, ft)
                                    return w
                                else
                                    let! w = builder.PatternWildcard(at, ft)
                                    let! v = builder.PatternAs(at, ft, w, caseVariable local member')
                                    return v
                            })

                    let! ctor = builder.PatternConstructor(at, t, fq, wildcards)

                    let fields = [ for name, member', fieldType in case.Fields -> member', caseVariable local member', fieldType ]

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
            if constant.Value.ConstantValue.HasValue && not (isNull constant.Value.ConstantValue.Value) then
                check {
                    let! t = this.Type(at, pattern.InputType)
                    let! literal = this.LiteralOf(at, constant.Value.ConstantValue.Value, pattern.InputType)
                    let! p = builder.PatternLiteral(at, t, literal)
                    return p, env
                }
            elif constant.Value.Type <> null && constant.Value.Type.TypeKind = TypeKind.Enum then
                // An enum constant: a constructor pattern with no arguments.
                match constant.Value with
                | :? IFieldReferenceOperation as field ->
                    match plan.ConstructorNames.TryGetValue field.Field.OriginalDefinition with
                    | true, (fq, _) ->
                        check {
                            let! t = this.Type(at, pattern.InputType)
                            let! p = builder.PatternConstructor(at, t, fq, [])
                            return p, env
                        }
                    | _ -> unsupportedReference at (field.Field.ToDisplayString())
                | _ -> unsupported "pattern" at "The constant pattern is not a literal or an enum member."
            else
                unsupported "pattern" at "The constant pattern is not a literal or an enum member."

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
            | _ -> unsupported (slugOf operation) at $"The C# construct '{operation.Syntax.Kind()}' is not in the Morphir subset."

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
        let arguments = [ for a in invocation.Arguments -> a.Value ]
        let parameters = [ for p in target.OriginalDefinition.Parameters -> p.Type ]
        let result = target.OriginalDefinition.ReturnType

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
            | None -> unsupportedReference at (target.ToDisplayString())

    member private this.Creation(env: Env, at, creation: IObjectCreationOperation) : Checked<'Value> =
        let created = creation.Type
        let arguments = [ for a in creation.Arguments -> a.Value ]

        match this.CaseOf created with
        | Some(fq, case) ->
            let parameters = [ for _, _, fieldType in case.Fields -> fieldType ]

            check {
                let! constructorType = this.Function(at, parameters, created)
                let! constructor = builder.Constructor(at, constructorType, fq)
                let! lowered = arguments |> Checked.traverse (fun a -> this.Value(env, a))
                return! this.Apply(at, constructor, parameters, created, lowered)
            }
        | None ->
            match created with
            | :? INamedTypeSymbol as named when plan.TypeNames.ContainsKey named.OriginalDefinition ->
                match snd plan.TypeNames[named.OriginalDefinition] with
                | { Kind = RecordAlias(fields, _) } ->
                    let initializers =
                        match creation.Initializer with
                        | null -> []
                        | initializer ->
                            initializer.Initializers
                            |> Seq.choose (function
                                | :? ISimpleAssignmentOperation as a ->
                                    match a.Target with
                                    | :? IMemberReferenceOperation as m -> Some(m.Member.Name, a.Value)
                                    | _ -> None
                                | _ -> None)
                            |> List.ofSeq

                    let positional = List.zip (List.truncate arguments.Length [ for _, member', _ in fields -> member' ]) arguments

                    let ordered =
                        [ for _, member', _ in fields do
                              match positional |> List.tryFind (fun (m, _) -> m = member') with
                              | Some(_, value) -> yield member', value
                              | None ->
                                  match initializers |> List.tryFind (fun (m, _) -> m = member') with
                                  | Some(_, value) -> yield member', value
                                  | None -> () ]

                    check {
                        let! lowered =
                            ordered
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
            | block -> this.Statements(env', symbol.ReturnType, List.ofSeq block.Operations)

        body |> Checked.bind (wrap parameters)

    /// <summary>Lowers a list of statements that ends in a return: declarations become lets, ifs become conditionals.</summary>
    member this.Statements(env: Env, result: ITypeSymbol, statements: IOperation list) : Checked<'Value> =
        match statements with
        | [] -> unsupported "missing_return" fallback "A method body must end with a return."
        | first :: rest ->
            let at = locate first.Syntax

            match first with
            | :? IReturnOperation as return' when not (isNull return'.ReturnedValue) -> this.Value(env, return'.ReturnedValue)
            | :? IBlockOperation as block -> this.Statements(env, result, List.ofSeq block.Operations @ rest)
            | :? IVariableDeclarationGroupOperation as group ->
                let declarators = [ for declaration in group.Declarations do yield! declaration.Declarators ]

                let rec bind (env: Env) (items: IVariableDeclaratorOperation list) : Checked<'Value> =
                    match items with
                    | [] -> this.Statements(env, result, rest)
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
            | :? IConditionalOperation as conditional ->
                let branch (operation: IOperation) = this.Statements(env, result, [ operation ])

                check {
                    let! condition = this.Value(env, conditional.Condition)
                    let! whenTrue = branch conditional.WhenTrue

                    let! whenFalse =
                        match conditional.WhenFalse with
                        | null -> this.Statements(env, result, rest)
                        | other -> branch other

                    let! inferred = this.Type(at, result)
                    return! builder.IfThenElse(at, inferred, condition, whenTrue, whenFalse)
                }
            | _ -> unsupported (slugOf first) at $"The C# statement '{first.Syntax.Kind()}' is not in the Morphir subset."

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
                        this.Statements(env, method'.ReturnType, List.ofSeq methodBody.BlockBody.Operations)
                    | _ -> unsupported "method_body" at "The method has no body that Morphir can read."
                | _ ->
                    match model.GetOperation(declaration) with
                    | :? IMethodBodyOperation as methodBody when not (isNull methodBody.ExpressionBody) ->
                        this.Statements(env, method'.ReturnType, List.ofSeq methodBody.ExpressionBody.Operations)
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
