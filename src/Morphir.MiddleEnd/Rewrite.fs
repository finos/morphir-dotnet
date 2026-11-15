// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.MiddleEnd

open System
open System.Collections.Generic
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.V4

/// <summary>A place in a model that a pass changes: the root and the typed steps of its node address, with no format or revision.</summary>
type NodePosition = { Root: NodeRoot; Steps: NodeStep list }

/// <summary>
/// One application of a rule, in the positions of one module. The host pins the positions to the input model of the
/// pipeline and to the output model to give a <see cref="RewriteApplication"/>.
/// </summary>
type PassApplication = {
    /// <summary>The rule.</summary>
    Rule: RuleId
    /// <summary>The place of the node that the rule matched, in the input of the pass or, after composition, of the pipeline.</summary>
    Before: NodePosition
    /// <summary>The place of the replacement in the output.</summary>
    After: NodePosition
    /// <summary>The source location of the node that was matched first, when it has one.</summary>
    Origin: SourceLocation option
}

/// <summary>Composition of the application lists of passes that run one after the other.</summary>
[<RequireQualifiedAccess>]
module PassApplications =

    let rec private isPrefix (prefix: NodeStep list) (steps: NodeStep list) =
        match prefix, steps with
        | [], _ -> true
        | head :: prefixTail, step :: stepsTail when head = step -> isPrefix prefixTail stepsTail
        | _ -> false

    /// <summary>True when <paramref name="inner"/> is the place <paramref name="outer"/> or a place below it.</summary>
    let isWithin (outer: NodePosition) (inner: NodePosition) =
        outer.Root = inner.Root && isPrefix outer.Steps inner.Steps

    /// <summary>
    /// The record of the node that holds a place: the record whose replacement is at the place, or else the record of the
    /// deepest replacement that the place is in. A place in a replacement is not in the input, so the node of the input that
    /// it came from is the node that the replacement replaced.
    /// </summary>
    let private holderOf (records: PassApplication list) (place: NodePosition) : PassApplication option =
        match records |> List.tryFind (fun record -> record.After = place) with
        | Some record -> Some record
        | None ->
            records
            |> List.filter (fun record -> isWithin record.After place)
            |> List.sortByDescending (fun record -> List.length record.After.Steps)
            |> List.tryHead

    /// <summary>
    /// The list after a later pass. A record of the later pass that replaces a replacement takes the origin of that
    /// replacement, and the earlier record is dropped. A record of the later pass for a node in an earlier replacement takes
    /// the origin of that replacement too, and the earlier record stays. An earlier record below the node that the later
    /// pass replaces is dropped: its node is gone.
    /// </summary>
    let compose (earlier: PassApplication list) (later: PassApplication list) : PassApplication list =
        later
        |> List.fold
            (fun (state: PassApplication list) (application: PassApplication) ->
                let rebuilt =
                    match holderOf state application.Before with
                    | Some record ->
                        { application with
                            Before = record.Before
                            Origin = (match record.Origin with Some _ -> record.Origin | None -> application.Origin) }
                    | None -> application

                (state |> List.filter (fun record -> not (isWithin application.Before record.After))) @ [ rebuilt ])
            earlier

/// <summary>A read of another module through a package view.</summary>
type PackageLookup = {
    /// <summary>The module that was being processed.</summary>
    From: Path
    /// <summary>The module that was read.</summary>
    Module: Path
    /// <summary>The value that was read, or none when the whole module was read.</summary>
    Value: Name option
} with
    /// <summary>For C# callers: the path of the module that was processed, as text.</summary>
    member this.FromText = CanonicalNames.pathToString this.From

    /// <summary>For C# callers: the path of the module that was read, as text.</summary>
    member this.ModuleText = CanonicalNames.pathToString this.Module

    /// <summary>For C# callers: the name of the value that was read, as text, or null when the whole module was read.</summary>
    member this.ValueTextOrNull: string | null =
        this.Value |> Option.map CanonicalNames.nameToString |> Option.toObj

    override this.ToString() =
        match this.ValueTextOrNull with
        | null -> $"{this.FromText} reads {this.ModuleText}"
        | value -> $"{this.FromText} reads {this.ModuleText}#{value}"

/// <summary>
/// The only way for a rule to read a module other than the one that it processes. The view shows the model as it was
/// before the pass, so the result does not depend on the order of the modules. Each read of another module is recorded.
/// </summary>
type IPackageView =
    /// <summary>
    /// The paths of the modules of the package, in order. A list of paths is not a read of a module, so it is not recorded; the
    /// first path lets a pass that checks the whole package run once.
    /// </summary>
    abstract ModulePaths: Path list
    /// <summary>The module, or none when the package has no such module.</summary>
    abstract TryModule: Path -> ModuleDefinition option
    /// <summary>The value definition, or none when the package has no such module or value.</summary>
    abstract TryValue: Path * Name -> ValueDefinition option

/// <summary>A package view that records the reads of modules other than the current one.</summary>
type RecordingPackageView(package: PackageDefinition, current: Path) =
    let reads = List<PackageLookup>()

    let find (path: Path) =
        package.Modules
        |> OrderedMap.toList
        |> List.tryPick (fun (ModulePath key, access) -> if key = path then Some access.Value else None)

    let record (lookup: PackageLookup) =
        if lookup.Module <> current && not (reads.Contains lookup) then
            reads.Add lookup

    /// <summary>The reads so far, in order and without repeats.</summary>
    member _.Lookups: PackageLookup list = List.ofSeq reads

    interface IPackageView with
        member _.ModulePaths =
            package.Modules |> OrderedMap.toList |> List.map (fun (ModulePath path, _) -> path)

        member _.TryModule path =
            record { From = current; Module = path; Value = None }
            find path

        member _.TryValue(path, name) =
            record { From = current; Module = path; Value = Some name }

            find path
            |> Option.bind (fun definition -> OrderedMap.tryFind name definition.Values)
            |> Option.map (fun access -> access.Value.Value)

/// <summary>What a rule can read besides the node: the module that it processes and the package view.</summary>
type RewriteContext = {
    /// <summary>The path of the module that is processed.</summary>
    Module: Path
    /// <summary>The other modules of the package.</summary>
    Package: IPackageView
}

/// <summary>An example of a rule: a value expression before and after. The test harness wraps it in a one-value model.</summary>
type RuleExample = { Before: Value; After: Value }

/// <summary>
/// A rewrite rule: compiled code with a catalog entry, never a script. A rule rewrites one node into another node and
/// returns none for a node that it does not match. The engine gives the replacement the attributes of the node.
/// </summary>
type RewriteRule = {
    /// <summary>The id, an absolute IRI.</summary>
    Id: RuleId
    /// <summary>A short title.</summary>
    Title: string
    /// <summary>The example. A rule without an example fails the catalog check and does not ship.</summary>
    Example: RuleExample option
    /// <summary>The features that the rule removes: the example before has them and the example after has not.</summary>
    Removes: Set<IrFeature>
    /// <summary>The features that the rule adds: the example after has them.</summary>
    Adds: Set<IrFeature>
    /// <summary>The rule. It returns none when it does not match, and it must return none when it would not change the node.</summary>
    Apply: RewriteContext -> Value -> Value option
}

/// <summary>The number of changing rounds that a fixed point may take: at least one.</summary>
[<CustomEquality; NoComparison>]
type RoundLimit =
    private
    | RoundLimit of int

    /// <summary>The number.</summary>
    member this.Value = let (RoundLimit n) = this in n

    override this.ToString() = string this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? RoundLimit as limit -> this.Value = limit.Value
        | _ -> false

    override this.GetHashCode() = this.Value

    /// <summary>A limit, or none when the number is less than one.</summary>
    static member TryCreate(value: int) : RoundLimit option =
        if value >= 1 then Some(RoundLimit value) else None

    /// <summary>A limit from a literal in source code. Throws <c>ArgumentOutOfRangeException</c> when the number is less than one.</summary>
    static member Create(value: int) : RoundLimit =
        match RoundLimit.TryCreate value with
        | Some limit -> limit
        | None -> raise (ArgumentOutOfRangeException(nameof value, value, "A round limit is at least one."))

/// <summary>How the engine applies a rule set to the nodes of a value.</summary>
[<RequireQualifiedAccess>]
type Strategy =
    /// <summary>Once, children before their parent.</summary>
    | BottomUp
    /// <summary>Once, a parent before its children, then the children of the replacement.</summary>
    | TopDown
    /// <summary>Bottom-up rounds until a round changes nothing, with at most the limit of changing rounds.</summary>
    | FixedPoint of RoundLimit

/// <summary>The diagnostic codes of the rewrite engine and of the rule catalog.</summary>
[<RequireQualifiedAccess>]
module RewriteCodes =

    /// <summary>A fixed point did not settle within its round limit.</summary>
    [<CompiledName("NotSettled")>]
    let notSettled = DiagnosticCode.Create "rewrite.not_settled"

    /// <summary>A rule has no example.</summary>
    [<CompiledName("RuleWithoutExample")>]
    let ruleWithoutExample = DiagnosticCode.Create "rewrite.rule_without_example"

    /// <summary>The example of a rule does not give its after-model.</summary>
    [<CompiledName("ExampleMismatch")>]
    let exampleMismatch = DiagnosticCode.Create "rewrite.example_mismatch"

    /// <summary>The example of a rule does not show the features that the rule says it removes or adds.</summary>
    [<CompiledName("FeatureClaimMismatch")>]
    let featureClaimMismatch = DiagnosticCode.Create "rewrite.feature_claim_mismatch"

    /// <summary>Two rules of a catalog have one id.</summary>
    [<CompiledName("DuplicateRule")>]
    let duplicateRule = DiagnosticCode.Create "rewrite.duplicate_rule"

/// <summary>The rewrite engine.</summary>
[<RequireQualifiedAccess>]
module RuleEngine =

    type private State(root: NodeRoot, context: RewriteContext, rules: RewriteRule list) =
        member val Applications: PassApplication list = [] with get, set
        member val Applied: RewriteRule list = [] with get, set
        member _.Root = root
        member _.Context = context
        member _.Rules = rules

    let private position (state: State) (reversed: NodeStep list) : NodePosition =
        { Root = state.Root; Steps = List.rev reversed }

    /// <summary>Tries the rules at a node, in order. The first rule that matches replaces the node.</summary>
    let private tryRules (state: State) (reversed: NodeStep list) (node: Value) : Value =
        let replaced =
            state.Rules
            |> List.tryPick (fun rule -> rule.Apply state.Context node |> Option.map (fun replacement -> rule, replacement))

        match replaced with
        | None -> node
        | Some(rule, replacement) ->
            let attributes = ValueTraversal.attributesOf node
            let place = position state reversed

            state.Applications <-
                PassApplications.compose
                    state.Applications
                    [ { Rule = rule.Id; Before = place; After = place; Origin = attributes.Source } ]

            state.Applied <- rule :: state.Applied
            ValueTraversal.withAttributes attributes replacement

    let rec private visit (strategy: Strategy) (state: State) (reversed: NodeStep list) (node: Value) : Value =
        let children (value: Value) =
            ValueTraversal.mapChildren (fun relative child -> visit strategy state (List.rev relative @ reversed) child) value

        match strategy with
        | Strategy.TopDown -> node |> tryRules state reversed |> children
        | Strategy.BottomUp
        | Strategy.FixedPoint _ -> node |> children |> tryRules state reversed

    /// <summary>
    /// Rewrites one value expression. <paramref name="root"/> and <paramref name="basePath"/> give its place in the
    /// model. The result is the new value and the applications, in the positions of the input. A fixed point that does
    /// not settle returns an error that names the rules that applied, and no value.
    /// </summary>
    let rewriteValue
        (strategy: Strategy)
        (rules: RewriteRule list)
        (context: RewriteContext)
        (root: NodeRoot)
        (basePath: NodeStep list)
        (value: Value)
        : Checked<Value * PassApplication list> =
        let state = State(root, context, rules)
        let reversedBase = List.rev basePath

        match strategy with
        | Strategy.BottomUp
        | Strategy.TopDown -> Checked.pass (visit strategy state reversedBase value, state.Applications)
        | Strategy.FixedPoint limit ->
            let rec loop (changing: int) (current: Value) =
                let countBefore = List.length state.Applied
                let next = visit strategy state reversedBase current
                let changed = List.length state.Applied > countBefore

                if not changed then
                    Checked.pass (next, state.Applications)
                elif changing >= limit.Value then
                    let names = state.Applied |> List.map (fun rule -> string rule.Id) |> List.distinct |> String.concat ", "

                    Checked.fail [
                        Diagnostic.error
                            RewriteCodes.notSettled
                            $"The rule set did not settle after {limit.Value} changing rounds. The rules that kept applying are: {names}."
                    ]
                else
                    loop (changing + 1) next

            loop 0 value

    /// <summary>
    /// Rewrites the value bodies of one module, one value after the other. The applications are in the positions of the
    /// input module, with the node roots of its values.
    /// </summary>
    let rewriteModule
        (strategy: Strategy)
        (rules: RewriteRule list)
        (package: IPackageView)
        (modulePath: Path)
        (definition: ModuleDefinition)
        : Checked<ModuleDefinition * PassApplication list> =
        let context = { Module = modulePath; Package = package }

        let step
            (state: Checked<(Name * AccessControlled<Documented<ValueDefinition>>) list * PassApplication list>)
            (name: Name, access: AccessControlled<Documented<ValueDefinition>>)
            =
            state
            |> Checked.bind (fun (done', applications) ->
                match access.Value.Value.Body with
                | ExpressionBody(outputType, body) ->
                    let root = NodeRoot.Value(NodeOwner.OwnPackage, modulePath, name)

                    rewriteValue strategy rules context root [ NodeStep.Body ] body
                    |> Checked.map (fun (rewritten, found) ->
                        let documented =
                            { access.Value with Value = { access.Value.Value with Body = ExpressionBody(outputType, rewritten) } }

                        done' @ [ name, { access with Value = documented } ], applications @ found)
                | NativeBody _
                | ExternalBody _
                | IncompleteBody _ -> Checked.pass (done' @ [ name, access ], applications))

        OrderedMap.toList definition.Values
        |> List.fold step (Checked.pass ([], []))
        |> Checked.map (fun (values, applications) ->
            let rebuilt =
                match OrderedMap.ofList values with
                | Ok map -> map
                | Error _ -> definition.Values

            { definition with Values = rebuilt }, applications)

/// <summary>The checks of a rule catalog: every rule has an example that runs, and its feature claims are true.</summary>
[<RequireQualifiedAccess>]
module RuleCatalog =

    let private emptyView =
        { new IPackageView with
            member _.ModulePaths = []
            member _.TryModule _ = None
            member _.TryValue(_, _) = None }

    let private exampleContext = { Module = Path []; Package = emptyView }

    let private diagnostic code (rule: RewriteRule) message =
        Diagnostic.error code $"The rule {rule.Id} ({rule.Title}): {message}"

    let private featureProblems (rule: RewriteRule) (example: RuleExample) : Diagnostic list =
        let before = ValueTraversal.features example.Before
        let after = ValueTraversal.features example.After

        [ for feature in rule.Removes do
              if not (before.Contains feature) || after.Contains feature then
                  yield
                      diagnostic
                          RewriteCodes.featureClaimMismatch
                          rule
                          $"it says that it removes {feature}, but the example before does not have it or the example after still has it."
          for feature in rule.Adds do
              if not (after.Contains feature) then
                  yield
                      diagnostic
                          RewriteCodes.featureClaimMismatch
                          rule
                          $"it says that it adds {feature}, but the example after does not have it." ]

    let private exampleProblems (rule: RewriteRule) : Diagnostic list =
        match rule.Example with
        | None -> [ diagnostic RewriteCodes.ruleWithoutExample rule "it has no example." ]
        | Some example ->
            let root = NodeRoot.Module(NodeOwner.OwnPackage, Path [])
            let limit = RoundLimit.Create 100

            let outcome =
                RuleEngine.rewriteValue (Strategy.FixedPoint limit) [ rule ] exampleContext root [] example.Before

            match Checked.tryValue outcome with
            | Some(result, _) when result = example.After -> featureProblems rule example
            | Some _ ->
                [ diagnostic RewriteCodes.exampleMismatch rule "the example before does not become the example after." ]
            | None -> Checked.diagnostics outcome |> List.ofSeq

    /// <summary>
    /// Checks a catalog: each rule id is used once, each rule has an example, the engine changes the example before to
    /// the example after, and the features that the rule says it removes and adds are in the example.
    /// </summary>
    let check (rules: RewriteRule list) : Checked<unit> =
        let duplicates =
            rules
            |> List.countBy (fun rule -> rule.Id.Value)
            |> List.filter (fun (_, count) -> count > 1)
            |> List.map (fun (id, _) -> Diagnostic.error RewriteCodes.duplicateRule $"The rule id {id} is used by more than one rule.")

        match duplicates @ (rules |> List.collect exampleProblems) with
        | [] -> Checked.pass ()
        | problems -> Checked.fail problems
