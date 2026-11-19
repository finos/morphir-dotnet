// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable
open System.Text.RegularExpressions

/// Reads the body of a PlantUML `@startregex` or `@startebnf` block into a railroad diagram, following PlantUML
/// 1.2026.9 and the refusals that diagrams.mbt adds.
[<RequireQualifiedAccess>]
module internal RailroadReader =
    exception private Refused of code: string * message: string

    let private refuse code message = raise (Refused(code, message))

    let private item kind text (children: RailroadItem list) =
        RailroadItem(kind, text, ImmutableArray.CreateRange children, 1, Some 1)

    let private sequence (items: RailroadItem list) =
        match items with
        | [ single ] -> single
        | [] -> item RailroadKind.Empty "" []
        | items -> item RailroadKind.Sequence "" items

    let private repeat (body: RailroadItem) minimum maximum =
        RailroadItem(RailroadKind.Repeat, "", ImmutableArray.Create body, minimum, maximum)

    /// Presentation statements that a regex or EBNF block may hold beside its content.
    let private presentation (text: string) =
        let lower = text.ToLowerInvariant()

        lower.StartsWith "title " || lower.StartsWith "caption " || lower.StartsWith "header " || lower.StartsWith "footer "
        || lower.StartsWith "skinparam" || lower.StartsWith "scale " || lower.StartsWith "hide " || lower.StartsWith "!pragma"
        || lower.StartsWith "!option"

    /// The statements of a body: blocks such as `<style>…</style>`, `legend…endlegend` and `skinparam x {…}` are
    /// skipped, as are presentation statements, and the rest are kept in order.
    let private content (body: (Reading.Line * int) seq) =
        let kept = ResizeArray<string * SourceSpan>()
        let mutable closing: string option = None

        for line, start in body do
            let text = line.Text.Substring(start).Trim()
            let lower = text.ToLowerInvariant()

            match closing with
            | Some ending -> if lower.StartsWith ending then closing <- None
            | None ->
                if lower.StartsWith "<style>" then (if not (lower.Contains "</style>") then closing <- Some "</style>")
                elif lower = "legend" || lower.StartsWith "legend " then closing <- Some "endlegend"
                elif lower.StartsWith "skinparam" && text.EndsWith '{' then closing <- Some "}"
                elif presentation text then Reading.warn "plantuml/unapplied-presentation" "Presentation is kept in the source but not drawn." (Some(line.Span(start, line.Text.Length)))
                else kept.Add(text, line.Span(start, line.Text.Length))

        kept

    // Regular expressions --------------------------------------------------------------------------------------------

    let private regex (pattern: string) =
        let mutable index = 0
        let peek offset = if index + offset < pattern.Length then pattern[index + offset] else '\000'
        let starts (text: string) = String.CompareOrdinal(pattern, index, text, 0, text.Length) = 0

        let bound (text: string) =
            match Int32.TryParse(text, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
            | true, value -> value
            | false, _ -> refuse "regex-bound" $"The repetition bound {text} is not a number that fits."

        let escape () =
            // `\x41` without braces, and `\P{…}`, are refused; every other escape is one box.
            let next = peek 1

            match next with
            | 'x' when peek 2 <> '{' -> refuse "regex-escape" "Write a hexadecimal escape as \\x{41}."
            | 'P' -> refuse "regex-escape" "Negated property classes \\P{…} are not supported."
            | 'p'
            | 'x' ->
                let close = pattern.IndexOf('}', index)
                if close < 0 then refuse "regex-escape" "An escape with braces has no closing brace."
                let text = pattern.Substring(index, close - index + 1)
                index <- close + 1
                item RailroadKind.Special text []
            | 'u' ->
                let text = pattern.Substring(index, min 6 (pattern.Length - index))
                index <- index + text.Length
                item RailroadKind.Special text []
            | '0' ->
                let digits = Regex.Match(pattern.Substring(index + 1), "^0[0-7]{0,3}").Value
                index <- index + 1 + digits.Length
                item RailroadKind.Special ("\\" + digits) []
            | 'Q' ->
                let close = pattern.IndexOf("\\E", index + 2, StringComparison.Ordinal)
                let finish = if close < 0 then pattern.Length else close
                let text = pattern.Substring(index + 2, finish - index - 2)
                index <- if close < 0 then pattern.Length else close + 2
                item RailroadKind.Terminal text []
            | c when Char.IsLetterOrDigit c ->
                index <- index + 2
                item RailroadKind.Special ("\\" + string c) []
            | '\000' -> refuse "regex-escape" "The pattern ends with a backslash."
            | c ->
                index <- index + 2
                item RailroadKind.Terminal (string c) []

        let characterClass () =
            index <- index + 1
            let negated = peek 0 = '^'
            if negated then index <- index + 1
            if peek 0 = ']' then refuse "regex-empty-class" "A character class needs at least one member."
            let builder = Text.StringBuilder()

            while index < pattern.Length && peek 0 <> ']' do
                if peek 0 = '\\' && index + 1 < pattern.Length then
                    builder.Append(pattern, index, 2) |> ignore
                    index <- index + 2
                elif starts "[:" then
                    let close = pattern.IndexOf(":]", index, StringComparison.Ordinal)
                    if close < 0 then refuse "regex-class" "A POSIX class has no closing :]."
                    builder.Append(pattern, index, close + 2 - index) |> ignore
                    index <- close + 2
                else
                    builder.Append(peek 0) |> ignore
                    index <- index + 1

            if index >= pattern.Length then refuse "regex-unclosed-class" "A character class has no closing ]."
            index <- index + 1
            item RailroadKind.CharacterClass ((if negated then "^" else "") + builder.ToString()) []

        let rec alternation depth =
            if depth > Reading.depthLimit then raise (Refused("depth-limit", $"Groups nest deeper than {Reading.depthLimit} levels."))
            let branches = ResizeArray<RailroadItem> [ concatenation depth ]

            while peek 0 = '|' do
                index <- index + 1
                branches.Add(concatenation depth)

            if branches |> Seq.exists (fun branch -> branch.Kind = RailroadKind.Empty) then
                refuse "regex-empty-branch" "Every alternative needs content."

            if branches.Count = 1 then branches[0] else item RailroadKind.Choice "" (List.ofSeq branches)

        and concatenation depth =
            let items = ResizeArray<RailroadItem>()
            let literal = Text.StringBuilder()

            let flush () =
                if literal.Length > 0 then
                    items.Add(item RailroadKind.Terminal (literal.ToString()) [])
                    literal.Clear() |> ignore

            while index < pattern.Length && peek 0 <> '|' && peek 0 <> ')' do
                let atom =
                    match peek 0 with
                    | '(' -> Some(group depth)
                    | '[' -> Some(characterClass ())
                    | '\\' -> Some(escape ())
                    | '.'
                    | '^'
                    | '$' ->
                        index <- index + 1
                        Some(item RailroadKind.Special (string pattern[index - 1]) [])
                    | '*'
                    | '+'
                    | '?'
                    | '{' when literal.Length = 0 && items.Count = 0 -> refuse "regex-quantifier" "A quantifier needs something to repeat."
                    | _ -> None

                match atom with
                | Some atom ->
                    flush ()
                    items.Add(quantified atom)
                | None ->
                    // A quantifier applies to the last character of a literal run.
                    let character = pattern[index]
                    index <- index + 1
                    let char' = string character

                    if "*+?{".Contains(peek 0) then
                        flush ()
                        items.Add(quantified (item RailroadKind.Terminal char' []))
                    else
                        literal.Append char' |> ignore

            flush ()

            // Escaped characters join the literal run around them, so `a\+b` is one box, as `a+b` reads.
            let merged = ResizeArray<RailroadItem>()

            for next in items do
                if next.Kind = RailroadKind.Empty then
                    ()
                elif merged.Count > 0 && next.Kind = RailroadKind.Terminal && merged[merged.Count - 1].Kind = RailroadKind.Terminal then
                    merged[merged.Count - 1] <- item RailroadKind.Terminal (merged[merged.Count - 1].Text + next.Text) []
                else
                    merged.Add next

            sequence (List.ofSeq merged)

        and quantified (atom: RailroadItem) =
            let lazyOrPossessive () = if peek 0 = '?' || peek 0 = '+' then index <- index + 1

            match peek 0 with
            | '*' ->
                index <- index + 1
                lazyOrPossessive ()
                repeat atom 0 None
            | '+' ->
                index <- index + 1
                lazyOrPossessive ()
                repeat atom 1 None
            | '?' ->
                index <- index + 1
                lazyOrPossessive ()
                RailroadItem(RailroadKind.Optional, "", ImmutableArray.Create atom, 0, Some 1)
            | '{' ->
                let close = pattern.IndexOf('}', index)
                if close < 0 then refuse "regex-quantifier" "A repetition has no closing brace."
                let text = pattern.Substring(index + 1, close - index - 1)
                index <- close + 1

                if peek 0 = '?' then refuse "regex-quantifier" "A lazy bounded repetition is not supported."

                match text.Split ',' with
                | [| exact |] -> let value = bound exact in repeat atom value (Some value)
                | [| low; "" |] when low <> "" -> repeat atom (bound low) None
                | [| low; high |] when low <> "" && high <> "" -> repeat atom (bound low) (Some(bound high))
                | _ -> refuse "regex-quantifier" $"{{{text}}} is not a repetition bound."
            | _ -> atom

        and group depth =
            index <- index + 1

            let take count kind =
                let text = pattern.Substring(index, count)
                index <- index + count
                kind, text

            let kind, name =
                if starts "?:" then
                    index <- index + 2
                    RailroadKind.Group, ""
                elif starts "?=" || starts "?!" then take 2 RailroadKind.Assertion
                elif starts "?<=" || starts "?<!" then take 3 RailroadKind.Assertion
                elif starts "?<" then
                    let close = pattern.IndexOf('>', index)
                    let text = pattern.Substring(index + 2, close - index - 2)
                    index <- close + 1
                    RailroadKind.Group, text
                elif starts "?#" then
                    let close = pattern.IndexOf(')', index)
                    index <- close
                    RailroadKind.Empty, ""
                elif starts "?" then refuse "regex-group" "Atomic groups and inline flags are not supported."
                else RailroadKind.Group, ""

            if kind = RailroadKind.Empty then
                index <- index + 1
                item RailroadKind.Empty "" []
            else
                if peek 0 = ')' then refuse "regex-empty-group" "A group needs content."
                let body = alternation (depth + 1)
                if peek 0 <> ')' then refuse "regex-unclosed-group" "A group has no closing parenthesis."
                index <- index + 1
                item kind name [ body ]

        let body = alternation 0
        if index < pattern.Length then refuse "regex-unmatched" "A closing parenthesis has no opening one."
        body

    // EBNF -----------------------------------------------------------------------------------------------------------

    let private ebnf (text: string) =
        let mutable index = 0
        let peek () = if index < text.Length then text[index] else '\000'

        let skip () =
            let mutable more = true

            while more do
                while index < text.Length && Char.IsWhiteSpace text[index] do index <- index + 1

                if index + 1 < text.Length && text[index] = '(' && text[index + 1] = '*' then
                    let close = text.IndexOf("*)", index + 2, StringComparison.Ordinal)
                    index <- if close < 0 then text.Length else close + 2
                else
                    more <- false

        let identifier () =
            skip ()
            let first = index
            while index < text.Length && (Char.IsLetterOrDigit text[index] || text[index] = '_' || text[index] = '-' || (text[index] = ' ' && index + 1 < text.Length && Char.IsLetterOrDigit text[index + 1] && index > first)) do index <- index + 1
            text.Substring(first, index - first).Trim()

        let rec alternation depth =
            if depth > Reading.depthLimit then raise (Refused("depth-limit", $"Groups nest deeper than {Reading.depthLimit} levels."))
            let branches = ResizeArray<RailroadItem> [ concatenation depth ]
            skip ()

            while peek () = '|' do
                index <- index + 1
                branches.Add(concatenation depth)
                skip ()

            if branches.Count = 1 then branches[0] else item RailroadKind.Choice "" (List.ofSeq branches)

        and concatenation depth =
            skip ()

            // An empty alternative, such as `empty = ;`, matches nothing.
            if peek () = ';' || peek () = '|' || peek () = ')' || peek () = ']' || peek () = '}' then item RailroadKind.Empty "" [] else

            let items = ResizeArray<RailroadItem> [ exception' depth ]
            skip ()

            while peek () = ',' do
                index <- index + 1
                items.Add(exception' depth)
                skip ()

            sequence (List.ofSeq items)

        and exception' depth =
            let first = factor depth
            skip ()

            if peek () = '-' then
                index <- index + 1
                let second = factor depth
                item RailroadKind.Exception "" [ first; second ]
            else
                first

        and factor depth =
            skip ()

            match peek () with
            | '"'
            | '\'' as quote ->
                let close = text.IndexOf(quote, index + 1)
                if close < 0 then refuse "ebnf-terminal" "A terminal has no closing quote."
                let value = text.Substring(index + 1, close - index - 1)
                index <- close + 1
                item RailroadKind.Terminal value []
            | '?' ->
                let close = text.IndexOf('?', index + 1)
                if close < 0 then refuse "ebnf-special" "A special sequence has no closing ?."
                let value = text.Substring(index + 1, close - index - 1).Trim()
                index <- close + 1
                item RailroadKind.Special value []
            | '[' -> bracketed depth '[' ']' (fun (body: RailroadItem) -> RailroadItem(RailroadKind.Optional, "", ImmutableArray.Create body, 0, Some 1))
            | '{' -> bracketed depth '{' '}' (fun body -> repeat body 0 None)
            | '(' -> bracketed depth '(' ')' (fun body -> item RailroadKind.Group "" [ body ])
            | c when Char.IsDigit c ->
                let digits = Regex.Match(text.Substring index, @"^\d+").Value
                index <- index + digits.Length
                skip ()

                // `3 * x` repeats x three times; a number alone is a literal.
                if peek () <> '*' then
                    item RailroadKind.NonTerminal digits []
                else
                    index <- index + 1
                    let count = int digits
                    repeat (factor depth) count (Some count)
            | c when Char.IsLetter c || c = '_' ->
                let name = identifier ()
                skip ()
                if peek () = '*' then refuse "ebnf-count" "A repetition count must be a decimal number."
                item RailroadKind.NonTerminal name []
            | _ -> refuse "ebnf-syntax" $"Unexpected '{peek ()}' in a rule."

        and bracketed depth (opening: char) (closing: char) make =
            index <- index + 1
            skip ()
            if peek () = '-' then refuse "ebnf-syntax" "A bracket cannot start with -."
            let body = alternation (depth + 1)
            skip ()
            if peek () <> closing then refuse "ebnf-syntax" $"Expected {closing} to close {opening}."
            index <- index + 1
            let made = make body

            // `{ x }-` repeats one or more times.
            if opening = '{' && peek () = '-' then
                index <- index + 1
                repeat body 1 None
            else
                made

        let rules = ResizeArray<RailroadRule>()
        skip ()

        while index < text.Length do
            let name = identifier ()
            skip ()
            if name = "" || peek () <> '=' then refuse "ebnf-rule" "A rule is a name, =, an expression and ;."
            index <- index + 1
            let body = alternation 0
            skip ()

            match peek () with
            | ';'
            | '.' -> index <- index + 1
            | '\000' -> refuse "ebnf-rule" $"The rule {name} has no closing ;."
            | c -> refuse "ebnf-syntax" $"Unexpected '{c}' after the rule {name}."

            rules.Add(RailroadRule(name, body, None))
            skip ()

        if rules.Count = 0 then refuse "ebnf-empty" "An EBNF block needs at least one rule."
        rules

    let read (kind: string) (statements: (Reading.Line * int) seq) : Result<Diagram, Diagnostic list> =
        let kept = content statements
        let span = kept |> Seq.tryHead |> Option.map snd

        try
            if kind = "regex" then
                match List.ofSeq kept with
                | [ pattern, _ ] -> Ok(Diagram.Railroad(RailroadDiagram(true, ImmutableArray.Create(RailroadRule("", regex pattern, None)))))
                | [] -> refuse "regex-empty" "A regex block needs a pattern."
                | _ -> refuse "regex-lines" "A regex block holds one pattern line."
            else
                // A second rule after a semicolon on the same line is refused; PlantUML would drop it.
                for line, _ in kept do
                    let outside = Regex.Replace(line, @"""[^""]*""|'[^']*'|\(\*.*?\*\)", "\"\"")
                    let semicolon = outside.IndexOf ';'

                    if semicolon >= 0 && Regex.IsMatch(outside.Substring(semicolon + 1), @"^\s*[\w-]+\s*=") then
                        raise (Refused("ebnf-after-rule", "Text after a rule's semicolon is refused."))

                let rules = ebnf (String.Join("\n", kept |> Seq.map fst))
                Ok(Diagram.Railroad(RailroadDiagram(false, Reading.immutable rules)))
        with Refused(code, message) ->
            let error = if code = "depth-limit" then Reading.limit "plantuml" "depth" message span else Reading.error $"plantuml/{code}" message span
            Error [ error ]
