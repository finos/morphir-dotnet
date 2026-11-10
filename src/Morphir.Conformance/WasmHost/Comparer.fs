// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System
open System.Globalization
open System.Text
open System.Text.RegularExpressions

/// A matcher that an observation does not satisfy, by the corpus's name for the matcher.
type MatcherFailure = {
    /// The run, counted from 1.
    Run: int
    /// The matcher, as the corpus's conventions name it, for example exitCode or events.
    Matcher: string
    Message: string
}

/// How a started event declares one bound.
[<RequireQualifiedAccess>]
type BoundState =
    | Enforced
    | NotEnforced of reason: string

    /// The state as the report writes it: enforced, or not-enforced:<reason>.
    member this.WireValue =
        match this with
        | Enforced -> "enforced"
        | NotEnforced reason -> $"not-enforced:{reason}"

    override this.ToString() = this.WireValue

/// One stderr line that is a `morphir/command-event`.
type WorkerEventLine = {
    /// The event name, for example started.
    Name: string
    /// When the runner read it, in milliseconds from the start of the worker.
    AtMs: int64
    /// The members of the event object, in the order written.
    Members: (string * JsonValue) list
}

/// The comparisons of the corpus's conventions. Each matcher has one function; an observation that does
/// not satisfy a matcher gives a failure that names it. Nothing here passes an observation it cannot read.
module Comparer =
    let private invariant = CultureInfo.InvariantCulture

    let private numberValue (literal: string) =
        match Decimal.TryParse(literal, NumberStyles.Float, invariant) with
        | true, value -> Some(Choice1Of2 value)
        | _ ->
            match Double.TryParse(literal, NumberStyles.Float, invariant) with
            | true, value when Double.IsFinite value -> Some(Choice2Of2 value)
            | _ -> None

    /// JSON value equality: the same kind; strings equal ordinally; numbers equal by value, so 1 equals 1.0;
    /// arrays equal item by item in order; objects with the same member names, in any order, and equal
    /// values.
    let rec jsonEquals (left: JsonValue) (right: JsonValue) =
        match left, right with
        | JsonValue.Null, JsonValue.Null -> true
        | JsonValue.Bool left, JsonValue.Bool right -> left = right
        | JsonValue.Number left, JsonValue.Number right ->
            left = right
            || (
                match numberValue left, numberValue right with
                | Some(Choice1Of2 left), Some(Choice1Of2 right) -> left = right
                | Some left, Some right ->
                    let asDouble =
                        function
                        | Choice1Of2(value: decimal) -> float value
                        | Choice2Of2(value: float) -> value

                    asDouble left = asDouble right
                | _ -> false
            )
        | JsonValue.String left, JsonValue.String right ->
            String.Equals(left, right, StringComparison.Ordinal)
        | JsonValue.Array left, JsonValue.Array right ->
            left.Length = right.Length
            && List.forall2 jsonEquals left right
        | JsonValue.Object left, JsonValue.Object right ->
            left.Length = right.Length
            && left
               |> List.forall (fun (name, value) ->
                   match
                       right
                       |> List.tryFind (fun (other, _) ->
                           String.Equals(name, other, StringComparison.Ordinal)
                       )
                   with
                   | Some(_, other) -> jsonEquals value other
                   | None -> false
               )
        | _ -> false

    /// The JSON type of a value.
    let jsonType (value: JsonValue) =
        match value with
        | JsonValue.Null -> JsonType.Null
        | JsonValue.Bool _ -> JsonType.Boolean
        | JsonValue.Number _ -> JsonType.Number
        | JsonValue.String _ -> JsonType.String
        | JsonValue.Array _ -> JsonType.Array
        | JsonValue.Object _ -> JsonType.Object

    let private arrayIndex = Regex("^(0|[1-9][0-9]*)$")

    /// The value at an RFC 6901 pointer, if the pointer resolves. The empty pointer is the whole value; ~1
    /// is / and ~0 is ~ in a token; an array token is a decimal index without leading zeros.
    let resolve (pointer: string) (value: JsonValue) : JsonValue option =
        if pointer = "" then
            Some value
        elif not (pointer.StartsWith('/')) then
            None
        else
            pointer.Substring(1).Split('/')
            |> Array.map (fun token -> token.Replace("~1", "/").Replace("~0", "~"))
            |> Array.fold
                (fun current token ->
                    match current with
                    | Some(JsonValue.Object members) ->
                        members
                        |> List.tryFind (fun (name, _) ->
                            String.Equals(name, token, StringComparison.Ordinal)
                        )
                        |> Option.map snd
                    | Some(JsonValue.Array items) when arrayIndex.IsMatch token ->
                        match Int32.TryParse(token, NumberStyles.None, invariant) with
                        | true, index when index < items.Length -> Some items[index]
                        | _ -> None
                    | _ -> None
                )
                (Some value)

    let private headerEnd (bytes: byte array) (from: int) =
        let rec find index =
            if
                index + 3
                >= bytes.Length
            then
                -1
            elif
                bytes[index] = 13uy
                && bytes[index + 1] = 10uy
                && bytes[index + 2] = 13uy
                && bytes[index + 3] = 10uy
            then
                index
            else
                find (index + 1)

        find from

    let private contentLength =
        Regex("^content-length[ \\t]*:[ \\t]*([0-9]+)[ \\t]*$", RegexOptions.IgnoreCase)

    /// The bodies of the Content-Length framed messages that make up stdout, in order. Stdout that is not
    /// a sequence of whole frames, each with one Content-Length header, is an error.
    let frames (stdout: byte array) : Result<byte array list, string> =
        let rec read offset (found: byte array list) =
            if offset = stdout.Length then
                Ok(List.rev found)
            else
                let number =
                    found.Length
                    + 1

                match headerEnd stdout offset with
                | -1 ->
                    Error
                        $"Frame {number} at byte {offset} has no header that ends with an empty line"
                | finish ->
                    let header =
                        Encoding.ASCII.GetString(
                            stdout,
                            offset,
                            finish
                            - offset
                        )

                    let lengths =
                        header.Split("\r\n")
                        |> Array.choose (fun line ->
                            let matched = contentLength.Match line

                            if matched.Success then
                                Some matched.Groups[1].Value
                            else
                                None
                        )

                    match lengths with
                    | [| text |] ->
                        match Int32.TryParse(text, NumberStyles.None, invariant) with
                        | true, length when
                            finish
                            + 4
                            + length
                            <= stdout.Length
                            ->
                            read
                                (finish
                                 + 4
                                 + length)
                                (stdout[finish + 4 .. finish
                                                      + 3
                                                      + length]
                                 :: found)
                        | true, length ->
                            Error
                                $"Frame {number} declares {length} bytes, and stdout ends {stdout.Length
                                                                                           - finish
                                                                                           - 4} bytes after its header"
                        | _ -> Error $"Frame {number} has a Content-Length that is too large"
                    | [||] -> Error $"Frame {number} at byte {offset} has no Content-Length header"
                    | _ ->
                        Error
                            $"Frame {number} at byte {offset} has more than one Content-Length header"

        read 0 []

    let private strictUtf8 = UTF8Encoding(false, true)

    /// A frame body as a JSON value: UTF-8 text holding one JSON value with no repeated member.
    let frameValue (body: byte array) : Result<JsonValue, string> =
        try
            Json.parse (strictUtf8.GetString body)
        with :? DecoderFallbackException ->
            Error "The body is not UTF-8 text"

    /// The stderr lines that are `morphir/command-event` objects with a string event member. Other lines
    /// are not events.
    let events (lines: StderrLine list) : WorkerEventLine list =
        lines
        |> List.choose (fun line ->
            match Json.parse line.Text with
            | Ok(JsonValue.Object members) ->
                let field name =
                    members
                    |> List.tryFind (fun (key, _) -> key = name)
                    |> Option.map snd

                match field "profile", field "event" with
                | Some(JsonValue.String profile), Some(JsonValue.String name) when
                    profile = Contract.CommandEventProfile
                    ->
                    Some
                        {
                            Name = name
                            AtMs = line.AtMs
                            Members = members
                        }
                | _ -> None
            | _ -> None
        )

    let private memberOf (name: string) (value: JsonValue) =
        match value with
        | JsonValue.Object members ->
            members
            |> List.tryFind (fun (key, _) -> key = name)
            |> Option.map snd
        | _ -> None

    let private eventMember (path: string) (event: WorkerEventLine) =
        path.Split('.')
        |> Array.fold
            (fun current name ->
                current
                |> Option.bind (memberOf name)
            )
            (Some(JsonValue.Object event.Members))

    let private eventText path event =
        match eventMember path event with
        | Some(JsonValue.String text) -> Some text
        | _ -> None

    /// The bounds of a started event: exactly memoryCeiling, instructionBudget and callDeadline, each
    /// {"enforced": true} or {"enforced": false, "reason": r} with r a reason of the contract.
    let startedBounds (event: WorkerEventLine) : Result<(Bound * BoundState) list, string> =
        match eventMember "bounds" event with
        | Some(JsonValue.Object members) ->
            let names =
                members
                |> List.map fst

            let expected =
                Contract.bounds
                |> List.map (fun bound -> bound.WireValue)

            if
                List.sort names
                <> List.sort expected
            then
                let shown = String.Join(", ", names)

                Error
                    $"started.bounds has the members [{shown}], not exactly memoryCeiling, instructionBudget and callDeadline"
            else
                Contract.bounds
                |> List.map (fun bound ->
                    let value =
                        members
                        |> List.find (fun (name, _) -> name = bound.WireValue)
                        |> snd

                    match value with
                    | JsonValue.Object [ "enforced", JsonValue.Bool true ] ->
                        Ok(bound, BoundState.Enforced)
                    | JsonValue.Object fields when fields.Length = 2 ->
                        match memberOf "enforced" value, memberOf "reason" value with
                        | Some(JsonValue.Bool false), Some(JsonValue.String reason) when
                            List.contains reason Contract.notEnforcedReasons
                            ->
                            Ok(bound, BoundState.NotEnforced reason)
                        | Some(JsonValue.Bool false), Some(JsonValue.String reason) ->
                            Error
                                $"started.bounds.{bound.WireValue} gives the reason \"{reason}\", which the contract does not list"
                        | _ ->
                            Error
                                $"started.bounds.{bound.WireValue} is not {{\"enforced\": false, \"reason\": r}}"
                    | other ->
                        Error
                            $"started.bounds.{bound.WireValue} is {Json.compact other}, not {{\"enforced\": true}} or {{\"enforced\": false, \"reason\": r}}"
                )
                |> List.fold
                    (fun state next ->
                        match state, next with
                        | Ok found, Ok item ->
                            Ok(
                                found
                                @ [ item ]
                            )
                        | Error message, _
                        | _, Error message -> Error message
                    )
                    (Ok [])
        | _ -> Error "started has no bounds object"

    /// The convention checks of every run: each started event's moduleSha256 is the module's digest and
    /// its bounds have the contract's shape; each fault event's fault has a string kind, may have a string
    /// message, and has nothing else.
    let conventions (case: WasmHostCase) (events: WorkerEventLine list) : (string * string) list = [
        for event in events do
            match event.Name with
            | "started" ->
                match eventText "moduleSha256" event with
                | Some sha when sha = case.Module.Sha256 -> ()
                | Some sha ->
                    yield
                        "started.moduleSha256",
                        $"started.moduleSha256 is {sha}, and the module's digest is {case.Module.Sha256}"
                | None -> yield "started.moduleSha256", "started has no string moduleSha256"

                match startedBounds event with
                | Ok _ -> ()
                | Error message -> yield "started.bounds", message
            | "fault" ->
                match eventMember "fault" event with
                | Some(JsonValue.Object members) ->
                    let names =
                        members
                        |> List.map fst

                    match memberOf "kind" (JsonValue.Object members) with
                    | Some(JsonValue.String _) -> ()
                    | _ -> yield "fault.members", "fault has no string kind"

                    match memberOf "message" (JsonValue.Object members) with
                    | None
                    | Some(JsonValue.String _) -> ()
                    | Some _ -> yield "fault.members", "fault.message is not a string"

                    match
                        names
                        |> List.filter (fun name ->
                            name
                            <> "kind"
                            && name
                               <> "message"
                        )
                    with
                    | [] -> ()
                    | extra ->
                        let shown = String.Join(", ", extra)

                        yield
                            "fault.members",
                            $"fault has members other than kind and message: {shown}"
                | _ -> yield "fault.members", "the fault event has no fault object"
            | _ -> ()
    ]

    /// exitCode: the worker's own exit status.
    let exitCode (expected: int) (observed: WorkerEnd) =
        match observed with
        | WorkerEnd.Exited code when code = expected -> None
        | WorkerEnd.Exited code -> Some $"The worker exited with status {code}, not {expected}"
        | WorkerEnd.StoppedByRunner ->
            Some
                $"The worker was still alive at the end of the run and the runner stopped it, so it did not exit with status {expected}"
        | WorkerEnd.NotStarted reason -> Some $"The worker did not start: {reason}"

    /// stdoutBytes: the byte count of all of stdout.
    let stdoutBytes (expected: int) (stdout: byte array) =
        if stdout.Length = expected then
            None
        else
            Some $"stdout has {stdout.Length} bytes, not {expected}"

    /// One frame matcher against one frame body.
    let frameMatches (sent: Frame list) (matcher: FrameMatcher) (body: byte array) =
        match frameValue body with
        | Error message -> Some message
        | Ok value ->
            match matcher with
            | FrameMatcher.Json expected ->
                if jsonEquals expected value then
                    None
                else
                    Some $"The body is {Json.compact value}, not equal to {Json.compact expected}"
            | FrameMatcher.SameAsFrame index ->
                match List.tryItem index sent with
                | None -> Some $"The run sends no frame {index + 1}"
                | Some frame ->
                    if jsonEquals frame.Value value then
                        None
                    else
                        Some
                            $"The body is {Json.compact value}, not equal to sent frame {index + 1} ({frame.Name})"
            | FrameMatcher.Pointers checks ->
                checks
                |> List.tryPick (fun check ->
                    match resolve check.Pointer value, check.Test with
                    | None, _ ->
                        Some
                            $"The pointer \"{check.Pointer}\" does not resolve in {Json.compact value}"
                    | Some found, PointerTest.Equals expected ->
                        if jsonEquals expected found then
                            None
                        else
                            Some
                                $"At \"{check.Pointer}\" the value is {Json.compact found}, not {Json.compact expected}"
                    | Some found, PointerTest.HasType kind ->
                        let actual = jsonType found

                        if actual = kind then
                            None
                        else
                            Some
                                $"At \"{check.Pointer}\" the value is of type {actual.WireValue}, not {kind.WireValue}"
                )

    /// stdoutFrames: every frame on stdout, in order, each matching its matcher.
    let stdoutFrames (sent: Frame list) (expected: FrameMatcher list) (stdout: byte array) =
        match frames stdout with
        | Error message -> Some $"stdout is not a sequence of Content-Length frames: {message}"
        | Ok bodies when
            bodies.Length
            <> expected.Length
            ->
            Some $"stdout has {bodies.Length} frames, not {expected.Length}"
        | Ok bodies ->
            List.zip expected bodies
            |> List.indexed
            |> List.tryPick (fun (index, (matcher, body)) ->
                frameMatches sent matcher body
                |> Option.map (fun message -> $"Frame {index + 1}: {message}")
            )

    /// Whether an event satisfies a matcher: the event name, and abi (started), code and messageIncludes
    /// (refused) or kind (fault) when the matcher fixes them.
    let eventMatches (matcher: EventMatcher) (event: WorkerEventLine) =
        event.Name = matcher.Event.WireValue
        && match matcher with
           | EventMatcher.Started None -> true
           | EventMatcher.Started(Some abi) -> eventText "abi" event = Some abi.WireValue
           | EventMatcher.Refused(code, includes) ->
               (match code with
                | None -> true
                | Some code -> eventText "error.code" event = Some code)
               && (
                   match includes, eventText "error.message" event with
                   | [], _ -> true
                   | _, None -> false
                   | includes, Some message ->
                       includes
                       |> List.forall (fun part -> message.Contains(part, StringComparison.Ordinal))
               )
           | EventMatcher.Fault None -> true
           | EventMatcher.Fault(Some kind) -> eventText "fault.kind" event = Some kind.WireValue
           | EventMatcher.Output -> true

    let private describe (matcher: EventMatcher) =
        match matcher with
        | EventMatcher.Started None -> "started"
        | EventMatcher.Started(Some abi) -> $"started with abi {abi.WireValue}"
        | EventMatcher.Refused(code, includes) ->
            let code =
                code
                |> Option.map (fun code -> $" with code {code}")
                |> Option.defaultValue ""

            let includes =
                match includes with
                | [] -> ""
                | parts ->
                    let shown =
                        String.Join(
                            ", ",
                            parts
                            |> List.map Json.quote
                        )

                    $" whose message includes {shown}"

            $"refused{code}{includes}"
        | EventMatcher.Fault None -> "fault"
        | EventMatcher.Fault(Some kind) -> $"fault of kind {kind.WireValue}"
        | EventMatcher.Output -> "output"

    let private summary (events: WorkerEventLine list) =
        match events with
        | [] -> "no event"
        | events ->
            events
            |> List.map (fun event ->
                match event.Name with
                | "refused" ->
                    let code =
                        eventText "error.code" event
                        |> Option.defaultValue "?"

                    let message =
                        eventText "error.message" event
                        |> Option.defaultValue ""

                    $"refused {code} {Json.quote message}"
                | "fault" ->
                    let kind =
                        eventText "fault.kind" event
                        |> Option.defaultValue "?"

                    $"fault {kind}"
                | "started" ->
                    let abi =
                        eventText "abi" event
                        |> Option.defaultValue "?"

                    $"started {abi}"
                | name -> name
            )
            |> fun parts -> String.Join("; ", parts)

    /// events: the matchers appear in this order among the events, possibly among others.
    let eventsInOrder (expected: EventMatcher list) (events: WorkerEventLine list) =
        let rec walk (matchers: EventMatcher list) (remaining: WorkerEventLine list) =
            match matchers with
            | [] -> None
            | matcher :: rest ->
                match
                    remaining
                    |> List.tryFindIndex (eventMatches matcher)
                with
                | Some index -> walk rest (List.skip (index + 1) remaining)
                | None ->
                    Some
                        $"No {describe matcher} event follows the events matched before it. The events: {summary events}"

        walk expected events

    /// noEvents: none of these events appears.
    let noEvents (forbidden: WorkerEvent list) (events: WorkerEventLine list) =
        match
            forbidden
            |> List.filter (fun name ->
                events
                |> List.exists (fun event -> event.Name = name.WireValue)
            )
        with
        | [] -> None
        | found ->
            let shown =
                String.Join(
                    ", ",
                    found
                    |> List.map (fun event -> event.WireValue)
                )

            Some $"stderr has the events {shown}. The events: {summary events}"

    /// The guest output: the output members of the output events, joined in their order with nothing
    /// between them. An output event that counts dropped bytes adds no text.
    let guestText (events: WorkerEventLine list) =
        events
        |> List.filter (fun event -> event.Name = "output")
        |> List.choose (eventText "output")
        |> String.concat ""

    /// guestOutput: the strings occur in this order in the guest output, each after the one before.
    let guestOutputInOrder (expected: string list) (text: string) =
        let rec walk (parts: string list) (from: int) =
            match parts with
            | [] -> None
            | part :: rest ->
                match text.IndexOf(part, from, StringComparison.Ordinal) with
                | -1 ->
                    Some
                        $"The guest output has no {Json.quote part} after the strings before it. The output: {Json.quote text}"
                | index ->
                    walk
                        rest
                        (index
                         + part.Length)

        walk expected 0

    /// guestOutputIncludes: each string occurs in the guest output, in any order.
    let guestOutputIncludes (expected: string list) (text: string) =
        match
            expected
            |> List.filter (fun part -> not (text.Contains(part, StringComparison.Ordinal)))
        with
        | [] -> None
        | missing ->
            let shown =
                String.Join(
                    ", ",
                    missing
                    |> List.map Json.quote
                )

            Some $"The guest output does not include {shown}. The output: {Json.quote text}"

    /// noGuestOutput: no string occurs in the guest output.
    let noGuestOutput (forbidden: string list) (text: string) =
        match
            forbidden
            |> List.filter (fun part -> text.Contains(part, StringComparison.Ordinal))
        with
        | [] -> None
        | found ->
            let shown =
                String.Join(
                    ", ",
                    found
                    |> List.map Json.quote
                )

            Some $"The guest output includes {shown}"

    let private seconds (milliseconds: int64) =
        decimal milliseconds
        / 1000m

    /// withinSeconds: the time from the end of the write of the last frame to the first fault event, or to
    /// the worker's exit, lies in the range.
    let withinSeconds
        (timing: Timing)
        (observation: RunObservation)
        (events: WorkerEventLine list)
        =
        match observation.LastFrameWrittenMs with
        | None ->
            Some
                "The worker did not take every frame, so the time from the last frame is not measured"
        | Some written ->
            let finish =
                match timing.Until with
                | Until.Fault ->
                    match
                        events
                        |> List.tryFind (fun event -> event.Name = "fault")
                    with
                    | Some event -> Ok event.AtMs
                    | None ->
                        Error "stderr has no fault event, so there is no time to the first fault"
                | Until.Exit ->
                    match observation.End with
                    | WorkerEnd.Exited _ -> Ok observation.EndMs
                    | ending ->
                        Error
                            $"The worker did not exit by itself ({ending}), so there is no time to its exit"

            match finish with
            | Error message -> Some message
            | Ok finish ->
                // The worker cannot answer a frame before it is written; a negative difference is only the order in
                // which two reader threads took their times.
                let elapsed =
                    seconds (
                        max
                            0L
                            (finish
                             - written)
                    )

                if
                    elapsed
                    >= timing.From
                    && elapsed
                       <= timing.To
                then
                    None
                else
                    let shown = elapsed.ToString("0.000", invariant)

                    Some
                        $"The time to the {timing.Until.WireValue} is {shown} seconds, outside {timing.From} to {timing.To}"

    /// Compares one run's observation with its expectation. The result names each matcher that the
    /// observation does not satisfy; an empty list means the run passed.
    let compare
        (case: WasmHostCase)
        (sent: Frame list)
        (expect: Expectation)
        (observation: RunObservation)
        : (string * string) list =
        let events = events observation.Stderr
        let text = guestText events

        let check name (result: string option) =
            result
            |> Option.map (fun message -> name, message)
            |> Option.toList

        conventions case events
        @ (expect.ExitCode
           |> Option.map (fun code -> check "exitCode" (exitCode code observation.End))
           |> Option.defaultValue [])
        @ (expect.StdoutBytes
           |> Option.map (fun bytes -> check "stdoutBytes" (stdoutBytes bytes observation.Stdout))
           |> Option.defaultValue [])
        @ (expect.StdoutFrames
           |> Option.map (fun matchers ->
               check "stdoutFrames" (stdoutFrames sent matchers observation.Stdout)
           )
           |> Option.defaultValue [])
        @ check "events" (eventsInOrder expect.Events events)
        @ check "noEvents" (noEvents expect.NoEvents events)
        @ check "guestOutput" (guestOutputInOrder expect.GuestOutput text)
        @ check "guestOutputIncludes" (guestOutputIncludes expect.GuestOutputIncludes text)
        @ check "noGuestOutput" (noGuestOutput expect.NoGuestOutput text)
        @ (expect.WithinSeconds
           |> Option.map (fun timing ->
               check "withinSeconds" (withinSeconds timing observation events)
           )
           |> Option.defaultValue [])
