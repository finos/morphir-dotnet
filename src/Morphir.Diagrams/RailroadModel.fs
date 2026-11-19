// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable

/// What an item of a railroad track is.
[<RequireQualifiedAccess>]
type RailroadKind =
    /// Literal text that the input must contain.
    | Terminal
    /// A reference to another EBNF rule; it stays a reference, so recursion never expands.
    | NonTerminal
    /// A special sequence, escape or class shorthand drawn as one box.
    | Special
    /// Items one after another.
    | Sequence
    /// One of several items.
    | Choice
    /// An item that may be skipped.
    | Optional
    /// An item repeated between Minimum and Maximum times.
    | Repeat
    /// A group, which may be named or capture.
    | Group
    /// A lookahead or lookbehind assertion; it matches no text.
    | Assertion
    /// An EBNF exception: the first child except the second.
    | Exception
    /// A character class, such as `[^abc]`.
    | CharacterClass
    /// An empty item, which matches nothing.
    | Empty

    /// The kind as documentation writes it.
    member this.WireValue =
        match this with
        | RailroadKind.Terminal -> "terminal"
        | RailroadKind.NonTerminal -> "nonterminal"
        | RailroadKind.Special -> "special"
        | RailroadKind.Sequence -> "sequence"
        | RailroadKind.Choice -> "choice"
        | RailroadKind.Optional -> "optional"
        | RailroadKind.Repeat -> "repeat"
        | RailroadKind.Group -> "group"
        | RailroadKind.Assertion -> "assertion"
        | RailroadKind.Exception -> "exception"
        | RailroadKind.CharacterClass -> "class"
        | RailroadKind.Empty -> "empty"

    override this.ToString() = this.WireValue

/// One item of a railroad track. Each item is a distinct occurrence, even when its text repeats.
[<Sealed>]
type RailroadItem
    internal (kind: RailroadKind, text: string, children: ImmutableArray<RailroadItem>, minimum: int, maximum: int option) =
    /// What the item is.
    member _.Kind = kind

    /// The item's text: a terminal, a rule name, a group name, an assertion or a class; empty for structure.
    member _.Text = text

    /// The items inside, in order.
    member _.Children = children

    /// The fewest repetitions of a repeat; 0 or 1 for others.
    member _.Minimum = minimum

    /// The most repetitions of a repeat, or null when unbounded. A large bound stays a number; it is never expanded.
    member _.Maximum = Option.toNullable maximum

    member internal _.MaximumOption = maximum

    override _.ToString() =
        if String.IsNullOrEmpty text then kind.ToString() else $"{kind} {text}"

/// A named track: an EBNF rule, or the one track of a regular expression.
[<Sealed>]
type RailroadRule internal (name: string, body: RailroadItem, comment: string option) =
    /// Rule name; empty for a regular expression.
    member _.Name = name

    /// The track.
    member _.Body = body

    /// A note drawn with the rule, or null.
    member _.Comment: string | null = Option.toObj comment

    override _.ToString() = name

/// A railroad diagram of a PlantUML regular expression or EBNF grammar.
[<Sealed>]
type RailroadDiagram internal (isRegex: bool, rules: ImmutableArray<RailroadRule>) =
    static let span = SourceSpan(SourcePosition(0, 1, 1), SourcePosition(0, 1, 1))

    static let isBox (item: RailroadItem) =
        match item.Kind with
        | RailroadKind.Terminal
        | RailroadKind.NonTerminal
        | RailroadKind.Special
        | RailroadKind.CharacterClass -> true
        | _ -> false

    static let prefixOf (rule: RailroadRule) = if rule.Name = "" then "track" else rule.Name

    /// Every drawn box with its identifier `rule:path`, where the path lists child positions from the rule's body.
    let occurrences =
        lazy
            [
                for rule in rules do
                    let rec walk (path: string) (item: RailroadItem) =
                        seq {
                            if isBox item then yield $"{prefixOf rule}:{path}", item

                            for index in 0 .. item.Children.Length - 1 do
                                yield! walk $"{path}.{index}" item.Children[index]
                        }

                    yield! walk "0" rule.Body
            ]

    let identities =
        lazy
            (let found = Collections.Generic.Dictionary<RailroadItem, string>(HashIdentity.Reference)
             for id, item in occurrences.Value do found[item] <- id
             found)

    /// Which box can come straight after which: the follow relation of the track, with each rule's start before its
    /// first boxes. An exception's excluded part is never consumed, so it has no links.
    let follows =
        lazy
            [
                for rule in rules do
                    let links = ResizeArray<string * string>()

                    // The first boxes, the last boxes and whether the item can match nothing.
                    let rec sets (item: RailroadItem) : string list * string list * bool =
                        if isBox item then
                            let id = identities.Value[item]
                            [ id ], [ id ], false
                        else
                            match item.Kind with
                            | RailroadKind.Sequence ->
                                let mutable first = []
                                let mutable last = []
                                let mutable nullable = true

                                for child in item.Children do
                                    let childFirst, childLast, childNullable = sets child
                                    for from in last do for into in childFirst do links.Add(from, into)
                                    if nullable then first <- first @ childFirst
                                    last <- if childNullable then last @ childLast else childLast
                                    nullable <- nullable && childNullable

                                first, last, nullable
                            | RailroadKind.Choice ->
                                let all = item.Children |> Seq.map sets |> Seq.toList
                                List.collect (fun (first, _, _) -> first) all, List.collect (fun (_, last, _) -> last) all, all |> List.exists (fun (_, _, nullable) -> nullable)
                            | RailroadKind.Optional ->
                                let first, last, _ = sets item.Children[0]
                                first, last, true
                            | RailroadKind.Repeat ->
                                let first, last, nullable = sets item.Children[0]
                                if item.MaximumOption <> Some 1 then for from in last do for into in first do links.Add(from, into)
                                first, last, nullable || item.Minimum = 0
                            | RailroadKind.Exception ->
                                // The excluded part is drawn but never consumed.
                                let saved = links.Count
                                sets item.Children[1] |> ignore
                                links.RemoveRange(saved, links.Count - saved)
                                sets item.Children[0]
                            | RailroadKind.Group
                            | RailroadKind.Assertion when item.Children.Length = 1 -> sets item.Children[0]
                            | _ -> [], [], true

                    let first, _, _ = sets rule.Body
                    if rule.Name <> "" then for into in first do links.Add(rule.Name, into)
                    yield! links
            ]
            |> List.distinct

    let projection =
        lazy
            (let nodes =
                [
                    // Each rule's start, then its boxes, so playback visits them in drawing order.
                    for rule in rules do
                        if rule.Name <> "" then FlowNode(DiagramId.Create rule.Name, rule.Name, FlowShape.Text, span)

                        for id, item in occurrences.Value do
                            if id.StartsWith(prefixOf rule + ":", StringComparison.Ordinal) then
                                let shape = if item.Kind = RailroadKind.NonTerminal then FlowShape.Rectangle else FlowShape.Stadium
                                FlowNode(DiagramId.Create id, item.Text, shape, span)
                ]

             let edges =
                 follows.Value
                 |> List.map (fun (first, second) ->
                     FlowEdge(DiagramId.Create first, DiagramId.Create second, None, FlowLine.Solid, EdgeHead.None, EdgeHead.Arrow, span))

             Flowchart(FlowDirection.LeftToRight, ImmutableArray.CreateRange nodes, ImmutableArray.CreateRange edges, ImmutableArray.Empty))

    /// A digest of the rules' structure, which a trace names so that it cannot be applied to another diagram.
    let fingerprint =
        lazy
            (let text = Text.StringBuilder()

             let rec write (item: RailroadItem) =
                 text.Append(item.Kind.WireValue).Append('(').Append(item.Text.Replace("\\", "\\\\").Replace(")", "\\)")).Append(')') |> ignore
                 text.Append(item.Minimum).Append(',').Append(item.MaximumOption |> Option.map string |> Option.defaultValue "*").Append('[') |> ignore
                 for child in item.Children do write child
                 text.Append(']') |> ignore

             text.Append(if isRegex then "regex" else "ebnf").Append('\n') |> ignore

             for rule in rules do
                 text.Append(rule.Name).Append('=') |> ignore
                 write rule.Body
                 text.Append('\n') |> ignore

             Convert.ToHexString(Security.Cryptography.SHA256.HashData(Text.Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant())

    /// Whether the diagram is a regular expression; otherwise an EBNF grammar.
    member _.IsRegex = isRegex

    /// Rules in source order; a regular expression has one rule with an empty name.
    member _.Rules = rules

    /// The identifier of every drawn box, in drawing order: `rule:path` for an EBNF rule, `track:path` for a regular
    /// expression, where the path lists child positions from the rule's body (`0.2.1`). Equal text never merges boxes.
    member _.OccurrenceIds = occurrences.Value |> List.map fst |> ImmutableArray.CreateRange

    /// The identifier of a drawn item of this diagram, or null for structure and for items of another diagram.
    member _.OccurrenceOf(item: RailroadItem | null) : string | null =
        match item with
        | null -> null
        | item ->
            match identities.Value.TryGetValue item with
            | true, id -> id
            | false, _ -> null

    /// A SHA-256 digest of the rules' structure, in lowercase hexadecimal. A trace carries it.
    member _.Fingerprint = fingerprint.Value

    member internal _.Occurrences = occurrences.Value

    member internal _.Projection = projection.Value

    override _.ToString() =
        if isRegex then "Railroad diagram of a regular expression" else $"Railroad diagram of {rules.Length} EBNF rules"

/// The result of matching or parsing input against a railroad diagram, as an external provider reports it.
[<RequireQualifiedAccess>]
type RailroadOutcome =
    /// The input matched.
    | Matched
    /// The input did not match.
    | NotMatched
    /// The provider does not support the pattern or grammar.
    | Unsupported
    /// The provider stopped at a limit before deciding.
    | Exhausted
    /// The provider failed.
    | Error

    /// The outcome as documentation writes it.
    member this.WireValue =
        match this with
        | RailroadOutcome.Matched -> "matched"
        | RailroadOutcome.NotMatched -> "not-matched"
        | RailroadOutcome.Unsupported -> "unsupported"
        | RailroadOutcome.Exhausted -> "exhausted"
        | RailroadOutcome.Error -> "error"

    override this.ToString() = this.WireValue

/// Whether a visited box consumed input.
[<RequireQualifiedAccess>]
type RailroadVisitResult =
    /// The box consumed input.
    | Matched
    /// The box was tried and did not match.
    | NotMatched

    /// The result as documentation writes it.
    member this.WireValue =
        match this with
        | RailroadVisitResult.Matched -> "matched"
        | RailroadVisitResult.NotMatched -> "not-matched"

    override this.ToString() = this.WireValue

/// One box that a provider visited, with the input it covered in UTF-16 code units.
[<Sealed>]
type RailroadVisit private (occurrence: string, result: RailroadVisitResult, input: (int * int) option) =
    /// A visit with no input span.
    static member Create(occurrence: string | null, result: RailroadVisitResult) =
        ArgumentNullException.ThrowIfNull result

        match occurrence with
        | null -> nullArg (nameof occurrence)
        | occurrence -> RailroadVisit(occurrence, result, None)

    /// A visit that covered `length` code units of input from `start`.
    static member Create(occurrence: string | null, result: RailroadVisitResult, start: int, length: int) =
        ArgumentNullException.ThrowIfNull result
        ArgumentOutOfRangeException.ThrowIfNegative start
        ArgumentOutOfRangeException.ThrowIfNegative length

        match occurrence with
        | null -> nullArg (nameof occurrence)
        | occurrence -> RailroadVisit(occurrence, result, Some(start, length))

    /// The visited box, as in RailroadDiagram.OccurrenceIds.
    member _.Occurrence = occurrence

    /// Whether the box consumed input.
    member _.Result = result

    /// The first input code unit, or null.
    member _.InputStart = input |> Option.map fst |> Option.toNullable

    /// The number of input code units, or null.
    member _.InputLength = input |> Option.map snd |> Option.toNullable

    override _.ToString() = $"{occurrence} {result}"

/// Evidence from an external matcher or parser, drawn over a railroad diagram as highlights. The library never
/// matches input itself. A trace names its diagram by fingerprint; rendering rejects it for any other diagram.
[<Sealed>]
type RailroadTrace
    private (fingerprint: string, outcome: RailroadOutcome, visits: ImmutableArray<RailroadVisit>, failure: string option) =
    /// A trace for a diagram, with its visits in order and the box where matching failed, or null.
    static member Create
        (diagram: RailroadDiagram, outcome: RailroadOutcome, visits: Collections.Generic.IEnumerable<RailroadVisit>, failure: string | null)
        =
        ArgumentNullException.ThrowIfNull diagram
        ArgumentNullException.ThrowIfNull outcome
        ArgumentNullException.ThrowIfNull visits
        let visits = ImmutableArray.CreateRange visits
        if visits |> Seq.exists (box >> isNull) then invalidArg (nameof visits) "A visit is null."
        RailroadTrace(diagram.Fingerprint, outcome, visits, Option.ofObj failure)

    /// The fingerprint of the diagram the trace was made for.
    member _.Fingerprint = fingerprint

    /// The provider's result.
    member _.Outcome = outcome

    /// Visited boxes in order.
    member _.Visits = visits

    /// The box where matching failed, or null.
    member _.Failure: string | null = Option.toObj failure

    member internal _.FailureOption = failure

    override _.ToString() = $"{outcome} trace with {visits.Length} visits"
