// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices

/// The PlantUML block envelope: one `@start<kind>` … `@end<kind>` block, its body and the family of a `@startuml` body.
[<RequireQualifiedAccess>]
module internal PlantUmlReader =
    let private kinds = set [ "uml"; "regex"; "ebnf"; "json"; "yaml" ]

    let private recognised =
        set [ "mindmap"; "wbs"; "gantt"; "salt"; "dot"; "ditaa"; "math"; "latex"; "chen"; "chronology"; "creole"; "files"; "board"; "git"; "nwdiag"; "wire" ]

    /// The text of a statement with its leading space removed.
    let private text (line: Reading.Line, start: int) = line.Text.Substring(start).TrimEnd()

    let private span (line: Reading.Line, start: int) = Some(line.Span(start, line.Text.Length))

    /// Lines that hold something other than blanks or a `'` comment, with block comments `/' … '/` removed.
    let private statements (source: string) =
        let results = ResizeArray<Reading.Line * int>()
        let mutable inComment = false

        for line in Reading.lines source do
            let cursor = Reading.Cursor(line, 0)

            if cursor.Peek = '﻿' && line.Number = 1 then
                cursor.Advance 1

            cursor.SkipSpace()

            if inComment then
                if line.Text.IndexOf("'/", cursor.Index, StringComparison.Ordinal) >= 0 then
                    inComment <- false
            elif cursor.StartsWith "/'" then
                inComment <- line.Text.IndexOf("'/", cursor.Index + 2, StringComparison.Ordinal) < 0
            elif not (cursor.AtEnd || cursor.StartsWith "'") then
                results.Add(line, cursor.Index)

        results.ToArray()

    let private marker (prefix: string) (statement: Reading.Line * int) =
        let value = text statement

        if value.StartsWith(prefix, StringComparison.Ordinal) then
            let rest = value.Substring prefix.Length
            let name = rest |> Seq.takeWhile Char.IsAsciiLetter |> Seq.toArray |> String
            Some(name.ToLowerInvariant())
        else
            None

    /// The family of a `@startuml` body, from statements outside quoted text.
    let private umlFamily (body: (Reading.Line * int) array) =
        let unquoted (value: string) =
            let builder = Text.StringBuilder()
            let mutable quoted = false

            for c in value do
                if c = '"' then quoted <- not quoted
                elif not quoted then builder.Append c |> ignore

            builder.ToString()

        let lines = body |> Array.map (text >> unquoted)
        let starts (prefix: string) (value: string) = value.StartsWith(prefix, StringComparison.Ordinal)

        let isState (value: string) =
            starts "state " value || value.Contains "[*]" || starts "hide empty description" value

        let isActivity (value: string) =
            starts ":" value
            || starts "|" value
            || value.Contains "(*)"
            || List.exists (fun word -> value = word || starts (word + " ") value || starts (word + "(") value)
                [ "start"; "stop"; "end"; "if"; "fork"; "repeat"; "while"; "detach"; "kill"; "split"; "partition" ]

        let isOther (value: string) =
            List.exists (fun word -> starts (word + " ") value)
                [ "class"; "interface"; "enum"; "abstract"; "participant"; "actor"; "usecase"; "component"; "node"; "object"; "entity"; "package"; "database"; "map"; "json"; "boundary"; "control"; "collections"; "queue"; "artifact"; "cloud"; "frame"; "folder"; "rectangle"; "card"; "file"; "agent"; "storage"; "stack"; "hexagon"; "person"; "label"; "port"; "portin"; "portout"; "timing"; "robust"; "concise"; "clock"; "binary" ]

        match Array.exists isState lines, Array.exists isActivity lines, Array.exists isOther lines with
        | true, false, _ -> Ok "state"
        | false, true, false -> Ok "activity"
        | true, true, _ -> Error "plantuml/ambiguous-kind"
        | _ -> Error "plantuml/unsupported-kind"

    /// Reads one PlantUML block.
    let read (source: string) : Result<Diagram, Diagnostic list> =
        let statements = statements source

        if statements.Length = 0 then
            Error [ Reading.error "plantuml/missing-start" "Expected a PlantUML @start line." None ]
        else
            let first = statements[0]

            match marker "@start" first with
            | None -> Error [ Reading.error "plantuml/missing-start" "Expected a PlantUML @start line." (span first) ]
            | Some kind when recognised.Contains kind ->
                Error [ Reading.error "plantuml/unsupported-kind" $"PlantUML @start{kind} diagrams are not supported." (span first) ]
            | Some kind when not (kinds.Contains kind) ->
                Error [ Reading.error "plantuml/unknown-kind" $"@start{kind} is not a PlantUML block." (span first) ]
            | Some kind ->
                match statements |> Array.tryFindIndex (marker "@end" >> Option.isSome) with
                | None -> Error [ Reading.error "plantuml/missing-end" $"Expected @end{kind} to close the block." (span first) ]
                | Some closer ->
                    let found = statements[closer]

                    if marker "@end" found <> Some kind then
                        Error [ Reading.error "plantuml/wrong-end" $"Expected @end{kind}, not {text found}." (span found) ]
                    else
                        let body = statements[1 .. closer - 1]
                        let trailing = statements[closer + 1 ..]

                        let envelope =
                            [
                                match trailing |> Array.tryHead with
                                | Some next when (marker "@start" next).IsSome ->
                                    Reading.error "plantuml/multiple-blocks" "A source holds one PlantUML block." (span next)
                                | Some next -> Reading.error "plantuml/trailing-content" "Only comments may follow the block." (span next)
                                | None -> ()

                                for statement in body do
                                    let value = text statement

                                    if value.StartsWith('!') && not (value.StartsWith("!pragma", StringComparison.Ordinal)) then
                                        Reading.error
                                            "plantuml/preprocessor-refused"
                                            "Preprocessor directives such as !include are not run: they could read files or the network."
                                            (span statement)
                            ]

                        if not envelope.IsEmpty then
                            Error envelope
                        else
                            match kind with
                            | "regex"
                            | "ebnf" -> RailroadReader.read kind body
                            | "json"
                            | "yaml" -> DataTreeReader.read kind body
                            | _ ->
                                match umlFamily body with
                                | Ok "state" -> PlantUmlStateReader.read body
                                | Ok _ -> PlantUmlActivityReader.read body
                                | Error code ->
                                    let message =
                                        if code = "plantuml/ambiguous-kind" then
                                            "The @startuml body mixes state and activity statements."
                                        else
                                            "This @startuml body is not an activity or state diagram."

                                    Error [ Reading.error code message (span first) ]

/// Reads PlantUML source and produces immutable diagram models.
[<AbstractClass; Sealed>]
type PlantUml private () =
    /// Parses one PlantUML activity, state, regex, EBNF, JSON or YAML block. Returns a Result for F# callers.
    static member Parse(source: string | null) : Result<DiagramDocument, ImmutableArray<Diagnostic>> =
        SourceReading.parse "plantuml" PlantUmlReader.read source

    /// Parses a PlantUML block for C# callers. Diagnostics is empty on success.
    static member TryParse
        (
            source: string | null,
            [<Out; MaybeNullWhen(false)>] document: byref<DiagramDocument>,
            [<Out>] diagnostics: byref<ImmutableArray<Diagnostic>>
        ) =
        match PlantUml.Parse source with
        | Ok parsed ->
            document <- parsed
            diagnostics <- ImmutableArray<Diagnostic>.Empty
            true
        | Error errors ->
            document <- Unchecked.defaultof<_>
            diagnostics <- errors
            false
