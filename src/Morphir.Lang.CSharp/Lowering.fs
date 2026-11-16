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
open Morphir.Lang.CSharp.Analyzers

/// <summary>What a name in scope stands for.</summary>
type internal Binding =
    /// <summary>A parameter or a local that is a Morphir variable.</summary>
    | Plain
    /// <summary>
    /// A local bound by a pattern on a case record: its fields are Morphir variables, and the value itself is the
    /// constructor applied to them.
    /// </summary>
    | CaseFields of constructor: FQName * fields: (string * Name * ITypeSymbol) list * caseType: ITypeSymbol
    /// <summary>
    /// A local bound by a positional or a property pattern on a case of a union: it is the field of the case value, which
    /// the pattern binds to a variable.
    /// </summary>
    | FieldOf of record: Name * recordType: ITypeSymbol * field: Name * fieldType: ITypeSymbol
    /// <summary>
    /// A local whose value is a computation, not a Morphir variable: the new value of a local that a loop changes, or the
    /// value of a declaration that only the next loop reads. The key selects the computation in the table of the lowerer.
    /// </summary>
    | Computed of key: int
    /// <summary>An empty list of the type <c>System.Collections.Generic.List&lt;T&gt;</c> that a later <c>foreach</c> fills.</summary>
    | Builder

/// <summary>A call of the equality or the order of .NET on two values of a type parameter of the method.</summary>
type internal TypeParameterCall =
    /// <summary><c>a.Equals(b)</c> or <c>EqualityComparer&lt;T&gt;.Default.Equals(a, b)</c>: the type parameter and the two operands.</summary>
    | TypeParameterEquality of ITypeSymbol * IOperation * IOperation
    /// <summary><c>a.CompareTo(b)</c> or <c>Comparer&lt;T&gt;.Default.Compare(a, b)</c>: the type parameter and the two operands.</summary>
    | TypeParameterOrder of ITypeSymbol * IOperation * IOperation

/// <summary>The names in scope. C# does not allow a local to hide another, so the name is the key.</summary>
type internal Env = Map<string, Binding>

/// <summary>
/// What a list of statements gives at its end and at a jump. A method body must end with a return. The body of a loop
/// ends with the next state: the state of a fold, or the next call of a recursion; a <c>break</c> goes on with the
/// statements after the loop.
/// </summary>
type internal Flow<'Value> = {
    /// <summary>The value when the end of the statements is reached, or none when the end needs a return.</summary>
    End: (Env -> Checked<'Value>) option
    /// <summary>The value of a <c>continue</c> statement.</summary>
    Continue: (Env -> Checked<'Value>) option
    /// <summary>The value of a <c>break</c> statement.</summary>
    Break: (Env -> Checked<'Value>) option
}

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

    /// <summary>Whether a type is the <c>IUnion</c> interface of C# 15, which reaches the state of a union with no case.</summary>
    let isUnionInterface (t: ITypeSymbol) =
        not (isNull t)
        && t.Name = "IUnion"
        && not (isNull t.ContainingNamespace)
        && t.ContainingNamespace.ToDisplayString() = "System.Runtime.CompilerServices"

    let unionInterfaceRefusal (at: Morphir.Compilation.Location) : Checked<'a> =
        unsupported
            "union_interface"
            at
            "The interface 'IUnion' reaches the state of a union with no case. A Morphir custom type has no such state: match the cases of the union."

    /// <summary>A union of the package, as it is used: a constructed type has the type arguments of its use.</summary>
    let unionOf (t: ITypeSymbol) =
        match t with
        | :? INamedTypeSymbol as named when named.IsUnion && plan.UnionCases.ContainsKey named.OriginalDefinition -> Some named
        | _ -> None

    /// <summary>The constructor of the case of a union that a case type gives, and the case type of this use.</summary>
    let unionCase (unionType: ITypeSymbol) (caseType: ITypeSymbol) : (FQName * ITypeSymbol) option =
        match unionOf unionType with
        | Some union when not (isNull caseType) ->
            let names = plan.UnionCases[union.OriginalDefinition]

            union.UnionCaseTypes
            |> Seq.indexed
            |> Seq.tryFind (fun (_, case) -> SymbolEqualityComparer.Default.Equals(case, caseType))
            |> Option.bind (fun (index, case) -> List.tryItem index names |> Option.map (fun fq -> fq, case))
        | _ -> None

    /// <summary>The computations of the <c>Computed</c> bindings, by key.</summary>
    let computations = Dictionary<int, unit -> Checked<'Value>>()

    let remember (computation: unit -> Checked<'Value>) =
        let key = computations.Count
        computations[key] <- computation
        Computed key

    /// <summary>The shape of each loop: the analyzer runs the same recognizer, so a loop has one answer.</summary>
    let shapes = Dictionary<ILoopOperation, LoopShape>(HashIdentity.Reference)

    let shapeOf (loop: ILoopOperation) =
        match shapes.TryGetValue loop with
        | true, shape -> shape
        | _ ->
            let shape = LoopRecognizer.Recognize(loop, System.Threading.CancellationToken.None)
            shapes[loop] <- shape
            shape

    /// <summary>
    /// The loop lowering of the method that the lowering reads now (OPT-1): <c>[MorphirLoop]</c> on the method, on a type
    /// that holds it or on the assembly. With <c>Recursion</c>, a fold is a <c>LetRecursion</c>.
    /// </summary>
    let mutable loopLowering = LoopLowering.Traversal

    /// <summary>The names that the lowering made for each member declaration: the functions and the list parameters of loops.</summary>
    let madeNames = Dictionary<SyntaxNode, HashSet<Name>>()

    /// <summary>
    /// A name for a function or a parameter that the lowering makes: the words, and a number when an identifier of the
    /// member or a name made before has those words.
    /// </summary>
    let freshName (at: SyntaxNode) (words: string list) =
        let scope =
            at.AncestorsAndSelf()
            |> Seq.tryFind (fun n -> n :? MemberDeclarationSyntax)
            |> Option.defaultValue (at.SyntaxTree.GetRoot())

        let made =
            match madeNames.TryGetValue scope with
            | true, names -> names
            | _ ->
                let names = HashSet<Name>()
                madeNames[scope] <- names
                names

        let taken = namesOfMember at

        let rec pick (count: int) =
            let candidate = if count = 1 then Name words else Name(words @ [ string count ])
            if Set.contains candidate taken || made.Contains candidate then pick (count + 1) else candidate

        let chosen = pick 1
        made.Add chosen |> ignore
        chosen

    /// <summary>The operation without its implicit conversions.</summary>
    let rec unwrap (operation: IOperation) =
        match operation with
        | :? IConversionOperation as conversion when conversion.IsImplicit -> unwrap conversion.Operand
        | _ -> operation

    /// <summary>Whether an operation, or an operation in it, names the local or the parameter.</summary>
    let references (symbol: ISymbol) (operation: IOperation) =
        Seq.append [ operation ] (operation.Descendants())
        |> Seq.exists (fun o ->
            match o with
            | :? ILocalReferenceOperation as local -> SymbolEqualityComparer.Default.Equals(local.Local, symbol)
            | :? IParameterReferenceOperation as parameter -> SymbolEqualityComparer.Default.Equals(parameter.Parameter, symbol)
            | _ -> false)

    /// <summary>The local or the parameter that an operation names, with its name and its type.</summary>
    let variableOf (operation: IOperation) : (ISymbol * string * ITypeSymbol) option =
        match operation with
        | :? ILocalReferenceOperation as local when not local.IsDeclaration -> Some(local.Local :> ISymbol, local.Local.Name, local.Local.Type)
        | :? IParameterReferenceOperation as parameter -> Some(parameter.Parameter :> ISymbol, parameter.Parameter.Name, parameter.Parameter.Type)
        | _ -> None

    /// <summary>The type of a local or a parameter.</summary>
    let typeOfSymbol (symbol: ISymbol) : ITypeSymbol =
        match symbol with
        | :? ILocalSymbol as local -> local.Type
        | :? IParameterSymbol as parameter -> parameter.Type
        | _ -> null

    /// <summary>The single targets of a deconstruction assignment.</summary>
    let rec deconstructionTargets (target: IOperation) : IOperation list =
        match target with
        | :? ITupleOperation as tuple -> tuple.Elements |> List.ofSeq |> List.collect deconstructionTargets
        | other -> [ other ]

    /// <summary>Whether an operation changes a local or a parameter: an assignment, an increment or a deconstruction into locals that exist.</summary>
    let isChange (operation: IOperation) =
        match operation with
        | :? IDeconstructionAssignmentOperation as deconstruction ->
            deconstructionTargets deconstruction.Target |> List.exists (fun t -> (variableOf t).IsSome)
        | :? ISimpleAssignmentOperation
        | :? ICompoundAssignmentOperation
        | :? IIncrementOrDecrementOperation -> true
        | _ -> false

    /// <summary>The operation of an expression statement, or the operation itself.</summary>
    let statementOperation (operation: IOperation) =
        match operation with
        | :? IExpressionStatementOperation as statement -> statement.Operation
        | other -> other

    /// <summary>The statements of a loop body: the operations of a block, or the one statement.</summary>
    let statementsOf (body: IOperation) : IOperation list =
        match body with
        | :? IBlockOperation as block -> List.ofSeq block.Operations
        | other -> [ other ]

    let isConstantTrue (operation: IOperation) =
        not (isNull operation) && operation.ConstantValue.HasValue && (match operation.ConstantValue.Value with :? bool as b -> b | _ -> false)

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
            elif isUnionInterface named then
                unionInterfaceRefusal at
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

        | :? IDeclarationPatternOperation as declaration when (unionCase pattern.InputType declaration.MatchedType).IsSome ->
            // `Circle c` on a Shape: the constructor Circle with the case value bound to c.
            let fq, caseType = (unionCase pattern.InputType declaration.MatchedType).Value
            let symbol = declaration.DeclaredSymbol

            check {
                let! t = this.Type(at, pattern.InputType)
                let! ct = this.Type(at, caseType)
                let! w = builder.PatternWildcard(at, ct)

                if isNull symbol then
                    let! ctor = builder.PatternConstructor(at, t, fq, [ w ])
                    return ctor, env
                else
                    let! bound = builder.PatternAs(at, ct, w, nameOf symbol.Name)
                    let! ctor = builder.PatternConstructor(at, t, fq, [ bound ])
                    return ctor, Map.add symbol.Name Plain env
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

        | :? ITypePatternOperation as typePattern when (unionCase pattern.InputType typePattern.MatchedType).IsSome ->
            let fq, caseType = (unionCase pattern.InputType typePattern.MatchedType).Value

            check {
                let! t = this.Type(at, pattern.InputType)
                let! ct = this.Type(at, caseType)
                let! w = builder.PatternWildcard(at, ct)
                let! ctor = builder.PatternConstructor(at, t, fq, [ w ])
                return ctor, env
            }

        | :? ITypePatternOperation as typePattern when isUnionInterface typePattern.MatchedType -> unionInterfaceRefusal at

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

        | :? IConstantPatternOperation as constant when
            (unionOf pattern.InputType).IsSome
            && constant.Value.ConstantValue.HasValue
            && isNull constant.Value.ConstantValue.Value
            ->
            unsupported
                "union_null"
                at
                $"A 'null' arm on the union '{pattern.InputType.Name}' reaches the state with no case. A Morphir custom type has no such state: remove the arm."

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

        | :? IRecursivePatternOperation as recursive when (unionCase pattern.InputType recursive.MatchedType).IsSome ->
            this.UnionCasePattern(env, at, recursive)

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

    /// <summary>
    /// A positional or a property pattern on a case of a union, such as <c>Circle(var radius)</c> or <c>Rect { W: var w }</c>:
    /// the constructor of the case with the case value bound to a variable, and each local of a subpattern stands for a
    /// field of that value. A subpattern that tests a value has no Morphir form, so only <c>var</c>, a declaration and a
    /// discard are accepted.
    /// </summary>
    member private this.UnionCasePattern(env: Env, at: Morphir.Compilation.Location, recursive: IRecursivePatternOperation) : Checked<'Pattern * Env> =
        let fq, caseType = (unionCase recursive.InputType recursive.MatchedType).Value

        let refuse () : Checked<'Pattern * Env> =
            unsupported
                "pattern"
                at
                $"Only 'var', a declaration and a discard can stand in a pattern on the case '{caseType.Name}' of a union. Write '{caseType.Name} c' and read the fields of c."

        let fields =
            match caseType with
            | :? INamedTypeSymbol as named ->
                match plan.TypeNames.TryGetValue named.OriginalDefinition with
                | true, (_, { Kind = RecordAlias(fields, _) }) -> Some [ for name, member', fieldType in fields -> member', name, fieldType ]
                | _ -> None
            | _ -> None

        match fields with
        | None -> refuse ()
        | Some fields ->
            let positional = List.ofSeq recursive.DeconstructionSubpatterns

            let bound: (IPatternOperation * (string * Name * ITypeSymbol)) list option =
                if not positional.IsEmpty then
                    if positional.Length = fields.Length then Some(List.zip positional fields) else None
                else
                    recursive.PropertySubpatterns
                    |> List.ofSeq
                    |> List.map (fun property ->
                        match property.Member with
                        | :? IMemberReferenceOperation as reference ->
                            fields
                            |> List.tryFind (fun (member', _, _) -> member' = reference.Member.Name)
                            |> Option.map (fun field -> property.Pattern, field)
                        | _ -> None)
                    |> fun items -> if List.forall Option.isSome items then Some(List.choose id items) else None

            let variable =
                match recursive.DeclaredSymbol with
                | null -> caseVariable recursive.Syntax caseType.Name "Value"
                | symbol -> nameOf symbol.Name

            let locals =
                bound
                |> Option.bind (fun items ->
                    let lowered =
                        items
                        |> List.map (fun (subpattern, (_, name, fieldType)) ->
                            match subpattern with
                            | :? IDiscardPatternOperation -> Some None
                            | :? IDeclarationPatternOperation as declaration when
                                isNull declaration.DeclaredSymbol
                                || SymbolEqualityComparer.Default.Equals(declaration.NarrowedType, fieldType)
                                ->
                                match declaration.DeclaredSymbol with
                                | null -> Some None
                                | symbol -> Some(Some(symbol.Name, FieldOf(variable, caseType, name, fieldType)))
                            | _ -> None)

                    if List.forall Option.isSome lowered then Some(lowered |> List.choose id |> List.choose id) else None)

            match locals with
            | None -> refuse ()
            | Some locals ->
                check {
                    let! t = this.Type(at, recursive.InputType)
                    let! ct = this.Type(at, caseType)
                    let! w = builder.PatternWildcard(at, ct)
                    let! value = builder.PatternAs(at, ct, w, variable)
                    let! ctor = builder.PatternConstructor(at, t, fq, [ value ])

                    let env' =
                        let withLocals = locals |> List.fold (fun e (local, binding) -> Map.add local binding e) env

                        match recursive.DeclaredSymbol with
                        | null -> withLocals
                        | symbol -> Map.add symbol.Name Plain withLocals

                    return ctor, env'
                }

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
            | _ -> unsupportedReference at memberName
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

            | :? IPropertyReferenceOperation as property when not (isNull property.Instance) && property.Property.ContainingType.IsUnion ->
                unsupported
                    "union_value"
                    at
                    $"The property '{property.Property.Name}' of the union '{property.Property.ContainingType.Name}' reaches the state with no case. A Morphir custom type has no such state: match the cases of the union."

            | :? IDefaultValueOperation as default' when (unionOf default'.Type).IsSome ->
                unsupported
                    "union_default"
                    at
                    $"The default of the union '{default'.Type.Name}' is the state with no case. A Morphir custom type has no such state: give a case."

            | :? IIsPatternOperation as isPattern -> this.IsPattern(env, at, isPattern)

            | :? IIsTypeOperation as isType ->
                // `x is T` with a type that is not a pattern: the operand is lowered first, so a refusal in it is the reason.
                this.Value(env, isType.ValueOperand)
                |> Checked.bind (fun _ ->
                    let construct = describe "expression" operation.Syntax
                    unsupported "type_test" at $"The C# {construct} is not in the Morphir subset.")

            | :? IPropertyReferenceOperation as property when
                isNull property.Instance
                && property.Property.Name = "Nothing"
                && property.Property.ContainingType.OriginalDefinition.ToDisplayString() = "Morphir.SDK.CSharp.Maybe<T>"
                ->
                this.Type(at, property.Type)
                |> Checked.bind (fun inferred -> builder.Constructor(at, inferred, SdkMapping.sdk "Maybe" "Nothing"))

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
        | Some(FieldOf(record, recordType, field, fieldType)) ->
            check {
                let! rt = this.Type(at, recordType)
                let! subject = builder.Variable(at, rt, record)
                let! ft = this.Type(at, fieldType)
                return! builder.Field(at, ft, subject, field)
            }
        | Some(Computed key) -> computations[key] ()
        | Some Builder ->
            // A list that no loop filled yet is empty.
            this.Type(at, t) |> Checked.bind (fun inferred -> builder.List(at, inferred, []))
        | _ ->
            this.Type(at, t)
            |> Checked.bind (fun inferred -> builder.Variable(at, inferred, nameOf name))

    member private this.Conversion(env: Env, at, conversion: IConversionOperation) : Checked<'Value> =
        let source = conversion.Operand
        let target = conversion.Type
        let sourceType = source.Type

        if (unionCase target sourceType).IsSome then
            let fq, caseType = (unionCase target sourceType).Value
            this.UnionConstruction(env, at, fq, caseType, target, source)
        elif isUnionInterface target then
            unionInterfaceRefusal at
        elif conversion.Conversion.IsIdentity || conversion.Conversion.IsReference then
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

    /// <summary>The constructor of a case of a union applied to the case value: <c>Shape s = circle</c> is <c>Circle circle</c>.</summary>
    member private this.UnionConstruction(env: Env, at, fq: FQName, caseType: ITypeSymbol, unionType: ITypeSymbol, value: IOperation) : Checked<'Value> =
        check {
            let! constructorType = this.Function(at, [ caseType ], unionType)
            let! constructor = builder.Constructor(at, constructorType, fq)
            let! lowered = this.Value(env, value)
            return! this.Apply(at, constructor, [ caseType ], unionType, [ lowered ])
        }

    /// <summary>
    /// <c>x is P</c>: a pattern match that gives <c>True</c> for the pattern and <c>False</c> for any other value. A pattern
    /// that binds a name is refused, because the name is in scope after the test and Morphir has no such scope.
    /// </summary>
    member private this.IsPattern(env: Env, at, isPattern: IIsPatternOperation) : Checked<'Value> =
        check {
            let! subject = this.Value(env, isPattern.Value)
            let! pattern, env' = this.Pattern(env, isPattern.Pattern)

            if env'.Count <> env.Count then
                return!
                    unsupported
                        "type_test"
                        at
                        "An 'is' pattern that binds a name is not in the Morphir subset. Use a 'switch' expression with one arm for each case."
            else
                let! inputType = this.Type(at, isPattern.Value.Type)
                let! boolType = this.Type(at, isPattern.Type)
                let! otherwise = builder.PatternWildcard(at, inputType)
                let! yes = builder.Literal(at, boolType, LiteralValue.Bool true)
                let! no = builder.Literal(at, boolType, LiteralValue.Bool false)
                return! builder.PatternMatch(at, boolType, subject, [ pattern, yes; otherwise, no ])
        }

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
            this.Operator(
                at,
                kind,
                (left.Type, fun () -> this.Value(env, left)),
                (right.Type, fun () -> this.Value(env, right)),
                binary.Type
            )

    /// <summary>The SDK call of a binary operator on two operands, each with its type and its lowering.</summary>
    member private this.Operator
        (
            at,
            kind: BinaryOperatorKind,
            left: ITypeSymbol * (unit -> Checked<'Value>),
            right: ITypeSymbol * (unit -> Checked<'Value>),
            result: ITypeSymbol
        ) : Checked<'Value> =
        let leftType, _ = left
        let rightType, _ = right

        match SdkMapping.tryBinary kind (SdkMapping.operandOf leftType) with
        | None -> unsupportedReference at $"{leftType.ToDisplayString()} {SdkMapping.operatorText kind} {rightType.ToDisplayString()}"
        | Some fq ->
            let arguments =
                // C# `a % b` is Morphir `remainderBy b a`.
                if kind = BinaryOperatorKind.Remainder then [ right; left ] else [ left; right ]

            let parameters = [ for t, _ in arguments -> t ]

            check {
                let! lowered = arguments |> Checked.traverse (fun (_, lower) -> lower ())
                let! fn = this.Reference(at, fq, parameters, result)
                return! this.Apply(at, fn, parameters, result, lowered)
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
        elif target.MethodKind = MethodKind.LocalFunction then
            if not (Map.containsKey target.Name env) then
                unsupported "local_function" at $"The local function '{target.Name}' is called before its declaration. Declare the local function before the statement that calls it."
            else
                check {
                    let! inferred = this.Function(at, parameters, result)
                    let! fn = builder.Variable(at, inferred, nameOf target.Name)
                    let! lowered = lowerArguments ()
                    return! this.Apply(at, fn, parameters, result, lowered)
                }
        elif plan.ValueNames.ContainsKey target.OriginalDefinition then
            check {
                let! fn = this.Reference(at, plan.ValueNames[target.OriginalDefinition], parameters, result)
                let! lowered = lowerArguments ()
                return! this.Apply(at, fn, parameters, result, lowered)
            }
        elif SdkMapping.isListFromEnumerable target then
            // The list value that the C# backend writes: List<T>.FromEnumerable(new T[] { ... }) is the list of the items.
            let items =
                match arguments with
                | [ argument ] ->
                    match argument with
                    | :? IConversionOperation as conversion when conversion.IsImplicit -> conversion.Operand
                    | other -> other
                    |> function
                        | :? IArrayCreationOperation as array when not (isNull array.Initializer) -> Some(List.ofSeq array.Initializer.ElementValues)
                        | _ -> None
                | _ -> None

            match items with
            | Some elements ->
                check {
                    let! lowered = elements |> Checked.traverse (fun element -> this.Value(env, element))
                    let! inferred = this.Type(at, result)
                    return! builder.List(at, inferred, lowered)
                }
            | None -> unsupportedReference at (target.ToDisplayString())
        elif this.IsListIdentity invocation then
            this.Value(env, invocation.Arguments[0].Value)
        elif
            target.IsStatic
            && target.Name = "Just"
            && target.ContainingType.OriginalDefinition.ToDisplayString() = "Morphir.SDK.CSharp.Maybe<T>"
        then
            check {
                let! inferred = this.Function(at, parameters, result)
                let! just = builder.Constructor(at, inferred, SdkMapping.sdk "Maybe" "Just")
                let! lowered = lowerArguments ()
                return! this.Apply(at, just, parameters, result, lowered)
            }
        elif (this.Traversal(env, at, invocation)).IsSome then
            (this.Traversal(env, at, invocation)).Value
        else
            match SdkMapping.tryMethod target with
            | Some(fq, _) ->
                check {
                    let! fn = this.Reference(at, fq, parameters, result)
                    let! lowered = lowerArguments ()
                    return! this.Apply(at, fn, parameters, result, lowered)
                }
            | None when (this.UnionEquality invocation).IsSome ->
                let unionType, a, b = (this.UnionEquality invocation).Value

                check {
                    let! fn = this.Reference(at, SdkMapping.sdk "Basics" "equal", [ unionType; unionType ], result)
                    let! left = this.Value(env, a)
                    let! right = this.Value(env, b)
                    return! this.Apply(at, fn, [ unionType; unionType ], result, [ left; right ])
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

    /// <summary>The lambda of a function argument: a lambda, or a delegate made from a lambda.</summary>
    member private this.LambdaOf(operation: IOperation) : IAnonymousFunctionOperation option =
        match unwrap operation with
        | :? IAnonymousFunctionOperation as lambda -> Some lambda
        | :? IDelegateCreationOperation as creation ->
            match creation.Target with
            | :? IAnonymousFunctionOperation as lambda -> Some lambda
            | _ -> None
        | _ -> None

    /// <summary>The value that a lambda returns, when its body is one return.</summary>
    member private this.Returned(lambda: IAnonymousFunctionOperation) : IOperation option =
        match lambda.Body with
        | null -> None
        | body ->
            match List.ofSeq body.Operations with
            | [ :? IReturnOperation as return' ] when not (isNull return'.ReturnedValue) -> Some return'.ReturnedValue
            | _ -> None

    /// <summary>
    /// A function argument of one parameter: the function that the lambda applies to its parameter, else the lambda.
    /// <c>o =&gt; Notional(o)</c> is <c>notional</c>, the same IR as a loop that adds <c>Notional(o)</c>.
    /// </summary>
    member private this.FunctionArgument(env: Env, operation: IOperation) : Checked<'Value> =
        let direct =
            match this.LambdaOf operation with
            | Some lambda when lambda.Symbol.Parameters.Length = 1 ->
                this.Returned lambda
                |> Option.bind (fun returned -> this.Eta(env, returned, [ lambda.Symbol.Parameters[0] :> ISymbol ]))
            | _ -> None

        match direct with
        | Some fn -> fn
        | None -> this.Value(env, operation)

    /// <summary>
    /// A call of a functional form of the transform table: <c>Select</c>, <c>Where</c> and <c>Aggregate</c> of LINQ or of an
    /// SDK list, and <c>FilterMap</c> of an SDK list. They give the IR of the loop forms: <c>List.map</c>,
    /// <c>List.filter</c>, <c>List.foldl</c> and <c>List.filterMap</c>.
    /// </summary>
    member private this.Traversal(env: Env, at, invocation: IInvocationOperation) : Checked<'Value> option =
        let target = invocation.TargetMethod

        let containing =
            if isNull target.ContainingType then "" else target.ContainingType.OriginalDefinition.ToDisplayString()

        let ordinal (a: IArgumentOperation) =
            match a.Parameter with
            | null -> System.Int32.MaxValue
            | parameter -> parameter.Ordinal

        let arguments = [ for a in invocation.Arguments |> Seq.sortBy ordinal -> a ]

        // The source list and the other arguments: the instance of an SDK list, or the first argument of LINQ.
        let parts =
            match containing with
            | "Morphir.SDK.CSharp.List<T>" when not target.IsStatic && not (isNull invocation.Instance) -> Some(invocation.Instance, arguments)
            | "System.Linq.Enumerable" when not (List.isEmpty arguments) -> Some((List.head arguments).Value, List.tail arguments)
            | _ -> None

        let funcArguments (argument: IArgumentOperation) =
            match argument.Parameter with
            | null -> []
            | parameter ->
                match parameter.Type with
                | :? INamedTypeSymbol as func when func.TypeKind = TypeKind.Delegate && func.Name = "Func" -> List.ofSeq func.TypeArguments
                | _ -> []

        let list (operation: IOperation) =
            let t = (unwrap operation).Type

            match t with
            | :? INamedTypeSymbol as named ->
                match SdkMapping.tryType named with
                | Some(fq, _) when fq = SdkMapping.sdk "List" "List" -> true
                | _ -> false
            | _ -> false

        let unary (fq: FQName) (source: IOperation) (f: IArgumentOperation) (resultElement: ITypeSymbol -> ITypeSymbol -> ITypeSymbol) =
            match funcArguments f with
            | [ input; output ] ->
                Some(
                    check {
                        let! elementType = this.Type(at, input)
                        let! outputType = this.Type(at, output)
                        let! listType = this.ListOf(at, elementType)
                        let! resultType = this.Type(at, resultElement input output) |> Checked.bind (fun t -> this.ListOf(at, t))
                        let! fnType = this.Arrow(at, [ elementType ], outputType)
                        let! fn = this.FunctionArgument(env, f.Value)
                        let! xs = this.Value(env, unwrap source)
                        return! this.Call(at, fq, [ fnType, fn; listType, xs ], resultType)
                    }
                )
            | _ -> None

        match parts with
        | Some(source, rest) when list source ->
            match target.Name, rest with
            | "Select", [ f ] -> unary (SdkMapping.sdk "List" "map") source f (fun _ output -> output)
            | "Where", [ f ] -> unary (SdkMapping.sdk "List" "filter") source f (fun input _ -> input)
            | "FilterMap", [ f ] when containing = "Morphir.SDK.CSharp.List<T>" ->
                match funcArguments f with
                | [ _; (:? INamedTypeSymbol as maybe) ] when maybe.TypeArguments.Length = 1 ->
                    unary (SdkMapping.sdk "List" "filterMap") source f (fun _ _ -> maybe.TypeArguments[0])
                | _ -> None
            | "Aggregate", [ seed; func ] ->
                match funcArguments func with
                | [ stateSymbol; elementSymbol; _ ] -> Some(this.Aggregate(env, at, source, seed.Value, func.Value, stateSymbol, elementSymbol))
                | _ -> None
            | _ -> None
        | _ -> None

    /// <summary>
    /// <c>Aggregate(seed, (state, x) =&gt; ...)</c>: <c>List.foldl</c> with a function of the element first, then the state.
    /// </summary>
    member private this.Aggregate
        (env: Env, at, source: IOperation, seed: IOperation, func: IOperation, stateSymbol: ITypeSymbol, elementSymbol: ITypeSymbol)
        : Checked<'Value> =
        check {
            let! stateType = this.Type(at, stateSymbol)
            let! elementType = this.Type(at, elementSymbol)
            let! listType = this.ListOf(at, elementType)
            let! fnType = this.Arrow(at, [ elementType; stateType ], stateType)
            let! stepType = this.Arrow(at, [ stateType ], stateType)

            let! fn =
                match this.LambdaOf func with
                | Some lambda when lambda.Symbol.Parameters.Length = 2 ->
                    let state = lambda.Symbol.Parameters[0]
                    let element = lambda.Symbol.Parameters[1]

                    let direct =
                        this.Returned lambda
                        |> Option.bind (fun returned -> this.Eta(env, returned, [ element :> ISymbol; state :> ISymbol ]))

                    // `var (a, b) = state;` first, and no other use of the state: the state is the tuple pattern (a, b).
                    let operations = List.ofSeq lambda.Body.Operations

                    let destructured =
                        match operations with
                        | (:? IExpressionStatementOperation as first) :: after ->
                            match first.Operation with
                            | :? IDeconstructionAssignmentOperation as deconstruction when
                                (match unwrap deconstruction.Value with
                                 | :? IParameterReferenceOperation as p -> SymbolEqualityComparer.Default.Equals(p.Parameter, state)
                                 | _ -> false)
                                && not (after |> List.exists (references state))
                                ->
                                let rec declared (target: IOperation) =
                                    match target with
                                    | :? IDeclarationExpressionOperation as declaration -> declared declaration.Expression
                                    | :? ITupleOperation as tuple ->
                                        let items = tuple.Elements |> List.ofSeq |> List.map declared
                                        if items |> List.forall Option.isSome then Some(items |> List.collect Option.get) else None
                                    | :? ILocalReferenceOperation as local when local.IsDeclaration -> Some [ local.Local ]
                                    | _ -> None

                                declared deconstruction.Target |> Option.map (fun locals -> locals, after)
                            | _ -> None
                        | _ -> None

                    match direct, destructured with
                    | Some fn, _ -> fn
                    | None, Some(locals, after) ->
                        check {
                            let inner = locals |> List.fold (fun (e: Env) l -> Map.add l.Name Plain e) (Map.add element.Name Plain env)
                            let! body = this.Statements(inner, stateSymbol, after, at)
                            let! items = locals |> Checked.traverse (fun l -> this.Type(at, l.Type) |> Checked.bind (fun t -> this.Binder(at, t, nameOf l.Name)))
                            let! statePattern = builder.PatternTuple(at, stateType, items)
                            let! step = builder.Lambda(at, stepType, statePattern, body)
                            let! elementPattern = this.Binder(at, elementType, nameOf element.Name)
                            return! builder.Lambda(at, fnType, elementPattern, step)
                        }
                    | None, None ->
                        check {
                            let inner = env |> Map.add state.Name Plain |> Map.add element.Name Plain
                            let! body = this.Statements(inner, stateSymbol, operations, at)
                            let! statePattern = this.Binder(at, stateType, nameOf state.Name)
                            let! step = builder.Lambda(at, stepType, statePattern, body)
                            let! elementPattern = this.Binder(at, elementType, nameOf element.Name)
                            return! builder.Lambda(at, fnType, elementPattern, step)
                        }
                | _ ->
                    // A function value of the state, then the element: a lambda of the element, then the state, that calls it.
                    check {
                        let! f = this.Value(env, func)
                        let elementName = freshName func.Syntax [ "item" ]
                        let stateName = freshName func.Syntax [ "state" ]
                        let! partialType = this.Arrow(at, [ elementType ], stateType)
                        let! stateVariable = builder.Variable(at, stateType, stateName)
                        let! elementVariable = builder.Variable(at, elementType, elementName)
                        let! partial = builder.Apply(at, partialType, f, stateVariable)
                        let! applied = builder.Apply(at, stateType, partial, elementVariable)
                        let! statePattern = this.Binder(at, stateType, stateName)
                        let! step = builder.Lambda(at, stepType, statePattern, applied)
                        let! elementPattern = this.Binder(at, elementType, elementName)
                        return! builder.Lambda(at, fnType, elementPattern, step)
                    }

            let! initial = this.Value(env, seed)
            let! xs = this.Value(env, unwrap source)
            return! this.Call(at, SdkMapping.sdk "List" "foldl", [ fnType, fn; stateType, initial; listType, xs ], stateType)
        }

    /// <summary>
    /// <c>a.Equals(b)</c> on two values of one union of the package: the equality of Morphir. The backend writes this form,
    /// because a plain union has no <c>==</c> (CS0019).
    /// </summary>
    member private this.UnionEquality(invocation: IInvocationOperation) : (ITypeSymbol * IOperation * IOperation) option =
        let rec unwrap (operation: IOperation) =
            match operation with
            | :? IConversionOperation as conversion when conversion.IsImplicit -> unwrap conversion.Operand
            | _ -> operation

        match invocation.TargetMethod.Name, invocation.Instance, List.ofSeq invocation.Arguments with
        | "Equals", instance, [ argument ] when not invocation.TargetMethod.IsStatic && not (isNull instance) && (unionOf instance.Type).IsSome ->
            let other = unwrap argument.Value

            if SymbolEqualityComparer.Default.Equals(other.Type, instance.Type) then Some(instance.Type, instance, other) else None
        | _ -> None

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

        match unionOf created with
        | Some union ->
            // `new Shape(circle)` is the constructor of the case; `new Shape()` is the state with no case.
            let case =
                match List.ofSeq creation.Arguments, creation.Constructor with
                | [ argument ], constructor when not (isNull constructor) && constructor.Parameters.Length = 1 ->
                    unionCase union constructor.Parameters[0].Type |> Option.map (fun found -> found, argument.Value)
                | _ -> None

            match case with
            | Some((fq, caseType), value) -> this.UnionConstruction(env, at, fq, caseType, created, value)
            | None ->
                unsupported
                    "union_default"
                    at
                    $"A '{union.Name}' with no case value is the state with no case. A Morphir custom type has no such state: give a case."
        | None ->

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
    /// ifs become conditionals, switch statements become pattern matches and loops become list functions or recursions.
    /// A list that ends before a return is refused at <c>endAt</c>, the end of the block that holds it.
    /// </summary>
    member this.Statements(env: Env, result: ITypeSymbol, statements: IOperation list, endAt: Morphir.Compilation.Location) : Checked<'Value> =
        this.Flow(env, result, { End = None; Continue = None; Break = None }, statements, endAt)

    /// <summary>
    /// Lowers a list of statements with the given flow: the end of the list, a <c>continue</c> and a <c>break</c> give the
    /// values of the flow. A <c>return</c> always gives its value.
    /// </summary>
    member this.Flow
        (env: Env, result: ITypeSymbol, flow: Flow<'Value>, statements: IOperation list, endAt: Morphir.Compilation.Location)
        : Checked<'Value> =
        match statements with
        | [] ->
            match flow.End with
            | Some finish -> finish env
            | None -> unsupported "missing_return" endAt "A method body must end with a return."
        | first :: rest ->
            let at = locate first.Syntax

            match first with
            | :? IReturnOperation as return' when not (isNull return'.ReturnedValue) -> this.Value(env, return'.ReturnedValue)
            | :? IBranchOperation as branch when branch.BranchKind = BranchKind.Break && flow.Break.IsSome -> flow.Break.Value env
            | :? IBranchOperation as branch when branch.BranchKind = BranchKind.Continue && flow.Continue.IsSome ->
                flow.Continue.Value env
            | :? IBlockOperation as block -> this.Flow(env, result, flow, List.ofSeq block.Operations @ rest, endAt)
            | :? IExpressionStatementOperation as statement when
                (match statement.Operation with
                 | :? ISimpleAssignmentOperation as assignment -> assignment.Target :? IDiscardOperation
                 | _ -> false)
                ->
                // `_ = value;` names a value and changes nothing.
                this.Flow(env, result, flow, rest, endAt)
            | :? IVariableDeclarationGroupOperation as group -> this.Declarations(env, result, flow, group, rest, endAt)
            | :? IExpressionStatementOperation as statement when isChange statement.Operation ->
                this.Change(env, result, flow, statement.Operation, rest, endAt)
            | change when isChange change -> this.Change(env, result, flow, change, rest, endAt)
            | :? IExpressionStatementOperation as statement when (statement.Operation :? IDeconstructionAssignmentOperation) ->
                this.Deconstruction(env, at, result, flow, statement.Operation :?> IDeconstructionAssignmentOperation, rest, endAt)
            | :? IConditionalOperation as conditional ->
                // A branch whose end can be reached goes on with the statements after the if.
                let branch (operation: IOperation) =
                    let model = operation.SemanticModel

                    let reachable =
                        match operation.Syntax with
                        | :? StatementSyntax as statement when not (isNull model) ->
                            let analysis = model.AnalyzeControlFlow statement
                            not (isNull analysis) && analysis.Succeeded && analysis.EndPointIsReachable
                        | _ -> false

                    if reachable then
                        this.Flow(env, result, flow, operation :: rest, endAt)
                    else
                        this.Flow(env, result, flow, [ operation ], locate operation.Syntax)

                check {
                    let! condition = this.Value(env, conditional.Condition)
                    let! whenTrue = branch conditional.WhenTrue

                    let! whenFalse =
                        match conditional.WhenFalse with
                        | null -> this.Flow(env, result, flow, rest, endAt)
                        | other -> branch other

                    let! inferred = this.Type(at, result)
                    return! builder.IfThenElse(at, inferred, condition, whenTrue, whenFalse)
                }
            | :? ISwitchOperation as switch -> this.SwitchStatement(env, at, result, flow, switch, rest, endAt)
            | :? ILoopOperation as loop -> this.Loop(env, result, flow, loop, rest, endAt)
            | :? ILocalFunctionOperation ->
                // Local functions next to each other are one group: they can call each other.
                let rec take (operations: IOperation list) (found: ILocalFunctionOperation list) =
                    match operations with
                    | (:? ILocalFunctionOperation as f) :: tail -> take tail (found @ [ f ])
                    | other -> found, other

                let functions, after = take statements []
                this.LocalFunctions(env, result, flow, functions, after, endAt)
            | _ ->
                let construct = describe "statement" first.Syntax
                unsupported (slugOf first) at $"The C# {construct} is not in the Morphir subset."

    /// <summary>A group of local functions: a <c>LetRecursion</c> with a definition for each function, and the statements after them.</summary>
    member private this.LocalFunctions
        (env: Env, result: ITypeSymbol, flow: Flow<'Value>, functions: ILocalFunctionOperation list, rest: IOperation list, endAt)
        : Checked<'Value> =
        let at = locate (List.head functions).Syntax
        let scope = functions |> List.fold (fun (e: Env) f -> Map.add f.Symbol.Name Plain e) env

        let definition (f: ILocalFunctionOperation) =
            let symbol = f.Symbol
            let fat = locate f.Syntax

            if symbol.IsGenericMethod then
                unsupported "local_function" fat $"The generic local function '{symbol.Name}' is not in the Morphir subset."
            elif isNull f.Body then
                unsupported "local_function" fat $"The local function '{symbol.Name}' has no body that Morphir can read."
            else
                let parameters = List.ofSeq symbol.Parameters
                let inner = parameters |> List.fold (fun (e: Env) p -> Map.add p.Name Plain e) scope

                let endOf =
                    match f.Syntax with
                    | :? LocalFunctionStatementSyntax as statement when not (isNull statement.Body) ->
                        CSharpCompiler.locationOfToken compiled statement.Body.CloseBraceToken |> Option.defaultValue fat
                    | _ -> fat

                check {
                    let! inputs = parameters |> Checked.traverse (fun p -> this.Type(fat, p.Type) |> Checked.map (fun t -> nameOf p.Name, t))
                    let! output = this.Type(fat, symbol.ReturnType)
                    let! body = this.Statements(inner, symbol.ReturnType, List.ofSeq f.Body.Operations, endOf)
                    let! lowered = builder.ValueDefinition(fat, inputs, output, body)
                    return nameOf symbol.Name, lowered
                }

        check {
            let! definitions = functions |> Checked.traverse definition
            let! inValue = this.Flow(scope, result, flow, rest, endAt)
            let! inferred = this.Type(at, result)
            return! builder.LetRecursion(at, inferred, definitions, inValue)
        }

    /// <summary>
    /// A declaration group. An empty <c>List&lt;T&gt;</c> is a list that a later loop fills; a local that only the next loop
    /// reads is its initial state; any other local is a let.
    /// </summary>
    member private this.Declarations
        (env: Env, result: ITypeSymbol, flow: Flow<'Value>, group: IVariableDeclarationGroupOperation, rest: IOperation list, endAt)
        : Checked<'Value> =
        let declarators = [ for declaration in group.Declarations do yield! declaration.Declarators ]

        // The declarations up to the next loop, and the loop.
        let rec run (operations: IOperation list) (found: IVariableDeclaratorOperation list) =
            match operations with
            | (:? IVariableDeclarationGroupOperation as next) :: tail ->
                run tail (found @ [ for declaration in next.Declarations do yield! declaration.Declarators ])
            | (:? ILoopOperation as loop) :: _ -> found, Some loop
            | _ -> found, None

        let laterDeclarators, nextLoop = run rest []

        let isStateOfNextLoop (local: ILocalSymbol) =
            match nextLoop with
            | Some loop -> (shapeOf loop).State |> Seq.exists (fun s -> SymbolEqualityComparer.Default.Equals(s, local))
            | None -> false

        let rec bind (env: Env) (items: IVariableDeclaratorOperation list) : Checked<'Value> =
            match items with
            | [] -> this.Flow(env, result, flow, rest, endAt)
            | declarator :: tail ->
                let local = declarator.Symbol
                let dat = locate declarator.Syntax

                match declarator.Initializer with
                | null -> unsupported "uninitialized_local" dat "A local variable needs an initial value."
                | initializer when LoopRecognizer.IsSystemList local.Type && LoopRecognizer.IsEmptyList initializer.Value ->
                    bind (Map.add local.Name Builder env) tail
                | initializer when
                    isStateOfNextLoop local
                    && not (
                        (tail @ laterDeclarators)
                        |> List.exists (fun later -> not (isNull later.Initializer) && references local later.Initializer.Value)
                    )
                    ->
                    let before = env
                    bind (Map.add local.Name (remember (fun () -> this.Value(before, initializer.Value))) env) tail
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

    /// <summary>
    /// Whether the statements after a change can read the new value as a computation: they only change other locals, with
    /// no read of this local, and jump. Then the change needs no let: <c>sum += p; count++;</c> is <c>(sum + p, count + 1)</c>.
    /// </summary>
    member private this.Substitutable(symbol: ISymbol, rest: IOperation list) =
        rest
        |> List.forall (fun operation ->
            match operation with
            | :? IBranchOperation as branch -> branch.BranchKind = BranchKind.Break || branch.BranchKind = BranchKind.Continue
            | other when isChange (statementOperation other) -> not (references symbol other)
            | _ -> false)

    /// <summary>
    /// A change of a local or a parameter, which only a loop has: the statements after it see the new value, as a
    /// computation or as a let with the same name.
    /// </summary>
    member private this.Change(env: Env, result: ITypeSymbol, flow: Flow<'Value>, operation: IOperation, rest: IOperation list, endAt) =
        let at = locate operation.Syntax

        let notLocal () =
            let construct = describe "change" operation.Syntax
            unsupported "mutation" at $"The C# {construct} changes a value that is not a local or a parameter."

        let single (target: IOperation) (value: unit -> Checked<'Value>) =
            match variableOf target with
            | None -> notLocal ()
            | Some(symbol, name, t) ->
                if this.Substitutable(symbol, rest) then
                    this.Flow(Map.add name (remember value) env, result, flow, rest, endAt)
                else
                    check {
                        let! lowered = value ()
                        let! localType = this.Type(at, t)
                        let! definition = builder.ValueDefinition(at, [], localType, lowered)
                        let! inValue = this.Flow(Map.add name Plain env, result, flow, rest, endAt)
                        let! inferred = this.Type(at, result)
                        return! builder.LetDefinition(at, inferred, nameOf name, definition, inValue)
                    }

        let one (t: ITypeSymbol) : Checked<obj> =
            match t.SpecialType with
            | SpecialType.System_Int32 -> Checked.pass (box 1)
            | SpecialType.System_Double -> Checked.pass (box 1.0)
            | SpecialType.System_Decimal -> Checked.pass (box 1m)
            | _ -> unsupported "mutation" at $"An increment of '{t.ToDisplayString()}' is not in the Morphir subset."

        match operation with
        | :? ISimpleAssignmentOperation as assignment -> single assignment.Target (fun () -> this.Value(env, assignment.Value))
        | :? ICompoundAssignmentOperation as compound ->
            if not compound.InConversion.IsIdentity || not compound.OutConversion.IsIdentity then
                unsupported "conversion" at "A compound assignment with a conversion is not in the Morphir subset."
            else
                single compound.Target (fun () ->
                    this.Operator(
                        at,
                        compound.OperatorKind,
                        (compound.Target.Type, fun () -> this.Value(env, compound.Target)),
                        (compound.Value.Type, fun () -> this.Value(env, compound.Value)),
                        compound.Type
                    ))
        | :? IIncrementOrDecrementOperation as step ->
            let kind = if step.Kind = OperationKind.Increment then BinaryOperatorKind.Add else BinaryOperatorKind.Subtract
            let t = step.Target.Type

            single step.Target (fun () ->
                this.Operator(
                    at,
                    kind,
                    (t, fun () -> this.Value(env, step.Target)),
                    (t, fun () -> one t |> Checked.bind (fun value -> this.Literal(at, value, t))),
                    t
                ))
        | :? IDeconstructionAssignmentOperation as deconstruction ->
            let targets = deconstructionTargets deconstruction.Target
            let variables = targets |> List.map variableOf

            if variables |> List.exists Option.isNone then
                notLocal ()
            else
                let variables = variables |> List.choose id

                match unwrap deconstruction.Value with
                | :? ITupleOperation as tuple when
                    tuple.Elements.Length = List.length variables
                    && variables |> List.forall (fun (symbol, _, _) -> this.Substitutable(symbol, rest))
                    ->
                    // The values are all computed before any local changes, as C# does.
                    let env' =
                        List.zip variables (List.ofSeq tuple.Elements)
                        |> List.fold (fun (e: Env) ((_, name, _), value) -> Map.add name (remember (fun () -> this.Value(env, value))) e) env

                    this.Flow(env', result, flow, rest, endAt)
                | _ ->
                    check {
                        let! subject = this.Value(env, deconstruction.Value)
                        let! valueType = this.Type(at, deconstruction.Value.Type)

                        let! items =
                            variables
                            |> Checked.traverse (fun (_, name, t) ->
                                check {
                                    let! itemType = this.Type(at, t)
                                    let! wildcard = builder.PatternWildcard(at, itemType)
                                    return! builder.PatternAs(at, itemType, wildcard, nameOf name)
                                })

                        let! pattern = builder.PatternTuple(at, valueType, items)
                        let env' = variables |> List.fold (fun (e: Env) (_, name, _) -> Map.add name Plain e) env
                        let! inValue = this.Flow(env', result, flow, rest, endAt)
                        let! inferred = this.Type(at, result)
                        return! builder.Destructure(at, inferred, pattern, subject, inValue)
                    }
        | _ -> notLocal ()

    // ----- loops -----

    /// <summary>The function type from the argument types to the result type.</summary>
    member private this.Arrow(at, arguments: 'Type list, result: 'Type) : Checked<'Type> =
        List.foldBack
            (fun (argument: 'Type) (tail: Checked<'Type>) -> tail |> Checked.bind (fun t -> builder.TypeFunction(at, argument, t)))
            arguments
            (Checked.pass result)

    /// <summary>A call of an SDK function with the types of its arguments and of its result.</summary>
    member private this.Call(at, fq: FQName, arguments: ('Type * 'Value) list, result: 'Type) : Checked<'Value> =
        let types = arguments |> List.map fst

        let rec go (current: 'Value) (remaining: ('Type * 'Value) list) =
            match remaining with
            | [] -> Checked.pass current
            | (_, argument) :: tail ->
                this.Arrow(at, tail |> List.map fst, result)
                |> Checked.bind (fun inferred -> builder.Apply(at, inferred, current, argument))
                |> Checked.bind (fun applied -> go applied tail)

        this.Arrow(at, types, result)
        |> Checked.bind (fun inferred -> builder.Reference(at, inferred, fq))
        |> Checked.bind (fun fn -> go fn arguments)

    /// <summary>The pattern that binds a name: <c>(as _ name)</c>.</summary>
    member private this.Binder(at, t: 'Type, name: Name) : Checked<'Pattern> =
        builder.PatternWildcard(at, t) |> Checked.bind (fun wildcard -> builder.PatternAs(at, t, wildcard, name))

    /// <summary>The SDK list type of an element type.</summary>
    member private this.ListOf(at, element: 'Type) : Checked<'Type> =
        builder.TypeReference(at, SdkMapping.sdk "List" "List", [ element ])

    /// <summary>
    /// The function that a body applies to its parameters as its last arguments, when no other part of the body names
    /// them: <c>o =&gt; Notional(o)</c> is <c>notional</c>, and <c>(x, acc) =&gt; Add(1, x, acc)</c> is <c>add 1</c>.
    /// The parameters are in the order of the Morphir function.
    /// </summary>
    member private this.Eta(env: Env, operation: IOperation, parameters: ISymbol list) : Checked<'Value> option =
        let at = locate operation.Syntax
        let names (symbol: ISymbol) (o: IOperation) = parameters |> List.exists (fun p -> SymbolEqualityComparer.Default.Equals(p, symbol)) && references symbol o

        match unwrap operation with
        | :? IInvocationOperation as invocation ->
            let ordinal (a: IArgumentOperation) =
                match a.Parameter with
                | null -> System.Int32.MaxValue
                | parameter -> parameter.Ordinal

            let arguments = [ for a in invocation.Arguments |> Seq.sortBy ordinal -> a.Value ]
            let count = List.length parameters

            if List.length arguments < count then
                None
            else
                let leading = List.take (List.length arguments - count) arguments
                let trailing = List.skip (List.length arguments - count) arguments

                let isParameter (argument: IOperation, parameter: ISymbol) =
                    match variableOf argument with
                    | Some(symbol, _, _) -> SymbolEqualityComparer.Default.Equals(symbol, parameter)
                    | None -> false

                let free (o: IOperation) = parameters |> List.forall (fun p -> not (names p o))

                let target = invocation.TargetMethod
                let parameterTypes = [ for p in target.Parameters -> p.Type ]

                let headOk =
                    List.zip trailing parameters |> List.forall isParameter
                    && leading |> List.forall free
                    && (isNull invocation.Instance || free invocation.Instance)

                if not headOk then
                    None
                else
                    let applyLeading (fn: Checked<'Value>) =
                        check {
                            let! f = fn
                            let! lowered = leading |> Checked.traverse (fun a -> this.Value(env, a))
                            return! this.Apply(at, f, parameterTypes, target.ReturnType, lowered)
                        }

                    if target.MethodKind = MethodKind.DelegateInvoke then
                        Some(applyLeading (this.Value(env, invocation.Instance)))
                    elif plan.ValueNames.ContainsKey target.OriginalDefinition then
                        Some(applyLeading (this.Reference(at, plan.ValueNames[target.OriginalDefinition], parameterTypes, target.ReturnType)))
                    else
                        match SdkMapping.tryMethod target with
                        | Some(fq, _) -> Some(applyLeading (this.Reference(at, fq, parameterTypes, target.ReturnType)))
                        | None -> None
        | _ -> None

    /// <summary>The value of a list of statements whose end gives a value.</summary>
    member private this.Ending(env: Env, result: ITypeSymbol, statements: IOperation list, at, finish: Env -> Checked<'Value>) =
        this.Flow(env, result, { End = Some finish; Continue = None; Break = None }, statements, at)

    /// <summary>
    /// The statements after a loop that gives a value for locals: the value itself when the statements only return the
    /// locals, else a let (one local) or a destructure (more locals) and the statements.
    /// </summary>
    member private this.After
        (env: Env, result: ITypeSymbol, flow: Flow<'Value>, at, locals: (string * ITypeSymbol) list, value: 'Value, rest: IOperation list, endAt)
        : Checked<'Value> =
        let rec plain (operation: IOperation) =
            match unwrap operation with
            | :? IInvocationOperation as invocation when this.IsListIdentity invocation -> plain invocation.Arguments[0].Value
            | other -> other

        let isLocal (name: string) (operation: IOperation) =
            match plain operation with
            | :? ILocalReferenceOperation as local -> local.Local.Name = name
            | _ -> false

        let returnsOnly =
            match rest, locals with
            | [ :? IReturnOperation as return' ], [ name, _ ] when not (isNull return'.ReturnedValue) -> isLocal name return'.ReturnedValue
            | [ :? IReturnOperation as return' ], _ when not (isNull return'.ReturnedValue) ->
                match plain return'.ReturnedValue with
                | :? ITupleOperation as tuple ->
                    tuple.Elements.Length = List.length locals
                    && List.forall2 (fun (element: IOperation) (name, _) -> isLocal name element) (List.ofSeq tuple.Elements) locals
                | _ -> false
            | _ -> false

        let env' = locals |> List.fold (fun (e: Env) (name, _) -> Map.add name Plain e) env

        if returnsOnly then
            Checked.pass value
        else
            match locals with
            | [ name, t ] ->
                check {
                    let! localType = this.Type(at, t)
                    let! definition = builder.ValueDefinition(at, [], localType, value)
                    let! inValue = this.Flow(env', result, flow, rest, endAt)
                    let! inferred = this.Type(at, result)
                    return! builder.LetDefinition(at, inferred, nameOf name, definition, inValue)
                }
            | _ ->
                check {
                    let! types = locals |> Checked.traverse (fun (_, t) -> this.Type(at, t))
                    let! items = List.zip locals types |> Checked.traverse (fun ((name, _), t) -> this.Binder(at, t, nameOf name))
                    let! tupleType = builder.TypeTuple(at, types)
                    let! pattern = builder.PatternTuple(at, tupleType, items)
                    let! inValue = this.Flow(env', result, flow, rest, endAt)
                    let! inferred = this.Type(at, result)
                    return! builder.Destructure(at, inferred, pattern, value, inValue)
                }

    /// <summary>Whether a call gives its list argument as it is: <c>List&lt;T&gt;.FromEnumerable(xs)</c> or <c>xs.ToList()</c>.</summary>
    member private this.IsListIdentity(invocation: IInvocationOperation) : bool =
        let target = invocation.TargetMethod
        let containing = if isNull target.ContainingType then "" else target.ContainingType.OriginalDefinition.ToDisplayString()

        invocation.Arguments.Length = 1
        && (match SdkMapping.tryType (target.ReturnType :?> INamedTypeSymbol) with
            | Some(fq, _) -> fq = SdkMapping.sdk "List" "List"
            | None -> false)
        && ((containing = "Morphir.SDK.CSharp.List<T>" && target.Name = "FromEnumerable" && target.IsStatic)
            || (containing = "System.Linq.Enumerable" && target.Name = "ToList"))

    /// <summary>A loop: a list function, a fold or a recursion, by the shape that the loop recognizer gives.</summary>
    member private this.Loop(env: Env, result: ITypeSymbol, flow: Flow<'Value>, loop: ILoopOperation, rest: IOperation list, endAt) =
        let at = locate loop.Syntax
        let shape = shapeOf loop

        match shape.Form with
        | LoopForm.Map
        | LoopForm.Filter
        | LoopForm.FilterMap -> this.ListLoop(env, result, flow, shape, rest, endAt)
        | LoopForm.Fold when loopLowering = LoopLowering.Recursion -> this.RecursionLoop(env, result, flow, shape, rest, endAt)
        | LoopForm.Fold -> this.FoldLoop(env, result, flow, shape, rest, endAt)
        | LoopForm.Recursion -> this.RecursionLoop(env, result, flow, shape, rest, endAt)
        | _ ->
            // The same code and message as the admission analyzer: one recognizer gives one answer.
            let reason =
                match shape.Refusal with
                | null -> "has no Morphir form"
                | refusal -> refusal.Reason

            Checked.fail [
                Diagnostic.error (CSharpCodes.unsupported Rules.Loop.Slug) (System.String.Format(Rules.Loop.Message, shape.Keyword, reason))
                |> Diagnostic.at at
            ]

    /// <summary>The value of the collection of a <c>foreach</c>, and its element type.</summary>
    member private this.Collection(env: Env, at, shape: LoopShape) =
        let foreach' = shape.Loop :?> IForEachLoopOperation
        let collection = unwrap foreach'.Collection
        let element = shape.Variable.Type

        check {
            let! elementType = this.Type(at, element)
            let! listType = this.ListOf(at, elementType)
            let! value = this.Value(env, collection)
            return value, elementType, listType
        }

    /// <summary>A <c>foreach</c> that fills a list: <c>List.map</c>, <c>List.filter</c> or <c>List.filterMap</c>.</summary>
    member private this.ListLoop(env: Env, result: ITypeSymbol, flow: Flow<'Value>, shape: LoopShape, rest: IOperation list, endAt) =
        let at = locate shape.Loop.Syntax
        let variable = shape.Variable
        let list = shape.Builder

        match Map.tryFind list.Name env with
        | Some Builder ->
            let added = (list.Type :?> INamedTypeSymbol).TypeArguments[0]
            let inner = Map.add variable.Name Plain env
            let prelude = List.ofSeq shape.Statements

            let lambda (bodyType: 'Type) (body: Checked<'Value>) =
                check {
                    let! elementType = this.Type(at, variable.Type)
                    let! pattern = this.Binder(at, elementType, nameOf variable.Name)
                    let! fnType = this.Arrow(at, [ elementType ], bodyType)
                    let! lowered = body
                    return! builder.Lambda(at, fnType, pattern, lowered)
                }

            let direct (operation: IOperation) =
                if List.isEmpty prelude then this.Eta(env, operation, [ variable :> ISymbol ]) else None

            check {
                let! source, elementType, listType = this.Collection(env, at, shape)
                let! addedType = this.Type(at, added)
                let! addedList = this.ListOf(at, addedType)

                let! traversal =
                    match shape.Form with
                    | LoopForm.Map ->
                        check {
                            let! fn =
                                match direct shape.Added with
                                | Some fn -> fn
                                | None -> lambda addedType (this.Ending(inner, added, prelude, at, fun e -> this.Value(e, shape.Added)))

                            let! fnType = this.Arrow(at, [ elementType ], addedType)
                            return! this.Call(at, SdkMapping.sdk "List" "map", [ fnType, fn; listType, source ], addedList)
                        }
                    | LoopForm.Filter ->
                        check {
                            let! boolType = this.Type(at, shape.Condition.Type)

                            let! fn =
                                match direct shape.Condition with
                                | Some fn -> fn
                                | None -> lambda boolType (this.Ending(inner, shape.Condition.Type, prelude, at, fun e -> this.Value(e, shape.Condition)))

                            let! fnType = this.Arrow(at, [ elementType ], boolType)
                            return! this.Call(at, SdkMapping.sdk "List" "filter", [ fnType, fn; listType, source ], listType)
                        }
                    | _ ->
                        check {
                            let! maybeType = builder.TypeReference(at, SdkMapping.sdk "Maybe" "Maybe", [ addedType ])

                            let choose (e: Env) =
                                check {
                                    let! condition = this.Value(e, shape.Condition)
                                    let! value = this.Value(e, shape.Added)
                                    let! justType = this.Arrow(at, [ addedType ], maybeType)
                                    let! just = builder.Constructor(at, justType, SdkMapping.sdk "Maybe" "Just")
                                    let! some = builder.Apply(at, maybeType, just, value)
                                    let! none = builder.Constructor(at, maybeType, SdkMapping.sdk "Maybe" "Nothing")
                                    return! builder.IfThenElse(at, maybeType, condition, some, none)
                                }

                            // The body type is the Maybe of the added type; the C# type of the body does not exist, so the
                            // statements before the add get the added type, which only the inferred types of lets read.
                            let! fn = lambda maybeType (this.Ending(inner, added, prelude, at, choose))
                            let! fnType = this.Arrow(at, [ elementType ], maybeType)
                            return! this.Call(at, SdkMapping.sdk "List" "filterMap", [ fnType, fn; listType, source ], addedList)
                        }

                return! this.After(env, result, flow, at, [ list.Name, list.Type ], traversal, rest, endAt)
            }
        | _ -> unsupported "list_builder" at $"The list '{list.Name}' must be empty before the loop that adds to it."

    /// <summary>A <c>foreach</c> that changes locals: <c>List.foldl</c>, with a tuple state for two or more locals.</summary>
    member private this.FoldLoop(env: Env, result: ITypeSymbol, flow: Flow<'Value>, shape: LoopShape, rest: IOperation list, endAt) =
        let at = locate shape.Loop.Syntax
        let variable = shape.Variable
        let state = [ for s in shape.State -> s, s.Name, typeOfSymbol s ]
        let compilation = compiled.Compilation

        let stateSymbol: ITypeSymbol =
            match state with
            | [ _, _, t ] -> t
            | _ -> compilation.CreateTupleTypeSymbol(System.Collections.Immutable.ImmutableArray.CreateRange [ for _, _, t in state -> t ]) :> ITypeSymbol

        let inner =
            state |> List.fold (fun (e: Env) (_, name, _) -> Map.add name Plain e) (Map.add variable.Name Plain env)

        let statements = statementsOf shape.Loop.Body

        let stateValue (e: Env) =
            match state with
            | [ _, name, t ] -> this.Variable(e, at, name, t)
            | _ ->
                check {
                    let! items = state |> Checked.traverse (fun (_, name, t) -> this.Variable(e, at, name, t))
                    let! stateType = this.Type(at, stateSymbol)
                    return! builder.Tuple(at, stateType, items)
                }

        check {
            let! source, elementType, listType = this.Collection(env, at, shape)
            let! stateType = this.Type(at, stateSymbol)
            let! initial = stateValue env

            let direct =
                match state, statements with
                | [ symbol, _, _ ], [ only ] ->
                    match statementOperation only with
                    | :? ISimpleAssignmentOperation as assignment when (variableOf assignment.Target |> Option.exists (fun (s, _, _) -> SymbolEqualityComparer.Default.Equals(s, symbol))) ->
                        this.Eta(env, assignment.Value, [ variable :> ISymbol; symbol ])
                    | _ -> None
                | _ -> None

            let! fn =
                match direct with
                | Some fn -> fn
                | None ->
                    check {
                        let! statePattern =
                            match state with
                            | [ _, name, _ ] -> this.Binder(at, stateType, nameOf name)
                            | _ ->
                                check {
                                    let! items = state |> Checked.traverse (fun (_, name, t) -> this.Type(at, t) |> Checked.bind (fun itemType -> this.Binder(at, itemType, nameOf name)))
                                    return! builder.PatternTuple(at, stateType, items)
                                }

                        let! body = this.Ending(inner, stateSymbol, statements, at, stateValue)
                        let! stepType = this.Arrow(at, [ stateType ], stateType)
                        let! step = builder.Lambda(at, stepType, statePattern, body)
                        let! elementPattern = this.Binder(at, elementType, nameOf variable.Name)
                        let! fnType = this.Arrow(at, [ elementType; stateType ], stateType)
                        return! builder.Lambda(at, fnType, elementPattern, step)
                    }

            let! fnType = this.Arrow(at, [ elementType; stateType ], stateType)
            let! fold = this.Call(at, SdkMapping.sdk "List" "foldl", [ fnType, fn; stateType, initial; listType, source ], stateType)
            return! this.After(env, result, flow, at, [ for _, name, t in state -> name, t ], fold, rest, endAt)
        }

    /// <summary>
    /// A loop that can leave early, or a <c>while</c>, <c>do</c> or <c>for</c> loop: a <c>LetRecursion</c> whose parameters
    /// are the state, and for a <c>foreach</c> also the rest of the list. The end of the body calls the function again;
    /// a <c>return</c> and a <c>break</c> are base cases, and the statements after the loop are the base case of the exit.
    /// </summary>
    member private this.RecursionLoop(env: Env, result: ITypeSymbol, flow: Flow<'Value>, shape: LoopShape, rest: IOperation list, endAt) =
        let loop = shape.Loop
        let at = locate loop.Syntax
        let functionName = freshName loop.Syntax [ "loop" ]
        let state = [ for s in shape.State -> s.Name, typeOfSymbol s ]
        let statements = statementsOf loop.Body

        // The variables of a for initializer get their initial values here.
        let before: Checked<Env> =
            match loop with
            | :? IForLoopOperation as forLoop ->
                forLoop.Before
                |> List.ofSeq
                |> List.fold
                    (fun (acc: Checked<Env>) operation ->
                        acc
                        |> Checked.bind (fun (e: Env) ->
                            match statementOperation operation with
                            | :? IVariableDeclarationGroupOperation as group ->
                                [ for declaration in group.Declarations do yield! declaration.Declarators ]
                                |> List.fold
                                    (fun (e: Env) declarator ->
                                        match declarator.Initializer with
                                        | null -> e
                                        | initializer -> let current = e in Map.add declarator.Symbol.Name (remember (fun () -> this.Value(current, initializer.Value))) e)
                                    e
                                |> Checked.pass
                            | :? ISimpleAssignmentOperation as assignment ->
                                match variableOf assignment.Target with
                                | Some(_, name, _) -> Checked.pass (Map.add name (remember (fun () -> this.Value(e, assignment.Value))) e)
                                | None -> unsupported "mutation" at "A for initializer must declare or set a local."
                            | _ -> unsupported "loop" at "A for initializer must declare or set a local."))
                    (Checked.pass env)
            | _ -> Checked.pass env

        check {
            let! resultType = this.Type(at, result)
            let! stateTypes = state |> Checked.traverse (fun (_, t) -> this.Type(at, t))
            let! envBefore = before

            let! cursor =
                match loop with
                | :? IForEachLoopOperation ->
                    check {
                        let! source, elementType, listType = this.Collection(env, at, shape)
                        return Some(freshName loop.Syntax [ "remaining" ], source, elementType, listType)
                    }
                | _ -> Checked.pass None

            let parameterTypes = stateTypes @ (cursor |> Option.map (fun (_, _, _, listType) -> listType) |> Option.toList)
            let! functionType = this.Arrow(at, parameterTypes, resultType)

            let applyTo (arguments: 'Value list) =
                check {
                    let! fn = builder.Variable(at, functionType, functionName)

                    let rec go (current: 'Value) (remaining: ('Type * 'Value) list) =
                        match remaining with
                        | [] -> Checked.pass current
                        | (_, argument) :: tail ->
                            this.Arrow(at, tail |> List.map fst, resultType)
                            |> Checked.bind (fun inferred -> builder.Apply(at, inferred, current, argument))
                            |> Checked.bind (fun applied -> go applied tail)

                    return! go fn (List.zip parameterTypes arguments)
                }

            let call (e: Env) =
                check {
                    let! values = state |> Checked.traverse (fun (name, t) -> this.Variable(e, at, name, t))

                    let! next =
                        match cursor with
                        | Some(name, _, _, listType) -> builder.Variable(at, listType, name) |> Checked.map (fun v -> [ v ])
                        | None -> Checked.pass []

                    return! applyTo (values @ next)
                }

            let after (e: Env) = this.Flow(e, result, flow, rest, endAt)
            let inner = state |> List.fold (fun (e: Env) (name, _) -> Map.add name Plain e) env

            let body (e: Env) (finish: Env -> Checked<'Value>) =
                this.Flow(e, result, { End = Some finish; Continue = Some finish; Break = Some after }, statements, at)

            let condition (e: Env) (operation: IOperation) (whenTrue: Checked<'Value>) =
                if isNull operation || isConstantTrue operation then
                    whenTrue
                else
                    check {
                        let! tested = this.Value(e, operation)
                        let! yes = whenTrue
                        let! no = after e
                        return! builder.IfThenElse(at, resultType, tested, yes, no)
                    }

            let! functionBody =
                match loop, cursor with
                | (:? IWhileLoopOperation as whileLoop), _ when whileLoop.ConditionIsTop ->
                    condition inner whileLoop.Condition (body inner call)
                | (:? IWhileLoopOperation as doLoop), _ ->
                    // A do loop runs its body once, then tests its condition.
                    body inner (fun e -> condition e doLoop.Condition (call e))
                | (:? IForLoopOperation as forLoop), _ ->
                    let step (e: Env) =
                        this.Flow(e, result, { End = Some call; Continue = None; Break = None }, List.ofSeq forLoop.AtLoopBottom, at)

                    condition inner forLoop.Condition (body inner step)
                | _, Some(name, _, elementType, listType) ->
                    check {
                        let! empty = builder.PatternEmptyList(at, listType)
                        let! whenEmpty = after inner
                        let! head = this.Binder(at, elementType, nameOf shape.Variable.Name)
                        let! tail = this.Binder(at, listType, name)
                        let! headTail = builder.PatternHeadTail(at, listType, head, tail)
                        let! whenItem = body (Map.add shape.Variable.Name Plain inner) call
                        let! subject = builder.Variable(at, listType, name)
                        return! builder.PatternMatch(at, resultType, subject, [ empty, whenEmpty; headTail, whenItem ])
                    }
                | _ -> unsupported "loop" at "This loop is not in the Morphir subset."

            let inputs =
                [ for name, _ in state -> nameOf name ] @ (cursor |> Option.map (fun (name, _, _, _) -> name) |> Option.toList)

            let! definition = builder.ValueDefinition(at, List.zip inputs parameterTypes, resultType, functionBody)
            let! initial = state |> Checked.traverse (fun (name, t) -> this.Variable(envBefore, at, name, t))
            let! start = applyTo (initial @ (cursor |> Option.map (fun (_, source, _, _) -> source) |> Option.toList))
            return! builder.LetRecursion(at, resultType, [ functionName, definition ], start)
        }

    /// <summary>
    /// A deconstruction declaration such as <c>var (a, b) = t;</c>: a destructure of the tuple with a tuple pattern, and the
    /// statements after it as the body. Only a tuple can be deconstructed: the <c>Deconstruct</c> method of a record has
    /// no Morphir pattern.
    /// </summary>
    member private this.Deconstruction
        (env: Env, at, result: ITypeSymbol, flow: Flow<'Value>, deconstruction: IDeconstructionAssignmentOperation, rest: IOperation list, endAt)
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
                let! inValue = this.Flow(env', result, flow, rest, endAt)
                let! inferred = this.Type(at, result)
                return! builder.Destructure(at, inferred, pattern, subject, inValue)
            }

    /// <summary>
    /// A <c>switch</c> statement whose sections each end with a return: a pattern match. The arms keep the order of the
    /// cases, and the <c>default</c> section is the last arm, because C# tests it last. With no <c>default</c>, the
    /// statements after the switch are the last arm.
    /// </summary>
    member private this.SwitchStatement
        (env: Env, at, result: ITypeSymbol, flow: Flow<'Value>, switch: ISwitchOperation, rest: IOperation list, endAt)
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
                    this.Flow(armEnv, result, { flow with End = None }, List.ofSeq section.Body, sat)
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
                    let! value = this.Flow(env, result, flow, rest, endAt)
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
            loopLowering <- (OptionAttributes.ofMember method').LoopLowering |> Option.defaultValue LoopLowering.Traversal

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

                // The body is lowered also when a type is refused, so that each refusal of the method is reported.
                let! output = this.Type(returnAt, method'.ReturnType)
                and! lowered = body
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
