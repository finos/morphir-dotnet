// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.ComparerTests

open System.Text
open Expecto
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.FakeWorker

let private json (text: string) =
    match Json.parse text with
    | Ok value -> value
    | Error message -> failtest message

let private frameOf (body: string) =
    let bytes = Encoding.UTF8.GetBytes body
    Array.append (Encoding.ASCII.GetBytes $"Content-Length: {bytes.Length}\r\n\r\n") bytes

let private line (atMs: int64) (body: string) = {
    AtMs = atMs
    Text =
        $"{{\"profile\":\"morphir/command-event\",\"contractVersion\":\"0.1.0\",\"command\":\"extension worker\",{body}}}"
}

let private observation (stdout: byte array) (stderr: StderrLine list) (ending: WorkerEnd) = {
    Arguments = []
    FramesWritten = 1
    LastFrameWrittenMs = Some 1000L
    Stdout = stdout
    Stderr = stderr
    End = ending
    EndMs = 2000L
}

let private events (lines: StderrLine list) = Comparer.events lines

let private none = Expect.isNone
let private some (result: string option) message = Expect.isSome result message

let private case = lazy (suiteCase "extism/echo").Case

let private startedLine sha bounds =
    line
        1100L
        $"\"event\":\"started\",\"abi\":\"extism-kernel-v1\",\"moduleSha256\":\"{sha}\",\"buildIdentity\":\"{Identity}\",\"bounds\":{bounds}"

let private enforcedBounds =
    "{\"memoryCeiling\":{\"enforced\":true},\"instructionBudget\":{\"enforced\":true},\"callDeadline\":{\"enforced\":true}}"

[<Tests>]
let tests =
    testList "WASM host comparer: one test per convention rule (task 1.5)" [
        test "JSON equality ignores object member order" {
            Expect.isTrue
                (Comparer.jsonEquals (json """{"a":1,"b":[true]}""") (json """{"b":[true],"a":1}"""))
                "member order"
        }

        test "JSON equality compares numbers by value" {
            Expect.isTrue (Comparer.jsonEquals (json "1") (json "1.0")) "1 and 1.0"
            Expect.isTrue (Comparer.jsonEquals (json "100") (json "1e2")) "100 and 1e2"
            Expect.isFalse (Comparer.jsonEquals (json "1") (json "2")) "1 and 2"
        }

        test "JSON equality keeps array order, kinds and exact strings" {
            Expect.isFalse (Comparer.jsonEquals (json "[1,2]") (json "[2,1]")) "array order"

            Expect.isFalse
                (Comparer.jsonEquals (json "\"1\"") (json "1"))
                "a string is not a number"

            Expect.isFalse (Comparer.jsonEquals (json "\"A\"") (json "\"a\"")) "ordinal strings"

            Expect.isFalse
                (Comparer.jsonEquals (json """{"a":1}""") (json """{"a":1,"b":2}"""))
                "an extra member"
        }

        test "An RFC 6901 pointer resolves members, indexes and escapes" {
            let value = json """{"a/b":{"m~n":[10,20]},"":5}"""

            Expect.equal
                (Comparer.resolve "" value)
                (Some value)
                "the empty pointer is the whole value"

            Expect.equal
                (Comparer.resolve "/a~1b/m~0n/1" value)
                (Some(json "20"))
                "~1 and ~0, and an index"

            Expect.equal (Comparer.resolve "/" value) (Some(json "5")) "the empty member name"
            Expect.isNone (Comparer.resolve "/a~1b/m~0n/01" value) "no leading zero"
            Expect.isNone (Comparer.resolve "/a~1b/m~0n/2" value) "past the end"
            Expect.isNone (Comparer.resolve "a" value) "a pointer starts with /"
        }

        test "stdout is read as Content-Length frames" {
            let bytes = Array.append (frameOf "{\"a\":1}") (frameOf "{}")

            Expect.equal
                (Comparer.frames bytes
                 |> Result.map List.length)
                (Ok 2)
                "two frames"

            Expect.isError
                (Comparer.frames (Array.append (frameOf "{}") (Encoding.ASCII.GetBytes "junk")))
                "trailing bytes are not a frame"

            Expect.isError
                (Comparer.frames (Encoding.ASCII.GetBytes "Content-Length: 9\r\n\r\n{}"))
                "a short body"

            Expect.isOk
                (Comparer.frames (
                    Encoding.ASCII.GetBytes
                        "Content-Type: application/json\r\ncontent-length: 2\r\n\r\n{}"
                ))
                "another header and any case"
        }

        test "stdoutBytes counts every byte of stdout" {
            none (Comparer.stdoutBytes 0 [||]) "empty"
            some (Comparer.stdoutBytes 0 [| 1uy |]) "one byte"
        }

        test "stdoutFrames needs every frame, in order and no other" {
            let sent = (case.Force()).Runs.Head.Frames
            let one = frameOf (Json.compact sent.Head.Value)
            none (Comparer.stdoutFrames sent [ FrameMatcher.SameAsFrame 0 ] one) "the sent frame"

            some
                (Comparer.stdoutFrames sent [ FrameMatcher.SameAsFrame 0 ] (Array.append one one))
                "one more frame"

            some (Comparer.stdoutFrames sent [] one) "Some [] means no frame"
        }

        test "A json frame matcher is JSON value equality" {
            none
                (Comparer.frameMatches
                    []
                    (FrameMatcher.Json(json """{"id":1,"r":2}"""))
                    (Encoding.UTF8.GetBytes """{"r":2.0,"id":1}"""))
                "equal"

            some
                (Comparer.frameMatches
                    []
                    (FrameMatcher.Json(json """{"id":1}"""))
                    (Encoding.UTF8.GetBytes """{"id":2}"""))
                "different"
        }

        test "A sameAsFrame matcher compares with the body of the run's frame" {
            let sent = (case.Force()).Runs.Head.Frames

            let reordered =
                """{"params":{"protocolVersion":"0.2.0-draft.1"},"method":"morphir.initialize","id":1,"jsonrpc":"2.0"}"""

            none
                (Comparer.frameMatches
                    sent
                    (FrameMatcher.SameAsFrame 0)
                    (Encoding.UTF8.GetBytes reordered))
                "a host may write it again"

            some
                (Comparer.frameMatches
                    sent
                    (FrameMatcher.SameAsFrame 0)
                    (Encoding.UTF8.GetBytes "{}"))
                "another value"
        }

        test "A pointers matcher needs every check: equals a value, or has a type" {
            let matcher =
                FrameMatcher.Pointers [
                    {
                        Pointer = "/id"
                        Test = PointerTest.Equals(json "1")
                    }
                    {
                        Pointer = "/result/n"
                        Test = PointerTest.HasType JsonType.Number
                    }
                ]

            none
                (Comparer.frameMatches
                    []
                    matcher
                    (Encoding.UTF8.GetBytes """{"id":1,"result":{"n":7}}"""))
                "both"

            some
                (Comparer.frameMatches
                    []
                    matcher
                    (Encoding.UTF8.GetBytes """{"id":1,"result":{"n":"7"}}"""))
                "another type"

            some
                (Comparer.frameMatches [] matcher (Encoding.UTF8.GetBytes """{"id":1}"""))
                "a pointer that does not resolve"
        }

        test "A frame body that is not UTF-8 JSON matches nothing" {
            some
                (Comparer.frameMatches [] (FrameMatcher.Json(json "{}")) [| 0xffuy; 0xfeuy |])
                "not UTF-8"

            some
                (Comparer.frameMatches
                    []
                    (FrameMatcher.Json(json "{}"))
                    (Encoding.UTF8.GetBytes "{"))
                "not JSON"

            some
                (Comparer.frameMatches
                    []
                    (FrameMatcher.Json(json """{"a":1}"""))
                    (Encoding.UTF8.GetBytes """{"a":1,"a":1}"""))
                "a repeated member"
        }

        test "exitCode is the worker's own exit status" {
            none (Comparer.exitCode 3 (WorkerEnd.Exited 3)) "the status"
            some (Comparer.exitCode 3 (WorkerEnd.Exited 1)) "another status"

            some
                (Comparer.exitCode 3 WorkerEnd.StoppedByRunner)
                "a worker the runner stopped did not exit"
        }

        test "events appear in this order, possibly among others" {
            let found =
                events [
                    line 1L "\"event\":\"started\",\"abi\":\"extism-kernel-v1\""
                    line 2L "\"event\":\"output\",\"output\":\"x\""
                    line 3L "\"event\":\"fault\",\"fault\":{\"kind\":\"trap\"}"
                ]

            none
                (Comparer.eventsInOrder
                    [ EventMatcher.Started(Some Profile.ExtismKernelV1); EventMatcher.Fault None ]
                    found)
                "in order among others"

            some
                (Comparer.eventsInOrder [ EventMatcher.Fault None; EventMatcher.Started None ] found)
                "out of order"

            some
                (Comparer.eventsInOrder [ EventMatcher.Started(Some Profile.MorphirBytesV1) ] found)
                "another abi"
        }

        test "A refused matcher fixes the code and needs each message fragment" {
            let found =
                events [
                    line
                        1L
                        "\"event\":\"refused\",\"error\":{\"code\":\"extension.abi_unsupported\",\"message\":\"env.open_socket is not allowed\"}"
                ]

            none
                (Comparer.eventsInOrder
                    [
                        EventMatcher.Refused(
                            Some "extension.abi_unsupported",
                            [ "env."; "open_socket" ]
                        )
                    ]
                    found)
                "code and fragments"

            some
                (Comparer.eventsInOrder
                    [ EventMatcher.Refused(Some "extension.abi_mismatch", []) ]
                    found)
                "another code"

            some
                (Comparer.eventsInOrder
                    [ EventMatcher.Refused(None, [ "open_socket"; "handle" ]) ]
                    found)
                "a missing fragment"
        }

        test "A fault matcher fixes the kind, and without one takes any kind" {
            let found =
                events [ line 1L "\"event\":\"fault\",\"fault\":{\"kind\":\"memory-limit\"}" ]

            none (Comparer.eventsInOrder [ EventMatcher.Fault None ] found) "any kind"

            none
                (Comparer.eventsInOrder [ EventMatcher.Fault(Some FaultKind.MemoryLimit) ] found)
                "the kind"

            some
                (Comparer.eventsInOrder [ EventMatcher.Fault(Some FaultKind.Trap) ] found)
                "another kind"
        }

        test "noEvents names events that must not appear" {
            let found =
                events [
                    line 1L "\"event\":\"refused\",\"error\":{\"code\":\"c\",\"message\":\"m\"}"
                ]

            none (Comparer.noEvents [ WorkerEvent.Started; WorkerEvent.Fault ] found) "absent"
            some (Comparer.noEvents [ WorkerEvent.Refused ] found) "present"
        }

        test "Only morphir/command-event lines are events" {
            let found =
                Comparer.events [
                    { AtMs = 1L; Text = "plain text" }
                    {
                        AtMs = 2L
                        Text = "{\"profile\":\"other\",\"event\":\"fault\"}"
                    }
                    line 3L "\"event\":\"exited\",\"exitCode\":0"
                ]

            Expect.equal
                (found
                 |> List.map (fun event -> event.Name))
                [ "exited" ]
                "one event"
        }

        test "Every started event's moduleSha256 is the module's digest" {
            let case = case.Force()

            Expect.isEmpty
                (Comparer.conventions
                    case
                    (events [ startedLine case.Module.Sha256 enforcedBounds ]))
                "the digest"

            Expect.equal
                (Comparer.conventions
                    case
                    (events [ startedLine (String.replicate 64 "0") enforcedBounds ])
                 |> List.map fst)
                [ "started.moduleSha256" ]
                "another digest"
        }

        test "Every started event's bounds have exactly the three members of the contract's shape" {
            let case = case.Force()

            let check bounds =
                Comparer.conventions case (events [ startedLine case.Module.Sha256 bounds ])
                |> List.map fst

            Expect.isEmpty
                (check
                    "{\"memoryCeiling\":{\"enforced\":true},\"instructionBudget\":{\"enforced\":false,\"reason\":\"no-instruction-metering\"},\"callDeadline\":{\"enforced\":true}}")
                "a reason of the contract"

            Expect.equal
                (check
                    "{\"memoryCeiling\":{\"enforced\":true},\"callDeadline\":{\"enforced\":true}}")
                [ "started.bounds" ]
                "a missing member"

            Expect.equal
                (check
                    "{\"memoryCeiling\":{\"enforced\":true},\"instructionBudget\":{\"enforced\":false,\"reason\":\"slow\"},\"callDeadline\":{\"enforced\":true}}")
                [ "started.bounds" ]
                "a reason the contract does not list"

            Expect.equal
                (check
                    "{\"memoryCeiling\":{\"enforced\":true},\"instructionBudget\":{\"enforced\":false},\"callDeadline\":{\"enforced\":true}}")
                [ "started.bounds" ]
                "not enforced without a reason"
        }

        test "Every fault event's fault has a kind, may have a message, and has nothing else" {
            let case = case.Force()

            let check body =
                Comparer.conventions case (events [ line 1L body ])
                |> List.map fst

            Expect.isEmpty
                (check "\"event\":\"fault\",\"fault\":{\"kind\":\"trap\",\"message\":\"m\"}")
                "kind and message"

            Expect.equal
                (check "\"event\":\"fault\",\"fault\":{\"message\":\"m\"}")
                [ "fault.members" ]
                "no kind"

            Expect.equal
                (check
                    "\"event\":\"fault\",\"fault\":{\"kind\":\"trap\",\"code\":\"extension.guest_trap\"}")
                [ "fault.members" ]
                "a code"
        }

        test "guestOutput needs the strings in order in the joined output text" {
            let found =
                events [
                    line 1L "\"event\":\"output\",\"output\":\"alpha \""
                    line 2L "\"event\":\"output\",\"droppedBytes\":12"
                    line 3L "\"event\":\"output\",\"output\":\"beta\""
                ]

            let text = Comparer.guestText found
            Expect.equal text "alpha beta" "joined in order; dropped bytes add no text"
            none (Comparer.guestOutputInOrder [ "alpha"; "beta" ] text) "in order"
            some (Comparer.guestOutputInOrder [ "beta"; "alpha" ] text) "out of order"
        }

        test "guestOutputIncludes needs each string in any order, and noGuestOutput none" {
            none (Comparer.guestOutputIncludes [ "b"; "a" ] "a b") "any order"
            some (Comparer.guestOutputIncludes [ "c" ] "a b") "missing"
            none (Comparer.noGuestOutput [ "c" ] "a b") "absent"
            some (Comparer.noGuestOutput [ "b" ] "a b") "present"
        }

        test
            "withinSeconds measures from the end of the last frame's write to the first fault or to the exit" {
            let toFault = {
                Until = Until.Fault
                From = 1m
                To = 2m
            }

            let toExit = {
                Until = Until.Exit
                From = 0m
                To = 0.5m
            }

            let observed =
                observation
                    [||]
                    [
                        line 2500L "\"event\":\"fault\",\"fault\":{\"kind\":\"call-deadline\"}"
                        line 2900L "\"event\":\"fault\",\"fault\":{\"kind\":\"trap\"}"
                    ]
                    (WorkerEnd.Exited 1)

            none
                (Comparer.withinSeconds toFault observed (events observed.Stderr))
                "1.5 seconds to the first fault"

            some
                (Comparer.withinSeconds { toFault with To = 1.4m } observed (events observed.Stderr))
                "outside the range"

            some
                (Comparer.withinSeconds toExit observed [])
                "1 second to the exit is outside 0 to 0.5"

            some (Comparer.withinSeconds toFault observed []) "no fault event"

            some
                (Comparer.withinSeconds
                    { toExit with To = 5m }
                    { observed with
                        End = WorkerEnd.StoppedByRunner
                    }
                    [])
                "a worker that did not exit"

            some
                (Comparer.withinSeconds
                    toFault
                    { observed with
                        LastFrameWrittenMs = None
                    }
                    (events observed.Stderr))
                "a worker that did not take every frame"
        }

        test "compare names each failed matcher of a run" {
            let case = case.Force()
            let run = case.Runs.Head

            let expect =
                match run.Expect with
                | RunExpectation.Always expect -> expect
                | RunExpectation.Alternatives _ -> failtest "one expectation"

            let observed = observation [| 1uy |] [] (WorkerEnd.Exited 5)

            let names =
                Comparer.compare case run.Frames expect observed
                |> List.map fst

            Expect.containsAll names [ "exitCode"; "stdoutFrames"; "events" ] "each failed matcher"
        }
    ]
