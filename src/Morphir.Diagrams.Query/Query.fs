// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Query

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.Bpmn

/// A compile error at a span of the query source, in UTF-16 code units.
[<Sealed>]
type QueryDiagnostic internal (code: string, message: string, start: int, finish: int, line: int, column: int) =
    /// A stable code such as `query/unsupported-quantifier` or `query/unknown-kind`.
    member _.Code = code

    /// What is wrong.
    member _.Message = message

    /// The first code unit of the span.
    member _.Start = start

    /// The code unit after the span.
    member _.End = finish

    /// The 1-based line of the span's start.
    member _.Line = line

    /// The 1-based column of the span's start, in code units.
    member _.Column = column

    override _.ToString() = $"{code} {start}-{finish} {message}"

/// Bounds on compilation and evaluation. Evaluation bounds are cumulative across the batches of one cursor.
[<Sealed>]
type QueryLimits private (source: int, patterns: int, depth: int, work: int64, pending: int, captures: int, results: int) =
    /// Generous defaults: 64 KiB of source, 256 patterns, depth 64, ten million steps, 4,096 pending child
    /// assignments, 64 captures a match and 100,000 results.
    static member Default = QueryLimits(65_536, 256, 64, 10_000_000L, 4_096, 64, 100_000)

    /// Bounds of your own; each must be positive.
    static member Create(source: int, patterns: int, depth: int, work: int64, pending: int, captures: int, results: int) =
        for value in [ int64 source; int64 patterns; int64 depth; work; int64 pending; int64 captures; int64 results ] do
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero value

        QueryLimits(source, patterns, depth, work, pending, captures, results)

    /// The longest source, in code units.
    member _.Source = source
    /// The most top-level patterns.
    member _.Patterns = patterns
    /// The deepest pattern nesting.
    member _.Depth = depth
    /// The most node tests.
    member _.Work = work
    /// The most child assignments in progress at once.
    member _.Pending = pending
    /// The most captures in one match.
    member _.Captures = captures
    /// The most matches.
    member _.Results = results

    override _.ToString() = $"limits source {source}, patterns {patterns}, depth {depth}, work {work}, pending {pending}, captures {captures}, results {results}"

/// A captured node.
[<Sealed>]
type QueryCapture internal (name: string, node: QueryNode) =
    /// The capture's name, without `@`.
    member _.Name = name
    /// The node.
    member _.Node = node
    override _.ToString() = $"{name}={node}"

/// One match: the index of the top-level pattern and its captures in the order they appear in the pattern.
[<Sealed>]
type QueryMatch internal (pattern: int, captures: ImmutableArray<QueryCapture>) =
    /// The index of the top-level pattern, from 0.
    member _.PatternIndex = pattern
    /// The captures.
    member _.Captures = captures
    override _.ToString() = $"match {pattern}: " + String.Join(" ", captures)

/// The evaluation bound that stopped a cursor.
[<RequireQualifiedAccess>]
type QueryBound =
    /// Node tests.
    | Work
    /// Child assignments in progress at once.
    | Pending
    /// Captures in one match.
    | Captures
    /// Matches.
    | Results

    /// The bound as documentation writes it.
    member this.WireValue =
        match this with
        | QueryBound.Work -> "work"
        | QueryBound.Pending -> "pending"
        | QueryBound.Captures -> "captures"
        | QueryBound.Results -> "results"

    override this.ToString() = this.WireValue

/// Whether a cursor has more matches.
[<RequireQualifiedAccess>]
type QueryStatus =
    /// More matches may follow.
    | More
    /// Every match has been read.
    | Complete
    /// Evaluation stopped at a bound.
    | LimitReached of bound: QueryBound

    /// Calls the function for the case, for C# callers.
    member this.Match<'T>(more: Func<'T>, complete: Func<'T>, limitReached: Func<QueryBound, 'T>) =
        match this with
        | QueryStatus.More -> more.Invoke()
        | QueryStatus.Complete -> complete.Invoke()
        | QueryStatus.LimitReached bound -> limitReached.Invoke bound

    override this.ToString() =
        match this with
        | QueryStatus.More -> "more"
        | QueryStatus.Complete -> "complete"
        | QueryStatus.LimitReached bound -> $"limit {bound}"

/// The matches of one read and the cursor's status after it.
[<Sealed>]
type QueryBatch internal (matches: ImmutableArray<QueryMatch>, status: QueryStatus) =
    /// The matches, in order.
    member _.Matches = matches
    /// The status after this batch.
    member _.Status = status

    override _.ToString() = $"{matches.Length} matches, {status}"

type internal Kind =
    | Any
    | Exact of string * string
    | Category of string

type internal Pattern =
    | Node of Kind * Child list * string list * (int * int)
    | Choice of Pattern list * string list * (int * int)

and internal Child =
    | Plain of Pattern
    | Field of string * Pattern
    | Absent of string

type internal Predicate =
    | AttrEq of string * string * string
    | AttrNotEq of string * string * string
    | HasAttr of string * string
    | MissingAttr of string * string
    | AttrIn of string * string * string list
    | TextEq of string * string

exception private QueryError of code: string * message: string * start: int * finish: int

exception internal QueryExhausted of bound: QueryBound

/// A compiled query of language version 1.
[<Sealed>]
type TreeQuery private (source: string, patterns: (Pattern * Predicate list) list, limits: QueryLimits) =
    static let bindings =
        [ "bpmn", BpmnNamespaces.Model; "bpmndi", BpmnNamespaces.Di; "dc", BpmnNamespaces.Dc; "di", BpmnNamespaces.DdDi ]

    static let categories =
        set
            [
                "document"; "comment"; "processing-instruction"; "text"; "activity"; "event"; "gateway"; "connection"; "data"
                "artifact"; "participant"; "lane"; "choreography-activity"; "conversation"; "conversation-link"; "model"
                "diagram-interchange"; "extension"
            ]

    /// The BPMN 2.0.2 semantic element names, which a `bpmn.` kind must be one of.
    static let modelKinds =
        set
            [
                "definitions"; "import"; "extension"; "extensionElements"; "documentation"; "process"; "collaboration"; "choreography"
                "globalConversation"; "participant"; "participantMultiplicity"; "participantAssociation"; "messageFlow"; "messageFlowAssociation"
                "conversation"; "subConversation"; "callConversation"; "conversationLink"; "conversationAssociation"; "correlationKey"
                "correlationProperty"; "correlationPropertyRetrievalExpression"; "correlationSubscription"; "correlationPropertyBinding"
                "task"; "userTask"; "serviceTask"; "sendTask"; "receiveTask"; "manualTask"; "businessRuleTask"; "scriptTask"; "script"
                "callActivity"; "subProcess"; "adHocSubProcess"; "transaction"; "startEvent"; "endEvent"; "intermediateCatchEvent"
                "intermediateThrowEvent"; "boundaryEvent"; "implicitThrowEvent"; "exclusiveGateway"; "inclusiveGateway"; "parallelGateway"
                "complexGateway"; "eventBasedGateway"; "sequenceFlow"; "conditionExpression"; "flowNodeRef"; "laneSet"; "lane"; "childLaneSet"
                "dataObject"; "dataObjectReference"; "dataStore"; "dataStoreReference"; "dataInput"; "dataOutput"; "dataState"
                "dataInputAssociation"; "dataOutputAssociation"; "ioSpecification"; "inputSet"; "outputSet"; "dataInputRefs"
                "dataOutputRefs"; "optionalInputRefs"; "optionalOutputRefs"; "whileExecutingInputRefs"; "whileExecutingOutputRefs"
                "inputSetRefs"; "outputSetRefs"; "ioBinding"; "property"; "assignment"; "from"; "to"; "transformation"; "sourceRef"
                "targetRef"; "textAnnotation"; "text"; "association"; "group"; "category"; "categoryValue"; "message"; "error"
                "escalation"; "signal"; "itemDefinition"; "interface"; "operation"; "inMessageRef"; "outMessageRef"; "errorRef"
                "endPoint"; "resource"; "resourceParameter"; "resourceRole"; "performer"; "humanPerformer"; "potentialOwner"
                "resourceRef"; "resourceAssignmentExpression"; "resourceParameterBinding"; "expression"; "formalExpression"
                "messageEventDefinition"; "timerEventDefinition"; "signalEventDefinition"; "errorEventDefinition"
                "escalationEventDefinition"; "compensateEventDefinition"; "conditionalEventDefinition"; "linkEventDefinition"
                "cancelEventDefinition"; "terminateEventDefinition"; "eventDefinition"; "eventDefinitionRef"; "timeDate"; "timeDuration"
                "timeCycle"; "condition"; "source"; "target"; "standardLoopCharacteristics"; "multiInstanceLoopCharacteristics"
                "loopCardinality"; "loopCondition"; "loopDataInputRef"; "loopDataOutputRef"; "inputDataItem"; "outputDataItem"
                "completionCondition"; "complexBehaviorDefinition"; "activationCondition"; "choreographyTask"; "subChoreography"
                "callChoreography"; "participantRef"; "messageFlowRef"; "choreographyRef"; "innerParticipantRef"; "outerParticipantRef"
                "globalTask"; "globalUserTask"; "globalManualTask"; "globalScriptTask"; "globalBusinessRuleTask"; "globalChoreographyTask"
                "rendering"; "auditing"; "monitoring"; "relationship"; "partnerEntity"; "partnerRole"; "lane"; "supports"; "incoming"
                "outgoing"; "interfaceRef"; "dataInputRef"; "dataOutputRef"
            ]

    /// Semantic fields: the child kinds that each named field holds.
    static let fields =
        dict
            [
                "flowElement",
                set
                    [
                        "task"; "userTask"; "serviceTask"; "sendTask"; "receiveTask"; "manualTask"; "businessRuleTask"; "scriptTask"; "callActivity"
                        "subProcess"; "adHocSubProcess"; "transaction"; "startEvent"; "endEvent"; "intermediateCatchEvent"
                        "intermediateThrowEvent"; "boundaryEvent"; "implicitThrowEvent"; "exclusiveGateway"; "inclusiveGateway"
                        "parallelGateway"; "complexGateway"; "eventBasedGateway"; "sequenceFlow"; "dataObject"; "dataObjectReference"
                        "dataStoreReference"; "choreographyTask"; "subChoreography"; "callChoreography"
                    ]
                "artifact", set [ "textAnnotation"; "association"; "group" ]
                "laneSet", set [ "laneSet" ]
                "rootElement",
                set [ "process"; "collaboration"; "choreography"; "message"; "error"; "escalation"; "signal"; "itemDefinition"; "interface"; "category"; "dataStore"; "resource"; "globalConversation"; "partnerEntity"; "partnerRole" ]
                "eventDefinition",
                set [ "messageEventDefinition"; "timerEventDefinition"; "signalEventDefinition"; "errorEventDefinition"; "escalationEventDefinition"; "compensateEventDefinition"; "conditionalEventDefinition"; "linkEventDefinition"; "cancelEventDefinition"; "terminateEventDefinition" ]
                "participant", set [ "participant" ]
                "messageFlow", set [ "messageFlow" ]
                "lane", set [ "lane" ]
            ]

    static let fail code message start finish = raise (QueryError(code, message, start, finish))

    static let parse (text: string) (namespaces: IReadOnlyDictionary<string, string> | null) (limits: QueryLimits) =
        let mutable index = 0
        let peek () = if index < text.Length then text[index] else '\000'

        let skip () =
            let mutable more = true

            while more do
                if index < text.Length && Char.IsWhiteSpace text[index] then index <- index + 1
                elif index < text.Length && text[index] = ';' then
                    while index < text.Length && text[index] <> '\n' do
                        index <- index + 1
                else more <- false

        let unexpected () =
            let width = if index + 1 < text.Length && Char.IsSurrogatePair(text, index) then 2 else 1
            if index >= text.Length then fail "query/syntax" "The query ends early." index index
            else fail "query/syntax" $"Unexpected '{text.Substring(index, width)}'." index (index + width)

        let expect (c: char) =
            skip ()
            if peek () <> c then unexpected ()
            index <- index + 1

        let name () =
            let start = index
            while index < text.Length && (Char.IsLetterOrDigit text[index] || text[index] = '_' || text[index] = '-' || text[index] = '.' || text[index] = '?' || text[index] = '!' || text[index] = ':') do
                index <- index + 1
            text.Substring(start, index - start), start

        let resolve (prefix: string) =
            match namespaces with
            | null -> None
            | map -> match map.TryGetValue prefix with | true, uri -> Some uri | _ -> None
            |> Option.orElse (bindings |> List.tryFind (fst >> (=) prefix) |> Option.map snd)

        let kind (token: string) start =
            let finish = start + token.Length

            if token = "_" then Any
            elif token.StartsWith("category.", StringComparison.Ordinal) then
                let value = token.Substring 9
                if not (categories.Contains value) then fail "query/unknown-category" $"{value} is not a category." start finish
                Category value
            else
                let dot = token.IndexOf '.'
                if dot <= 0 then fail "query/syntax" $"{token} is not a kind; write prefix.name." start finish
                let prefix, local = token.Substring(0, dot), token.Substring(dot + 1)

                match resolve prefix with
                | None -> fail "query/unbound-prefix" $"The prefix {prefix} is not bound to a namespace." start finish
                | Some uri ->
                    if uri = BpmnNamespaces.Model && not (modelKinds.Contains local) then fail "query/unknown-kind" $"{local} is not a BPMN element." start finish
                    Exact(uri, local)

        // Captures after a pattern; the pattern's span ends after the last one, before any white space.
        let captures () =
            let names = ResizeArray<string>()
            let mutable finish = index
            skip ()

            while peek () = '@' do
                index <- index + 1
                let value, _ = name ()
                names.Add value
                finish <- index
                skip ()

            match peek () with
            | '*' | '+' | '?' -> fail "query/unsupported-quantifier" "Quantifiers are not supported in version 1." index (index + 1)
            | _ ->
                index <- finish
                List.ofSeq names

        let field (token: string) start =
            let dot = token.IndexOf '.'
            let local = if dot >= 0 then token.Substring(dot + 1) else token
            if not (fields.ContainsKey local) then fail "query/unknown-field" $"{token} is not a field." start (start + token.Length)
            local

        let rec pattern depth : Pattern =
            if depth > limits.Depth then fail "query/depth-limit" "The pattern nests too deeply." index index
            skip ()
            let start = index

            match peek () with
            | '[' ->
                index <- index + 1
                let branches = ResizeArray<Pattern>()
                skip ()

                while peek () <> ']' do
                    if index >= text.Length then unexpected ()
                    branches.Add(pattern (depth + 1))
                    skip ()

                index <- index + 1
                let names = captures ()
                Choice(List.ofSeq branches, names, (start, index))
            | '(' ->
                index <- index + 1
                skip ()
                let token, tokenStart = name ()
                if token = "" then unexpected ()
                let found = kind token tokenStart
                let children = ResizeArray<Child>()
                skip ()

                while peek () <> ')' do
                    match peek () with
                    | '.' -> fail "query/unsupported-anchor" "Anchors are not supported in version 1." index (index + 1)
                    | '!' ->
                        index <- index + 1
                        let token, fieldStart = name ()
                        children.Add(Absent(field token fieldStart))
                    | '(' | '[' -> children.Add(Plain(pattern (depth + 1)))
                    | '\000' -> unexpected ()
                    | _ ->
                        let token, fieldStart = name ()
                        if token = "" || not (token.EndsWith ':') then
                            index <- fieldStart
                            unexpected ()

                        let local = field (token.TrimEnd ':') fieldStart
                        children.Add(Field(local, pattern (depth + 1)))

                    skip ()

                index <- index + 1
                let names = captures ()
                Node(found, List.ofSeq children, names, (start, index))
            | _ -> unexpected ()

        // A top-level item: a pattern, or a group of one pattern and its predicates.
        let item () =
            skip ()
            let start = index

            let grouped =
                if peek () <> '(' then
                    false
                else
                    index <- index + 1
                    skip ()
                    let found = peek () = '(' || peek () = '['
                    index <- start
                    found

            if grouped then
                index <- index + 1
                let inner = pattern 1
                let predicates = ResizeArray<string * string list * (int * int) * (int * int)>()
                skip ()

                while peek () <> ')' do
                    let predicateStart = index
                    if peek () <> '(' then unexpected ()
                    skip ()
                    let saved = index
                    index <- index + 1
                    skip ()

                    if peek () <> '#' then
                        // A second pattern in a group is a sibling sequence, which version 1 does not have.
                        index <- saved
                        let other = index
                        pattern 1 |> ignore
                        // The span covers the sibling pattern up to its closing parenthesis, as upstream reports it.
                        fail "query/unsupported-grouping" "Groups of sibling patterns are not supported in version 1." other (index - 1)

                    index <- index + 1
                    let predicate, _ = name ()
                    let nameEnd = index
                    let arguments = ResizeArray<string>()
                    skip ()

                    while peek () <> ')' do
                        match peek () with
                        | '@' ->
                            index <- index + 1
                            let value, _ = name ()
                            arguments.Add("@" + value)
                        | '"' ->
                            let close = text.IndexOf('"', index + 1)
                            if close < 0 then unexpected ()
                            arguments.Add(text.Substring(index + 1, close - index - 1))
                            index <- close + 1
                        | _ -> unexpected ()

                        skip ()

                    index <- index + 1
                    predicates.Add(predicate, List.ofSeq arguments, (predicateStart, nameEnd), (predicateStart, index))
                    skip ()

                index <- index + 1
                inner, List.ofSeq predicates, (start, index)
            else
                let found = pattern 1
                found, [], (start, index)

        let items = ResizeArray()
        skip ()

        while index < text.Length do
            items.Add(item ())
            skip ()

        if items.Count > limits.Patterns then fail "query/patterns-limit" $"A query holds at most {limits.Patterns} patterns." 0 text.Length

        [
            for found, predicates, (start, finish) in items do
                let names = ResizeArray<string>()

                let rec collect pattern =
                    match pattern with
                    | Node(_, children, captured, _) ->
                        for child in children do
                            match child with
                            | Plain inner
                            | Field(_, inner) -> collect inner
                            | Absent _ -> ()

                        names.AddRange captured
                    | Choice(branches, captured, _) ->
                        for branch in branches do collect branch
                        names.AddRange captured

                collect found

                // A name may repeat across the branches of a choice, but not within one match.
                let rec duplicated pattern =
                    match pattern with
                    | Node(_, children, captured, _) ->
                        let inner = children |> List.collect (function | Plain p | Field(_, p) -> duplicated p | Absent _ -> [])
                        inner @ captured
                    | Choice(branches, captured, _) ->
                        let longest = branches |> List.map duplicated
                        (longest |> List.tryHead |> Option.defaultValue []) @ captured

                let all = duplicated found
                if (all |> List.distinct |> List.length) <> all.Length then fail "query/duplicate-capture" "A capture name is used twice in one pattern." start finish

                let known = HashSet<string>(names)

                let checkedPredicates =
                    [
                        for predicate, arguments, (nameStart, nameEnd), (whole, wholeEnd) in predicates do
                            let capture (value: string) =
                                if not (value.StartsWith '@') || not (known.Contains(value.Substring 1)) then
                                    fail "query/unknown-capture" $"{value} is not a capture of the pattern." whole wholeEnd
                                value.Substring 1

                            match predicate, arguments with
                            | p, _ when p.EndsWith '!' -> fail "query/unsupported-directive" $"#{p} directives are not supported." nameStart nameEnd
                            | ("match?" | "not-match?" | "any-match?" | "any-not-match?" | "lua-match?"), _ ->
                                fail "query/unsupported-regex" $"#{predicate} needs regular expressions, which version 1 does not have." nameStart nameEnd
                            | "attr-eq?", [ node; attribute; value ] -> AttrEq(capture node, attribute, value)
                            | "attr-not-eq?", [ node; attribute; value ] -> AttrNotEq(capture node, attribute, value)
                            | "has-attr?", [ node; attribute ] -> HasAttr(capture node, attribute)
                            | "missing-attr?", [ node; attribute ] -> MissingAttr(capture node, attribute)
                            | "attr-in?", node :: attribute :: values when not values.IsEmpty -> AttrIn(capture node, attribute, values)
                            | "text-eq?", [ node; value ] -> TextEq(capture node, value)
                            | ("attr-eq?" | "attr-not-eq?" | "has-attr?" | "missing-attr?" | "attr-in?" | "text-eq?"), _ ->
                                fail "query/predicate-arguments" $"#{predicate} has the wrong arguments." whole wholeEnd
                            | _ -> fail "query/unknown-predicate" $"#{predicate} is not a predicate of version 1." whole wholeEnd
                    ]

                found, checkedPredicates
        ]

    /// Compiles a query. `namespaces` binds prefixes beyond `bpmn`, `bpmndi`, `dc` and `di`. Returns a Result for F#
    /// callers.
    static member Compile(source: string | null, namespaces: IReadOnlyDictionary<string, string> | null, limits: QueryLimits) : Result<TreeQuery, QueryDiagnostic> =
        ArgumentNullException.ThrowIfNull limits

        let position (text: string) (offset: int) =
            let before = text.Substring(0, min offset text.Length)
            let lines = before.Split '\n'
            lines.Length, lines[lines.Length - 1].TrimEnd('\r').Length + 1

        match source with
        | null -> Error(QueryDiagnostic("query/missing-source", "No query was given.", 0, 0, 1, 1))
        | text when text.Length > limits.Source -> Error(QueryDiagnostic("query/source-limit", $"A query holds at most {limits.Source} code units.", 0, text.Length, 1, 1))
        | text ->
            try
                Ok(TreeQuery(text, parse text namespaces limits, limits))
            with QueryError(code, message, start, finish) ->
                let line, column = position text start
                Error(QueryDiagnostic(code, message, start, finish, line, column))

    /// Compiles a query for C# callers: true with the query, or false with the diagnostic.
    static member TryCompile
        (
            source: string | null,
            namespaces: IReadOnlyDictionary<string, string> | null,
            limits: QueryLimits,
            [<Out; MaybeNullWhen(false)>] query: byref<TreeQuery>,
            [<Out; MaybeNullWhen(true)>] diagnostic: byref<QueryDiagnostic>
        ) =
        match TreeQuery.Compile(source, namespaces, limits) with
        | Ok compiled ->
            query <- compiled
            diagnostic <- Unchecked.defaultof<_>
            true
        | Error found ->
            query <- Unchecked.defaultof<_>
            diagnostic <- found
            false

    /// The number of top-level patterns.
    member _.PatternCount = patterns.Length

    /// Starts reading the matches below a node of the tree (the node itself included), or below its root when the
    /// node is null. Matches come in candidate preorder, then pattern order, then child positions.
    member _.Run(tree: QueryTree, root: QueryNode | null) =
        ArgumentNullException.ThrowIfNull tree
        let start = match root with | null -> tree.Root | node -> node
        QueryCursor(start, patterns, limits)

    override _.ToString() = source

/// Reads the matches of a query in batches. Bounds count across batches, so the batch size never changes the matches.
and [<Sealed>] QueryCursor internal (root: QueryNode, patterns: (Pattern * Predicate list) list, limits: QueryLimits) =
    let mutable work = 0L
    let mutable pending = 0
    let mutable results = 0
    let mutable status = QueryStatus.More

    let step () =
        work <- work + 1L
        if work > limits.Work then raise (QueryExhausted QueryBound.Work)

    let kindMatches kind (node: QueryNode) =
        match kind with
        | Any -> true
        | Category value -> node.Category = value
        | Exact(uri, local) -> node.NamespaceUri = uri && node.LocalName = local && node.Category <> "processing-instruction"

    let inField (field: string) (node: QueryNode) =
        node.NamespaceUri = BpmnNamespaces.Model
        && (match field with
            | "flowElement" -> node.Category = "activity" || node.Category = "event" || node.Category = "gateway" || node.LocalName = "sequenceFlow" || node.LocalName = "dataObject" || node.LocalName = "dataObjectReference" || node.LocalName = "dataStoreReference" || node.Category = "choreography-activity"
            | "artifact" -> node.LocalName = "textAnnotation" || node.LocalName = "association" || node.LocalName = "group"
            | "rootElement" ->
                (match node.Parent with | null -> false | owner -> owner.LocalName = "definitions")
                && node.LocalName <> "import" && node.LocalName <> "extensionElements" && node.LocalName <> "documentation"
            | "eventDefinition" -> node.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal)
            | other -> node.LocalName = other)

    /// Every way the pattern matches the node, each as its captures in pattern order.
    let rec matches (pattern: Pattern) (node: QueryNode) : (string * QueryNode) list list =
        step ()

        match pattern with
        | Choice(branches, names, _) ->
            [
                for branch in branches do
                    for found in matches branch node do
                        found @ (names |> List.map (fun name -> name, node))
            ]
        | Node(kind, children, names, _) when kindMatches kind node ->
            let absent = children |> List.choose (function | Absent field -> Some field | _ -> None)

            if absent |> List.exists (fun field -> node.Children |> Seq.exists (inField field)) then
                []
            else
                let ordered = children |> List.filter (function | Absent _ -> false | _ -> true)

                // Child patterns take children in increasing positions; every assignment is a match.
                let rec assign (rest: Child list) (from: int) : (string * QueryNode) list list =
                    match rest with
                    | [] -> [ [] ]
                    | child :: tail ->
                        pending <- pending + 1
                        if pending > limits.Pending then raise (QueryExhausted QueryBound.Pending)

                        let found =
                            [
                                for position in from .. node.Children.Length - 1 do
                                    let candidate = node.Children[position]

                                    let inner, allowed =
                                        match child with
                                        | Field(field, inner) -> inner, inField field candidate
                                        | Plain inner -> inner, true
                                        | Absent _ -> failwith "unreachable"

                                    if allowed then
                                        for head in matches inner candidate do
                                            for later in assign tail (position + 1) do
                                                head @ later
                            ]

                        pending <- pending - 1
                        found

                [ for found in assign ordered 0 -> found @ (names |> List.map (fun name -> name, node)) ]
        | Node _ -> []

    let holds (captured: (string * QueryNode) list) predicate =
        let node name = captured |> List.find (fst >> (=) name) |> snd

        match predicate with
        | AttrEq(name, attribute, value) -> (node name).Attribute attribute = value
        | AttrNotEq(name, attribute, value) -> (match (node name).Attribute attribute with | null -> false | found -> found <> value)
        | HasAttr(name, attribute) -> not (isNull ((node name).Attribute attribute))
        | MissingAttr(name, attribute) -> isNull ((node name).Attribute attribute)
        | AttrIn(name, attribute, values) -> (match (node name).Attribute attribute with | null -> false | found -> List.contains found values)
        | TextEq(name, value) -> (node name).Text = value

    /// Matches in order, produced on demand.
    let produced =
        (seq {
            let stack = Stack<QueryNode>()
            stack.Push root

            while stack.Count > 0 do
                let candidate = stack.Pop()

                for child in Seq.rev candidate.Children do
                    stack.Push child

                let mutable index = 0

                for pattern, predicates in patterns do
                    for captured in matches pattern candidate do
                        if predicates |> List.forall (holds captured) then
                            if captured.Length > limits.Captures then raise (QueryExhausted QueryBound.Captures)
                            results <- results + 1
                            if results > limits.Results then raise (QueryExhausted QueryBound.Results)
                            yield QueryMatch(index, captured |> List.map (fun (name, node) -> QueryCapture(name, node)) |> ImmutableArray.CreateRange)

                    index <- index + 1
        })
            .GetEnumerator()

    /// Reads up to `count` more matches.
    member _.Next(count: int) =
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero count
        let found = ResizeArray<QueryMatch>()

        try
            while status = QueryStatus.More && found.Count < count do
                if produced.MoveNext() then found.Add produced.Current else status <- QueryStatus.Complete
        with QueryExhausted bound ->
            status <- QueryStatus.LimitReached bound

        QueryBatch(ImmutableArray.CreateRange found, status)

    /// The status after the last read.
    member _.Status = status

    override _.ToString() = $"Query cursor, {status}"
