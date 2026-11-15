// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Emit

open System
open System.Collections.Generic
open System.Globalization
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Morphir.CodeGeneration.CSharp
open Morphir.IR
open TypeEmission
open ValueLowering

/// <summary>Writes the value definitions of a module as C# methods, from the typed lowering.</summary>
module internal Values =
    let private sdkRoot (name: string) = [ "Morphir"; "SDK"; "CSharp"; name ]

    /// <summary>A type syntax used as the target of a member access: a name is an expression.</summary>
    let private asExpression (t: TypeSyntax) : ExpressionSyntax = t :> ExpressionSyntax

    /// <summary>The expression, in parentheses unless it is already a primary expression.</summary>
    let private wrap (e: ExpressionSyntax) : ExpressionSyntax =
        match e with
        | :? IdentifierNameSyntax
        | :? MemberAccessExpressionSyntax
        | :? InvocationExpressionSyntax
        | :? ParenthesizedExpressionSyntax
        | :? ObjectCreationExpressionSyntax
        | :? LiteralExpressionSyntax
        | :? TupleExpressionSyntax
        | :? ElementAccessExpressionSyntax
        | :? TypeSyntax -> e
        | _ -> Cs.paren e

    /// <summary>A literal of the model as a C# expression.</summary>
    let literal (ct: System.Threading.CancellationToken) (value: LiteralEmission.Literal) : ExpressionSyntax =
        ct.ThrowIfCancellationRequested()
        let double' = asExpression (Cs.keywordType SyntaxKind.DoubleKeyword)

        match value with
        | LiteralEmission.Bool flag -> Cs.boolLiteral flag
        | LiteralEmission.Int number ->
            SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(int number)) :> ExpressionSyntax
        | LiteralEmission.Decimal number ->
            SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal number) :> ExpressionSyntax
        | LiteralEmission.Float number when Double.IsNaN number -> Cs.access double' "NaN"
        | LiteralEmission.Float number when Double.IsPositiveInfinity number -> Cs.access double' "PositiveInfinity"
        | LiteralEmission.Float number when Double.IsNegativeInfinity number -> Cs.access double' "NegativeInfinity"
        | LiteralEmission.Float number when number = 0.0 && BitConverter.DoubleToInt64Bits number < 0L ->
            SF.PrefixUnaryExpression(
                SyntaxKind.UnaryMinusExpression,
                SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal("0.0d", 0.0))
            )
            :> ExpressionSyntax
        | LiteralEmission.Float number ->
            let text = number.ToString("R", CultureInfo.InvariantCulture)
            // The suffix keeps a whole number a double.
            SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(text + "d", number)) :> ExpressionSyntax
        | LiteralEmission.String text -> Cs.stringLiteral text
        | LiteralEmission.Char c ->
            SF.LiteralExpression(SyntaxKind.CharacterLiteralExpression, SF.Literal c) :> ExpressionSyntax

    /// <summary>The state of the emission of one value definition.</summary>
    type private Scope(context: Context, checkedValue: Checked) =
        let used = HashSet<string>(StringComparer.Ordinal)

        /// <summary>The local functions of the method: the C# name, the input shapes and the output shape.</summary>
        member val Functions = Dictionary<string, Shape list * Shape>(StringComparer.Ordinal)

        member _.Context = context
        member _.Checked = checkedValue
        member _.Location = checkedValue.Top.Location
        member _.Ct = context.Ct

        /// <summary>A name for a local that no other local of the method has.</summary>
        member _.Fresh(hint: string) : string =
            let rec pick count =
                let text = if count = 0 then hint else hint + "_" + string (count + 1)
                if used.Add text then text else pick (count + 1)

            pick 0

        /// <summary>The C# name of a local of the model: camel case, and unique in the method.</summary>
        member this.Local(name: Name) : string =
            let (Name words) = name

            let camel =
                match words with
                | [] -> "value"
                | first :: rest ->
                    let tail = rest |> List.map (fun w -> if w = "" then "" else w.Substring(0, 1).ToUpperInvariant() + w.Substring 1)
                    let text = first.ToLowerInvariant() + String.concat "" tail
                    if text.Length > 0 && Char.IsAsciiDigit text[0] then "_" + text else text

            this.Fresh camel

        member _.Type(shape: Shape) : TypeSyntax =
            TypeSyntaxes.render context checkedValue.Top.Location checkedValue.Parameters Set.empty (checkedValue.CheckedType shape)

        member _.Resolve(shape: Shape) : Shape = checkedValue.Resolve shape

    type private Env = Map<Name, string>

    let private funcOf (inputs: TypeSyntax list) (output: TypeSyntax) : TypeSyntax =
        Cs.globalGeneric [ "System"; "Func" ] (inputs @ [ output ])

    /// <summary>A block that computes a value, called at once: the form of a statement-bodied expression.</summary>
    let private immediately (output: TypeSyntax) (statements: StatementSyntax list) : ExpressionSyntax =
        Cs.invoke
            (Cs.access (Cs.newObject (funcOf [] output) [ Cs.lambdaNoArgs (Cs.block statements) ]) "Invoke")
            []

    let private caseType (scope: Scope) (candidate: Constructor) (arguments: Shape list) : TypeSyntax =
        Cs.globalGeneric (TypeSyntaxes.caseSegments scope.Context candidate.Owner candidate.Case) (arguments |> List.map scope.Type)

    let private comparer (scope: Scope) (shape: Shape) : ExpressionSyntax =
        let rec go shape =
            let comparers = asExpression (Cs.globalGeneric (sdkRoot "MorphirComparers") [])

            match shape with
            | Named(name, []) when sdk name = Some("basics", "int") -> Cs.access comparers "Int"
            | Named(name, []) when sdk name = Some("basics", "float") -> Cs.access comparers "Float"
            | Named(name, []) when sdk name = Some("char", "char") -> Cs.access comparers "Char"
            | Named(name, []) when sdk name = Some("string", "string") -> Cs.access comparers "String"
            | Named(name, [ item ]) when sdk name = Some("list", "list") -> Cs.invoke (Cs.access comparers "List") [ go item ]
            | Product items when List.length items = 2 || List.length items = 3 ->
                Cs.invoke (Cs.access comparers "Tuple") (List.map go items)
            | _ ->
                Admission.fail "unsupported-comparable" scope.Location "No structural SDK comparer exists for this key type."

        go shape

    /// <summary>The call of an SDK operation with its arguments.</summary>
    let private sdkCall (scope: Scope) (operation: SdkMapping.Operation) (args: ExpressionSyntax list) : ExpressionSyntax =
        let instance index inputs = Cs.invoke (Cs.access (wrap args[index]) operation.Method) inputs
        let factory inputs = Cs.invoke (Cs.access (asExpression (scope.Type operation.Output)) operation.Method) inputs

        match operation.Module, operation.Method with
        | ("Basics" | "Decimal"), methodName ->
            Cs.invoke (Cs.access (asExpression (Cs.globalGeneric (sdkRoot operation.Module) [])) methodName) args
        | "List", "Singleton" -> factory args
        | "List", "Foldl" -> instance 2 [ args[0]; args[1] ]
        | "List", "Map"
        | "List", "Filter"
        | "List", "FilterMap"
        | "Maybe", _
        | "Result", _
        | "List", "Cons" -> instance 1 [ args[0] ]
        | "List", "Length" -> Cs.access (wrap args[0]) "Length"
        | "List", "Head"
        | "List", "Tail"
        | "Dict", "ToList"
        | "Set", "ToList" -> instance 0 []
        | ("Dict" | "Set"), ("Empty" | "Singleton" | "FromEnumerable") ->
            let key =
                match scope.Resolve operation.Output with
                | Named(_, key :: _) -> key
                | _ -> Admission.fail "type-mismatch" scope.Location "A collection factory requires its concrete output type."

            factory (args @ [ comparer scope key ])
        | "Dict", "Get"
        | "Dict", "Remove"
        | "Set", "Insert"
        | "Set", "Remove" -> instance 1 [ args[0] ]
        | "Dict", "Insert"
        | "Dict", "Update" -> instance 2 [ args[0]; args[1] ]
        | _ -> instance 0 [ args[1] ]

    /// <summary>A function value of the inputs and the output, which calls <paramref name="call"/> when all inputs are given.</summary>
    let private curry (scope: Scope) (inputs: Shape list) (output: Shape) (call: ExpressionSyntax list -> ExpressionSyntax) : ExpressionSyntax =
        match inputs with
        | [] -> call []
        | _ ->
            let names = inputs |> List.map (fun _ -> scope.Fresh "arg")
            let pairs = List.zip inputs names

            // The output of the step for the last input is the output of the function; of an earlier step, a function.
            let rec outputs (remaining: Shape list) =
                match remaining with
                | [] -> output
                | input :: rest -> Arrow(input, outputs rest)

            let rec loop (remaining: (Shape * string) list) =
                match remaining with
                | [] -> call (names |> List.map (fun n -> Cs.id n :> ExpressionSyntax))
                | (input, name) :: rest ->
                    let result = outputs (rest |> List.map fst)
                    Cs.newObject (funcOf [ scope.Type input ] (scope.Type result)) [ Cs.lambda name (loop rest) ]

            loop pairs

    /// <summary>The names of the fields of the record that a record shape is written as, in the order of its declaration.</summary>
    let private recordFieldsOf (scope: Scope) (shape: Shape) : Name list =
        let resolved = scope.Resolve shape

        match resolved with
        | Fields _ ->
            let owner, _ = recordTarget scope.Ct scope.Context.Catalog scope.Location (scope.Checked.CheckedType resolved)

            match owner.Definition with
            | Alias(Fields declared) -> declared |> List.map fst
            | _ -> Admission.fail "type-mismatch" scope.Location "The selected record target is not a record."
        | _ -> Admission.fail "type-mismatch" scope.Location "A closed record is required."

    /// <summary>The types of the fields of a constructor, the type that it builds, and how to build it from the values.</summary>
    let private constructorParts (scope: Scope) (candidate: Constructor) (args: Shape list) =
        let substitutions = List.zip candidate.Owner.Parameters args |> Map.ofList
        let fields = candidate.Fields |> List.map (fun (name, shape) -> name, substitute scope.Ct substitutions shape)
        let output = if candidate.IsRecord then Fields fields else Named(candidate.Owner.Name, args)

        let build (values: ExpressionSyntax list) : ExpressionSyntax =
            if candidate.IsRecord then
                Cs.newObject (scope.Type output) values
            elif candidate.Runtime.IsSome then
                let target = Cs.access (asExpression (scope.Type output)) candidate.Runtime.Value
                if List.isEmpty values then target else Cs.invoke target values
            elif TypeSyntaxes.isUnion candidate.Owner then
                Cs.cast (scope.Type output) (wrap (List.head values))
            else
                Cs.newObject (caseType scope candidate args) values

        List.map snd fields, output, build

    /// <summary>The pieces of a pattern for a switch expression, or none when the pattern needs the long form.</summary>
    let rec private switchPattern (scope: Scope) (env: Env) (p: TypedPattern) : (PatternSyntax * Env) option =
        scope.Ct.ThrowIfCancellationRequested()
        let many (patterns: TypedPattern list) =
            let rec go acc env remaining =
                match remaining with
                | [] -> Some(List.rev acc, env)
                | item :: rest ->
                    match switchPattern scope env item with
                    | Some(converted, env') -> go (converted :: acc) env' rest
                    | None -> None

            go [] env patterns

        let positional (subs: PatternSyntax list) =
            SF.PositionalPatternClause(SF.SeparatedList<SubpatternSyntax>(subs |> List.map (fun s -> SF.Subpattern s)))

        match p.Pattern with
        | TWild
        | TUnitPattern -> Some(SF.DiscardPattern() :> PatternSyntax, env)
        | TAs({ Pattern = TWild }, name) ->
            let local = scope.Local name
            Some(SF.VarPattern(SF.SingleVariableDesignation(Cs.token local)) :> PatternSyntax, Map.add name local env)
        | TAs(inner, name) ->
            switchPattern scope env inner
            |> Option.map (fun (converted, env') ->
                let local = scope.Local name
                SF.BinaryPattern(SyntaxKind.AndPattern, converted, SF.VarPattern(SF.SingleVariableDesignation(Cs.token local))) :> PatternSyntax,
                Map.add name local env')
        | TLiteralPattern(LiteralEmission.Float _) -> None
        | TLiteralPattern lit -> Some(SF.ConstantPattern(literal scope.Ct lit) :> PatternSyntax, env)
        | TTuplePattern items ->
            many items
            |> Option.map (fun (subs, env') -> SF.RecursivePattern().WithPositionalPatternClause(positional subs) :> PatternSyntax, env')
        | TEmptyList
        | THeadTail _ -> None
        | TConstructorPattern(candidate, _, _) when candidate.Runtime.IsSome -> None
        | TConstructorPattern(candidate, args, items) ->
            if candidate.IsRecord then
                many items
                |> Option.map (fun (subs, env') ->
                    SF.RecursivePattern(scope.Type p.Type, positional subs, null, null) :> PatternSyntax, env')
            elif TypeSyntaxes.isUnion candidate.Owner then
                let wrapped = scope.Type (snd (List.head candidate.Fields) |> substitute scope.Ct (List.zip candidate.Owner.Parameters args |> Map.ofList))

                match items with
                | [ { Pattern = TAs({ Pattern = TWild }, name) } ] ->
                    let local = scope.Local name
                    Some(SF.DeclarationPattern(wrapped, SF.SingleVariableDesignation(Cs.token local)) :> PatternSyntax, Map.add name local env)
                | [ { Pattern = TWild } ] -> Some(SF.TypePattern(wrapped) :> PatternSyntax, env)
                | [ inner ] ->
                    switchPattern scope env inner
                    |> Option.map (fun (converted, env') -> SF.BinaryPattern(SyntaxKind.AndPattern, SF.TypePattern(wrapped), converted) :> PatternSyntax, env')
                | _ -> None
            else
                let target = caseType scope candidate args

                if List.isEmpty items then
                    Some(SF.TypePattern(target) :> PatternSyntax, env)
                else
                    many items
                    |> Option.map (fun (subs, env') -> SF.RecursivePattern(target, positional subs, null, null) :> PatternSyntax, env')

    /// <summary>Whether a pattern matches every value of its type.</summary>
    let rec private irrefutable (p: TypedPattern) : bool =
        match p.Pattern with
        | TWild
        | TUnitPattern -> true
        | TAs(inner, _) -> irrefutable inner
        | TTuplePattern items -> List.forall irrefutable items
        | TConstructorPattern(candidate, _, items) when candidate.IsRecord -> List.forall irrefutable items
        | _ -> false

    /// <summary>The value expressions that a typed expression holds directly.</summary>
    let private childrenOf (v: Typed) : Typed list =
        match v.Node with
        | TLiteral _
        | TVariable _
        | TUnit
        | TFieldFunction _
        | TConstructor _
        | TSdk _
        | TReference _ -> []
        | TApply(f, x) -> [ f; x ]
        | TLambda(_, _, body)
        | TPatternLambda(_, _, body) -> [ body ]
        | TLet(_, binding, rest) -> [ binding; rest ]
        | TLetRec(definitions, rest) -> (definitions |> List.map (fun (_, d) -> d.LocalBody)) @ [ rest ]
        | TTuple items
        | TList items -> items
        | TRecord fields -> fields |> List.map snd
        | TField(target, _) -> [ target ]
        | TUpdate(target, fields) -> target :: (fields |> List.map snd)
        | TIf(c, a, b) -> [ c; a; b ]
        | TMatch(target, cases) -> target :: (cases |> List.map snd)
        | TDestructure(_, target, body) -> [ target; body ]

    /// <summary>Whether the expression names the variable. A local that nothing names gets a discard, so the compiler stays quiet.</summary>
    let rec private uses (name: Name) (v: Typed) : bool =
        match v.Node with
        | TVariable n when n = name -> true
        | _ -> childrenOf v |> List.exists (uses name)

    let rec private expression (scope: Scope) (env: Env) (value: Typed) : ExpressionSyntax =
        scope.Ct.ThrowIfCancellationRequested()
        let expr = expression scope env
        let typ = scope.Type

        match value.Node with
        | TLiteral lit -> literal scope.Ct lit
        | TVariable name ->
            let local = Map.find name env

            match scope.Functions.TryGetValue local with
            | true, (inputs, output) -> curry scope inputs output (fun values -> Cs.invoke (Cs.id local) values)
            | _ -> Cs.id local :> ExpressionSyntax
        | TUnit -> Cs.access (asExpression (Cs.globalGeneric (sdkRoot "Unit") [])) "Value"
        | TTuple items -> Cs.tuple (List.map expr items)
        | TList items ->
            match scope.Resolve value.Type with
            | Named(_, [ item ]) ->
                let target = asExpression (Cs.globalGeneric (sdkRoot "List") [ typ item ])

                if List.isEmpty items then
                    Cs.access target "Empty"
                else
                    let array =
                        SF.ArrayCreationExpression(
                            SF.ArrayType(typ item).WithRankSpecifiers(SF.SingletonList(SF.ArrayRankSpecifier(SF.SingletonSeparatedList<ExpressionSyntax>(SF.OmittedArraySizeExpression())))),
                            SF.InitializerExpression(SyntaxKind.ArrayInitializerExpression, SF.SeparatedList<ExpressionSyntax>(List.map expr items))
                        )

                    Cs.invoke (Cs.access target "FromEnumerable") [ array ]
            | _ -> Admission.fail "type-mismatch" scope.Location "A list expression has an inconsistent type."
        | TConstructor(candidate, args) ->
            let fieldShapes, output, build = constructorParts scope candidate args
            curry scope fieldShapes output build
        | TRecord fields -> recordExpression scope env value fields
        | TField(target, name) -> Cs.access (wrap (expr target)) (title name)
        | TFieldFunction name ->
            match scope.Resolve value.Type with
            | Arrow(input, output) ->
                let parameter = scope.Fresh "item"
                Cs.newObject (funcOf [ typ input ] (typ output)) [ Cs.lambda parameter (Cs.access (Cs.id parameter) (title name)) ]
            | _ -> Admission.fail "type-mismatch" scope.Location "A field function has an inconsistent type."
        | TUpdate(target, fields) ->
            let assignments =
                fields
                |> List.map (fun (name, v) ->
                    SF.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, Cs.id (title name), expr v) :> ExpressionSyntax)

            SF.WithExpression(wrap (expr target), SF.InitializerExpression(SyntaxKind.WithInitializerExpression, SF.SeparatedList<ExpressionSyntax>(assignments)))
            :> ExpressionSyntax
        | TIf(condition, yes, no) -> SF.ConditionalExpression(wrap (expr condition), wrap (expr yes), wrap (expr no)) :> ExpressionSyntax
        | TMatch(target, cases) -> matchExpression scope env value target cases
        | TLet _
        | TLetRec _
        | TDestructure _ -> immediately (typ value.Type) (tail scope env value)
        | TPatternLambda(p, input, body) ->
            let subject = scope.Fresh "input"
            let condition, bindings, scoped = patternCondition scope env (Cs.id subject) p

            let guard =
                match condition with
                | Some c -> [ SF.IfStatement(SF.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, Cs.paren c), Cs.throwStatement [ "System"; "InvalidOperationException" ] "Morphir argument pattern failed.") :> StatementSyntax ]
                | None -> []

            let statements = guard @ bindings @ tail scope scoped body
            Cs.newObject (funcOf [ typ input ] (typ body.Type)) [ Cs.lambdaBlock subject (Cs.block statements) ]
        | TApply _ -> applyExpression scope env value
        | TLambda(name, input, body) ->
            let parameter = scope.Local name
            Cs.newObject (funcOf [ typ input ] (typ body.Type)) [ Cs.lambda parameter (expression scope (Map.add name parameter env) body) ]
        | TSdk operation -> curry scope operation.Inputs operation.Output (sdkCall scope operation)
        | TReference(target, args) ->
            let inputs, output, call = referenceCall scope target args
            curry scope inputs output call

    and private referenceCall (scope: Scope) (target: Top) (args: Shape list) =
        let definition = target.Definition
        let signature = List.foldBack (fun (_, input) output -> Arrow(input, output)) definition.Inputs definition.Output
        let freeNames = freeParameters scope.Ct signature |> Set.toList
        let substitutions = List.zip freeNames args |> Map.ofList
        let inputs = definition.Inputs |> List.map (snd >> substitute scope.Ct substitutions)
        let output = substitute scope.Ct substitutions definition.Output
        let segments = ModuleLayouts.qualified scope.Context.Layouts target.Name
        let method' = asExpression (Cs.globalGeneric segments (args |> List.map scope.Type))
        inputs, output, (fun values -> Cs.invoke method' values)

    and private recordExpression (scope: Scope) (env: Env) (value: Typed) (fields: (Name * Typed) list) : ExpressionSyntax =
        let declared = recordFieldsOf scope value.Type

        if List.map fst fields = declared then
            Cs.newObject (scope.Type value.Type) (fields |> List.map (snd >> expression scope env))
        else
            // The fields are evaluated in the order that the model wrote them, then given in the order of the record.
            let temporaries = fields |> List.map (fun (name, v) -> name, scope.Fresh(scope.Local name), v)
            let statements = temporaries |> List.map (fun (_, temp, v) -> Cs.local (scope.Type v.Type) temp (expression scope env v))
            let values = declared |> List.map (fun name -> let _, temp, _ = temporaries |> List.find (fun (n, _, _) -> n = name) in Cs.id temp :> ExpressionSyntax)
            immediately (scope.Type value.Type) (statements @ [ Cs.returns (Cs.newObject (scope.Type value.Type) values) ])

    and private applyExpression (scope: Scope) (env: Env) (value: Typed) : ExpressionSyntax =
        let rec spine (v: Typed) (acc: Typed list) =
            match v.Node with
            | TApply(f, x) -> spine f (x :: acc)
            | _ -> v, acc

        let head, args = spine value []
        let values = args |> List.map (expression scope env)

        let rec invokeAll (target: ExpressionSyntax) (remaining: ExpressionSyntax list) =
            match remaining with
            | [] -> target
            | x :: rest -> invokeAll (Cs.invoke (Cs.access (wrap target) "Invoke") [ x ]) rest

        let saturated (count: int) (call: ExpressionSyntax list -> ExpressionSyntax) =
            if count > 0 && List.length values >= count then
                Some(invokeAll (call (List.take count values)) (List.skip count values))
            else
                None

        let direct =
            match head.Node with
            | TReference(target, tyArgs) ->
                let inputs, _, call = referenceCall scope target tyArgs

                if List.isEmpty inputs then
                    Some(invokeAll (call []) values)
                else
                    saturated (List.length inputs) call
            | TConstructor(candidate, tyArgs) ->
                let fieldShapes, _, build = constructorParts scope candidate tyArgs
                saturated (List.length fieldShapes) build
            | TSdk operation -> saturated (List.length operation.Inputs) (sdkCall scope operation)
            | TVariable name ->
                match scope.Functions.TryGetValue(Map.find name env) with
                | true, (inputs, _) when not (List.isEmpty inputs) ->
                    let local = Map.find name env
                    saturated (List.length inputs) (fun values -> Cs.invoke (Cs.id local) values)
                | _ -> None
            | _ -> None

        match direct with
        | Some result -> result
        | None -> invokeAll (expression scope env head) values

    and private matchExpression (scope: Scope) (env: Env) (value: Typed) (target: Typed) (cases: (TypedPattern * Typed) list) : ExpressionSyntax =
        let subject = expression scope env target

        let converted =
            let rec go acc remaining =
                match remaining with
                | [] -> Some(List.rev acc)
                | (p, body) :: rest ->
                    match switchPattern scope env p with
                    | Some(pattern, scoped) -> go ((pattern, scoped, body, p) :: acc) rest
                    | None -> None

            go [] cases

        // An arm after an arm that takes every value, or after arms that already name every case, can never match.
        // C# refuses it, so it is not written.
        let reachable (arms: (PatternSyntax * Env * Typed * TypedPattern) list) =
            let rec go (acc: _ list) (remaining: _ list) =
                match remaining with
                | [] -> List.rev acc
                | ((_, _, _, p) as arm) :: rest ->
                    let taken = List.rev (arm :: acc)

                    if irrefutable p || coversAllCases scope target taken then List.rev (arm :: acc) else go (arm :: acc) rest

            go [] arms

        match converted |> Option.map reachable with
        | Some arms ->
            let armSyntax =
                arms
                |> List.map (fun (pattern, scoped, body, _) ->
                    SF.SwitchExpressionArm(pattern, bodyExpression scope scoped body))

            // Without an arm that takes every value, the switch ends with the failure of the model.
            let exhaustive = arms |> List.exists (fun (_, _, _, p) -> irrefutable p) || coversAllCases scope target arms
            let last =
                if exhaustive then []
                else [ SF.SwitchExpressionArm(SF.DiscardPattern(), Cs.throwNew [ "System"; "InvalidOperationException" ] "Non-exhaustive Morphir pattern.") ]

            SF.SwitchExpression(wrap subject, SF.SeparatedList<SwitchExpressionArmSyntax>(armSyntax @ last)) :> ExpressionSyntax
        | None ->
            let name = scope.Fresh "subject"
            let rec branches remaining : StatementSyntax list =
                match remaining with
                | [] -> [ Cs.throwStatement [ "System"; "InvalidOperationException" ] "Non-exhaustive Morphir pattern." ]
                | (p, body) :: rest ->
                    let condition, bindings, scoped = patternCondition scope env (Cs.id name) p
                    let inner = bindings @ tail scope scoped body

                    match condition with
                    | None -> inner
                    | Some condition -> (SF.IfStatement(condition, Cs.block inner) :> StatementSyntax) :: branches rest

            immediately (scope.Type value.Type) ([ Cs.local (scope.Type target.Type) name subject ] @ branches cases)

    /// <summary>Whether the arms name every case of the custom type that the subject has, each with patterns that take any value.</summary>
    and private coversAllCases (scope: Scope) (target: Typed) arms : bool =
        match scope.Resolve target.Type with
        | Named(name, _) ->
            match Map.tryFind name scope.Context.Catalog with
            | Some { Definition = Cases(_, cases) } ->
                let covered =
                    arms
                    |> List.choose (fun (_, _, _, (p: TypedPattern)) ->
                        match p.Pattern with
                        | TConstructorPattern(candidate, _, items) when List.forall irrefutable items -> Some candidate.Case
                        | _ -> None)
                    |> Set.ofList

                cases |> List.forall (fun (caseName, _) -> Set.contains caseName covered)
            | _ -> false
        | _ -> false

    /// <summary>The body of an arm: an expression, or a block when the body needs statements.</summary>
    and private bodyExpression (scope: Scope) (env: Env) (body: Typed) : ExpressionSyntax = expression scope env body

    /// <summary>The conditions, the statements that bind names and the extended scope of a pattern, for the long form.</summary>
    and private patternCondition (scope: Scope) (env: Env) (target: ExpressionSyntax) (p: TypedPattern) : ExpressionSyntax option * StatementSyntax list * Env =
        scope.Ct.ThrowIfCancellationRequested()
        let typ = scope.Type

        let combine (conditions: ExpressionSyntax option list) : ExpressionSyntax option =
            match List.choose id conditions with
            | [] -> None
            | first :: rest ->
                Some(rest |> List.fold (fun (left: ExpressionSyntax) right -> SF.BinaryExpression(SyntaxKind.LogicalAndExpression, left, right) :> ExpressionSyntax) first)

        let many (parts: (TypedPattern * ExpressionSyntax) list) =
            parts
            |> List.fold
                (fun (conditions, statements, env) (item, subTarget) ->
                    let condition, bindings, env' = patternCondition scope env subTarget item
                    conditions @ [ condition ], statements @ bindings, env')
                ([], [], env)

        let rec tupleItem (t: ExpressionSyntax) (index: int) : ExpressionSyntax =
            if index < 7 then Cs.access (wrap t) ("Item" + string (index + 1)) else tupleItem (Cs.access (wrap t) "Rest") (index - 7)

        match p.Pattern with
        | TWild
        | TUnitPattern -> None, [], env
        | TAs(inner, name) ->
            let condition, bindings, env' = patternCondition scope env target inner
            let local = scope.Local name
            condition, bindings @ [ Cs.local (typ p.Type) local target; Cs.unused local ], Map.add name local env'
        | TLiteralPattern lit ->
            let equal = Cs.access (asExpression (Cs.globalGeneric (sdkRoot "Basics") [])) "Equal"
            Some(Cs.invoke equal [ target; literal scope.Ct lit ]), [], env
        | TTuplePattern items ->
            let conditions, bindings, env' = many (items |> List.mapi (fun i item -> item, tupleItem target i))
            combine conditions, bindings, env'
        | TEmptyList ->
            Some(SF.BinaryExpression(SyntaxKind.EqualsExpression, Cs.access (wrap target) "Length", Cs.intLiteral 0) :> ExpressionSyntax), [], env
        | THeadTail(head, tail') ->
            let rest =
                Cs.invoke
                    (Cs.access (asExpression (typ p.Type)) "FromEnumerable")
                    [ Cs.invoke (Cs.access (asExpression (Cs.globalGeneric [ "System"; "Linq"; "Enumerable" ] [])) "Skip") [ target; Cs.intLiteral 1 ] ]

            let conditions, bindings, env' =
                many [ head, (SF.ElementAccessExpression(wrap target, SF.BracketedArgumentList(SF.SingletonSeparatedList(SF.Argument(Cs.intLiteral 0)))) :> ExpressionSyntax); tail', rest ]

            let nonEmpty = SF.BinaryExpression(SyntaxKind.GreaterThanExpression, Cs.access (wrap target) "Length", Cs.intLiteral 0) :> ExpressionSyntax
            combine (Some nonEmpty :: conditions), bindings, env'
        | TConstructorPattern(candidate, args, items) when candidate.Runtime.IsSome ->
            let methodName = candidate.Runtime.Value
            let variable = scope.Fresh "payload"
            let failure = Cs.throwNew [ "System"; "InvalidOperationException" ] "Morphir constructor pattern failed."
            let order = asExpression (Cs.globalGeneric (sdkRoot "Order") [])
            let equal = Cs.access (asExpression (Cs.globalGeneric (sdkRoot "Basics") [])) "Equal"
            let notOf (e: ExpressionSyntax) = SF.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, e) :> ExpressionSyntax
            let match' (first: ExpressionSyntax) (second: ExpressionSyntax) = Cs.invoke (Cs.access (wrap target) "Match") [ first; second ]

            let extraction, condition =
                match methodName with
                | "LT"
                | "EQ"
                | "GT" -> target, Cs.invoke equal [ target; Cs.access order methodName ]
                | "Just" -> match' (Cs.lambda variable (Cs.id variable)) (SF.ParenthesizedLambdaExpression(failure) :> ExpressionSyntax), Cs.access (wrap target) "IsJust"
                | "Nothing" -> target, notOf (Cs.access (wrap target) "IsJust")
                | "Ok" -> match' (Cs.lambda variable failure) (Cs.lambda variable (Cs.id variable)), Cs.access (wrap target) "IsOk"
                | "Err" -> match' (Cs.lambda variable (Cs.id variable)) (Cs.lambda variable failure), notOf (Cs.access (wrap target) "IsOk")
                | _ -> Admission.fail "unmapped-sdk" scope.Location "The SDK constructor pattern has no binding."

            let conditions, bindings, env' = many (items |> List.map (fun item -> item, extraction))
            combine (Some condition :: conditions), bindings, env'
        | TConstructorPattern(candidate, args, items) ->
            if candidate.IsRecord then
                let subjects = candidate.Fields |> List.map (fun (name, _) -> Cs.access (wrap target) (title name))
                let conditions, bindings, env' = many (List.zip items subjects)
                combine conditions, bindings, env'
            else
                let caseTypeSyntax = if TypeSyntaxes.isUnion candidate.Owner then scope.Type (snd (List.head candidate.Fields) |> substitute scope.Ct (List.zip candidate.Owner.Parameters args |> Map.ofList)) else caseType scope candidate args
                let local = scope.Fresh "case"
                let tag = SF.IsPatternExpression(wrap target, SF.DeclarationPattern(caseTypeSyntax, SF.SingleVariableDesignation(Cs.token local))) :> ExpressionSyntax

                let subjects =
                    if TypeSyntaxes.isUnion candidate.Owner then [ Cs.id local :> ExpressionSyntax ]
                    else candidate.Fields |> List.map (fun (name, _) -> Cs.access (Cs.id local) (title name))

                let conditions, bindings, env' = many (List.zip items subjects)
                combine (Some tag :: conditions), bindings, env'

    /// <summary>The statements of a value in the position of a result: lets become locals, and the last step returns.</summary>
    and private tail (scope: Scope) (env: Env) (value: Typed) : StatementSyntax list =
        scope.Ct.ThrowIfCancellationRequested()

        match value.Node with
        | TLet(name, binding, rest) ->
            let local = scope.Local name
            let initial = expression scope env binding
            Cs.local (scope.Type binding.Type) local initial
            :: ((if uses name rest then [] else [ Cs.unused local ]) @ tail scope (Map.add name local env) rest)
        | TDestructure(p, target, body) ->
            let subject = scope.Fresh "subject"
            let condition, bindings, scoped = patternCondition scope env (Cs.id subject) p

            let guard =
                match condition with
                | Some c -> [ SF.IfStatement(SF.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, Cs.paren c), Cs.throwStatement [ "System"; "InvalidOperationException" ] "Morphir destructuring failed.") :> StatementSyntax ]
                | None -> []

            [ Cs.local (scope.Type target.Type) subject (expression scope env target) ] @ guard @ bindings @ tail scope scoped body
        | TLetRec(definitions, rest) ->
            // Every function of the group is known to every body, so the names are bound before the bodies are written.
            let names = definitions |> List.map (fun (name, definition) -> name, scope.Local name, definition)

            let groupEnv =
                names |> List.fold (fun (acc: Env) (name, local, definition) ->
                    scope.Functions[local] <- (definition.LocalInputs |> List.map snd, definition.LocalOutput)
                    Map.add name local acc) env

            // A function that nothing calls is not written.
            let isUsed (name: Name) = uses name rest || (definitions |> List.exists (fun (_, d) -> uses name d.LocalBody))

            let functions =
                names
                |> List.filter (fun (name, _, _) -> isUsed name)
                |> List.map (fun (name, local, definition) -> localFunction scope groupEnv name local definition)

            functions @ tail scope groupEnv rest
        | _ -> [ Cs.returns (expression scope env value) ]

    /// <summary>
    /// A local function. A function that only calls itself, and only as its last step, becomes a loop that replaces its
    /// parameters and goes round again: no stack grows. Any other function is a plain local function.
    /// </summary>
    and private localFunction (scope: Scope) (env: Env) (name: Name) (local: string) (definition: TypedDefinition) : StatementSyntax =
        let inputs = definition.LocalInputs |> List.map (fun (inputName, shape) -> inputName, shape, scope.Local inputName)
        let bodyEnv = inputs |> List.fold (fun (acc: Env) (inputName, _, ident) -> Map.add inputName ident acc) env
        let parameters = inputs |> List.map (fun (_, shape, ident) -> SF.Parameter(Cs.token ident).WithType(scope.Type shape))

        let body: StatementSyntax list =
            if isTailLoop name (List.length inputs) definition.LocalBody then
                [ SF.WhileStatement(Cs.boolLiteral true, Cs.block (loopTail scope bodyEnv name (inputs |> List.map (fun (_, _, ident) -> ident)) definition.LocalBody)) :> StatementSyntax ]
            else
                tail scope bodyEnv definition.LocalBody

        SF.LocalFunctionStatement(scope.Type definition.LocalOutput, Cs.token local)
            .WithParameterList(SF.ParameterList(SF.SeparatedList<ParameterSyntax>(parameters)))
            .WithBody(Cs.block body)
        :> StatementSyntax

    /// <summary>The statements of a loop body: a call of the function itself as the last step replaces the parameters and continues.</summary>
    and private loopTail (scope: Scope) (env: Env) (self: Name) (parameters: string list) (value: Typed) : StatementSyntax list =
        scope.Ct.ThrowIfCancellationRequested()
        let recurse = loopTail scope

        let rec spine (v: Typed) (acc: Typed list) =
            match v.Node with
            | TApply(f, x) -> spine f (x :: acc)
            | _ -> v, acc

        match value.Node with
        | TIf(condition, yes, no) ->
            [ SF.IfStatement(expression scope env condition, Cs.block (recurse env self parameters yes), SF.ElseClause(Cs.block (recurse env self parameters no))) :> StatementSyntax ]
        | TLet(name, binding, rest) ->
            let local = scope.Local name
            Cs.local (scope.Type binding.Type) local (expression scope env binding)
            :: ((if uses name rest then [] else [ Cs.unused local ]) @ recurse (Map.add name local env) self parameters rest)
        | TDestructure(p, target, body) ->
            let subject = scope.Fresh "subject"
            let condition, bindings, scoped = patternCondition scope env (Cs.id subject) p

            let guard =
                match condition with
                | Some c -> [ SF.IfStatement(SF.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, Cs.paren c), Cs.throwStatement [ "System"; "InvalidOperationException" ] "Morphir destructuring failed.") :> StatementSyntax ]
                | None -> []

            [ Cs.local (scope.Type target.Type) subject (expression scope env target) ] @ guard @ bindings @ recurse scoped self parameters body
        | TMatch(target, cases) ->
            let subject = scope.Fresh "subject"

            let rec branches remaining : StatementSyntax list =
                match remaining with
                | [] -> [ Cs.throwStatement [ "System"; "InvalidOperationException" ] "Non-exhaustive Morphir pattern." ]
                | (p, body) :: rest ->
                    let condition, bindings, scoped = patternCondition scope env (Cs.id subject) p
                    let inner = bindings @ recurse scoped self parameters body

                    match condition with
                    | None -> inner
                    | Some c -> (SF.IfStatement(c, Cs.block inner) :> StatementSyntax) :: branches rest

            [ Cs.local (scope.Type target.Type) subject (expression scope env target) ] @ branches cases
        | TApply _ ->
            let head, args = spine value []

            match head.Node with
            | TVariable name when name = self && List.length args = List.length parameters ->
                // The new values are all computed before any parameter changes.
                let temporaries = args |> List.map (fun arg -> scope.Fresh "next", arg)
                let computes = temporaries |> List.map (fun (temp, arg) -> Cs.local (scope.Type arg.Type) temp (expression scope env arg))

                let assigns =
                    List.zip parameters temporaries
                    |> List.map (fun (parameter, (temp, _)) ->
                        SF.ExpressionStatement(SF.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, Cs.id parameter, Cs.id temp)) :> StatementSyntax)

                computes @ assigns @ [ SF.ContinueStatement() :> StatementSyntax ]
            | _ -> [ Cs.returns (expression scope env value) ]
        | _ -> [ Cs.returns (expression scope env value) ]

    /// <summary>Whether the body calls the function only as its last step, at least once, with no closure that could keep a parameter.</summary>
    and private isTailLoop (self: Name) (arity: int) (body: Typed) : bool =
        let rec spine (v: Typed) (acc: Typed list) =
            match v.Node with
            | TApply(f, x) -> spine f (x :: acc)
            | _ -> v, acc

        let mutable tailCalls = 0
        let mutable otherUses = 0
        let mutable closures = false

        let rec visit (isTail: bool) (v: Typed) : unit =
            match v.Node with
            | TApply _ ->
                let head, args = spine v []

                match head.Node with
                | TVariable name when name = self && List.length args = arity && isTail ->
                    tailCalls <- tailCalls + 1
                    args |> List.iter (visit false)
                | _ ->
                    visit false head
                    args |> List.iter (visit false)
            | TVariable name -> if name = self then otherUses <- otherUses + 1
            | TIf(c, a, b) ->
                visit false c
                visit isTail a
                visit isTail b
            | TMatch(target, cases) ->
                visit false target
                cases |> List.iter (fun (_, arm) -> visit isTail arm)
            | TLet(_, binding, rest) ->
                visit false binding
                visit isTail rest
            | TDestructure(_, target, rest) ->
                visit false target
                visit isTail rest
            | TLetRec(definitions, rest) ->
                closures <- true
                definitions |> List.iter (fun (_, d) -> visit false d.LocalBody)
                visit isTail rest
            | TLambda(_, _, inner)
            | TPatternLambda(_, _, inner) ->
                closures <- true
                visit false inner
            | TTuple items
            | TList items -> items |> List.iter (visit false)
            | TRecord fields -> fields |> List.iter (snd >> visit false)
            | TField(target, _) -> visit false target
            | TUpdate(target, fields) ->
                visit false target
                fields |> List.iter (snd >> visit false)
            | TLiteral _
            | TUnit
            | TFieldFunction _
            | TConstructor _
            | TSdk _
            | TReference _ -> ()

        visit true body
        tailCalls > 0 && otherUses = 0 && not closures

    /// <summary>The method of a value definition.</summary>
    let methodOf (context: Context) (checkedValue: Checked) : MemberDeclarationSyntax =
        let scope = Scope(context, checkedValue)
        let top = checkedValue.Top

        let inputs = top.Definition.Inputs |> List.map (fun (name, shape) -> name, shape, scope.Local name)
        let env: Env = inputs |> List.map (fun (name, _, local) -> name, local) |> Map.ofList
        let parameters = inputs |> List.map (fun (_, shape, local) -> SF.Parameter(Cs.token local).WithType(scope.Type shape))

        let statements = tail scope env checkedValue.Body
        let name = ModuleLayouts.identifier context.Layouts top.Name
        let output = scope.Type top.Definition.Output

        let declaration =
            SF.MethodDeclaration(output, Cs.token name)
                .WithModifiers(Cs.modifiers [ (if top.Public then SyntaxKind.PublicKeyword else SyntaxKind.InternalKeyword); SyntaxKind.StaticKeyword ])
                .WithParameterList(SF.ParameterList(SF.SeparatedList<ParameterSyntax>(parameters)))

        let declaration =
            if List.isEmpty checkedValue.Parameters then
                declaration
            else
                declaration
                    .WithTypeParameterList(Declarations.typeParameterList checkedValue.Parameters)
                    .WithConstraintClauses(SF.List<TypeParameterConstraintClauseSyntax>(Declarations.constraintClauses checkedValue.Parameters))

        match statements with
        | [ :? ReturnStatementSyntax as only ] ->
            declaration.WithExpressionBody(SF.ArrowExpressionClause(only.Expression)).WithSemicolonToken(SF.Token SyntaxKind.SemicolonToken) :> MemberDeclarationSyntax
        | _ -> declaration.WithBody(Cs.block statements) :> MemberDeclarationSyntax
