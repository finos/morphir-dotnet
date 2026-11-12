// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Reads the pinned jsonrpc transcripts: each fenced jsonrpc block is one message with a direction.
module Morphir.Mep.Tests.Transcripts

open System
open System.IO
open System.Text.Json.Nodes

/// Who sent a message.
type Direction =
    | HostToExtension
    | ExtensionToHost

/// One message of a transcript. Entry is the zero-based message index that the expected findings use.
type Entry =
    { Entry: int
      Direction: Direction
      Text: string }

    member this.Node = JsonNode.Parse this.Text |> nonNull

/// Held by a test while it builds a C# consumer project with `dotnet build`. Two such builds at once race in
/// the NuGet restore ("Cannot create a file when that file already exists"), so the tests take turns.
let consumerBuild = new System.Threading.SemaphoreSlim(1, 1)

/// The path of a pinned fixture.
let fixture name =
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mep", name)

/// The messages of a pinned transcript, in order.
let read name =
    let lines = File.ReadAllLines(fixture name)

    let rec loop index entries =
        if index >= lines.Length then
            List.rev entries
        else
            match lines[index].Trim() with
            | "```jsonrpc host -> extension"
            | "```jsonrpc extension -> host" as fence ->
                let direction =
                    if fence.EndsWith "host -> extension" then
                        HostToExtension
                    else
                        ExtensionToHost

                let finish =
                    seq { index + 1 .. lines.Length - 1 }
                    |> Seq.find (fun line -> lines[line].Trim() = "```")

                let text = String.Join("\n", lines[index + 1 .. finish - 1])

                loop
                    (finish + 1)
                    ({ Entry = List.length entries
                       Direction = direction
                       Text = text }
                     :: entries)
            | _ -> loop (index + 1) entries

    loop 0 []

/// The three pinned transcripts.
let all =
    [ "session-model-transport.md"; "lifecycle-violations.md"; "protocol-errors.md" ]

/// The stream tests block on Wait and Result while handlers continue on the thread pool. With Expecto's parallel
/// test lists on a slow runner, the pool can run out of threads and delay a continuation past a test timeout.
/// The stream test lists call this before they run, and run their own tests one at a time.
let ensureThreads =
    lazy
        (let workers, ports = System.Threading.ThreadPool.GetMinThreads()
         System.Threading.ThreadPool.SetMinThreads(max workers 64, max ports 64) |> ignore)
