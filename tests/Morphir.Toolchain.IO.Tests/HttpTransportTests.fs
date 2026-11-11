// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Toolchain.IO.Tests.HttpTransportTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.IO
open Morphir.Toolchain.IO.Tests.ScriptedIO

type private StreamingContent(stream: Stream, declared: int64 option) =
    inherit HttpContent()
    let mutable eagerCopies = 0
    member _.EagerCopies = eagerCopies
    override _.TryComputeLength(length: byref<int64>) =
        match declared with
        | Some value -> length <- value; true
        | None -> length <- 0L; false
    override _.SerializeToStreamAsync(_, _) =
        eagerCopies <- eagerCopies + 1
        Task.FromException(InvalidOperationException "The HTTP response must remain streamed")
    override _.CreateContentReadStreamAsync() = Task.FromResult stream
    override _.CreateContentReadStreamAsync(_: CancellationToken) = Task.FromResult stream
    override _.Dispose(disposing) =
        if disposing then stream.Dispose()
        base.Dispose disposing

type private Handler(send: HttpRequestMessage -> CancellationToken -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, ct) = send request ct

[<Tests>]
let tests = testList "HTTP acquisition transport" [
    testCaseTask "HTTP response streaming composes with bounded target verification and private publication" (fun () -> task {
        let bytes = [| 1uy; 2uy; 3uy |]
        use input = new ScriptedStream([ Bytes bytes[..0]; Bytes bytes[1..] ])
        use handler = new Handler(fun _ _ -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK, Content = new StreamingContent(input, None))))
        use transport = new HttpAcquisitionTransport(handler)
        let store = MemoryStore()
        let clock = ManualClock()
        let target = TargetPath.tryParse "objects/tool.bin" |> Result.defaultWith (fun error -> failwithf "%A" error)
        let expected: AuthenticatedTarget = { Length = 3UL; Sha256 = Sha256.ofBytes bytes; Custom = None }
        let request = {
            Source = Uri "https://caller-configured.invalid/object"
            MaxBytes = 3UL
            Deadline = clock.GetUtcNow().AddMinutes 1
            Mode = AcquisitionMode.Online
        }
        let! result = Acquisition(transport, clock).AcquireTargetAsync(request, target, expected, store, CancellationToken.None)
        Expect.equal result (Ok()) "The production HTTP and acquisition adapters compose without a real network"
        Expect.equal store.Objects[expected.Sha256.Text] bytes "Only checked streamed bytes are published"
        Expect.isTrue input.Disposed "HTTP ownership is released"
    })

    testCaseTask "open returns headers without draining or buffering content" (fun () -> task {
        use input = new ScriptedStream([ Bytes [| 1uy; 2uy |] ])
        use content = new StreamingContent(input, Some 2L)
        use handler = new Handler(fun request ct -> task {
            Expect.equal request.Method HttpMethod.Get "Explicit GET"
            Expect.equal request.RequestUri (Uri "https://caller-configured.invalid/metadata") "No default repository"
            Expect.isTrue ct.CanBeCanceled "The operation cancellation reaches HTTP"
            return new HttpResponseMessage(HttpStatusCode.OK, Content = content)
        })
        use transport = new HttpAcquisitionTransport(handler)
        use ct = new CancellationTokenSource()
        let! response = (transport :> IAcquisitionTransport).OpenAsync(Uri "https://caller-configured.invalid/metadata", ct.Token)
        Expect.equal response.DeclaredLength (Some 2L) "The header is retained for the bounded reader"
        Expect.isEmpty input.Requests "Open never drains the body"
        Expect.equal content.EagerCopies 0 "No HttpContent buffering"
        do! response.DisposeAsync()
        Expect.isTrue input.Disposed "Disposing the response releases its stream"
    })

    for status in [ HttpStatusCode.Redirect; HttpStatusCode.PartialContent; HttpStatusCode.NotFound; HttpStatusCode.InternalServerError ] do
        testCaseTask $"HTTP {int status} is refused without reading the body" (fun () -> task {
            use input = new ScriptedStream([ Bytes [| 1uy |] ])
            use handler = new Handler(fun _ _ ->
                let response = new HttpResponseMessage(status, Content = new StreamingContent(input, None))
                response.Headers.Location <- Uri "https://different-source.invalid/"
                Task.FromResult response)
            use transport = new HttpAcquisitionTransport(handler)
            try
                let! response = (transport :> IAcquisitionTransport).OpenAsync(Uri "https://caller-configured.invalid/object", CancellationToken.None)
                do! response.DisposeAsync()
                failtest "Only a complete 200 response is accepted"
            with :? HttpRequestException -> ()
            Expect.isEmpty input.Requests "No error or redirect body read"
            Expect.isTrue input.Disposed "Refused response disposed"
        })

    testCaseTask "encoded bodies are refused rather than silently changing hashed bytes" (fun () -> task {
        use input = new ScriptedStream([ Bytes [| 1uy |] ])
        use handler = new Handler(fun _ _ ->
            let content = new StreamingContent(input, None)
            content.Headers.ContentEncoding.Add "gzip"
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK, Content = content)))
        use transport = new HttpAcquisitionTransport(handler)
        try
            let! response = (transport :> IAcquisitionTransport).OpenAsync(Uri "https://caller-configured.invalid/object", CancellationToken.None)
            do! response.DisposeAsync()
            failtest "Encoded bodies cannot be treated as exact object bytes"
        with :? HttpRequestException -> ()
        Expect.isTrue input.Disposed "Encoded response disposed"
    })

    testCaseTask "cancellation while opening HTTP propagates without conversion to unavailable" (fun () -> task {
        let waiting = started ()
        use handler = new Handler(fun _ ct -> task {
            waiting.TrySetResult() |> ignore
            do! Task.Delay(Timeout.InfiniteTimeSpan, ct)
            return new HttpResponseMessage(HttpStatusCode.OK)
        })
        use transport = new HttpAcquisitionTransport(handler)
        use ct = new CancellationTokenSource()
        let work = (transport :> IAcquisitionTransport).OpenAsync(Uri "https://caller-configured.invalid/object", ct.Token)
        do! awaitPhase waiting.Task work ct.Cancel
        ct.Cancel()
        try
            let! _ = work.WaitAsync(TimeSpan.FromSeconds 5.0)
            failtest "Caller cancellation must propagate"
        with :? OperationCanceledException -> ()
    })
]
