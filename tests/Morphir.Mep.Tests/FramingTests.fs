// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Mep.Tests.FramingTests

open System
open System.IO
open System.Text
open System.Threading
open Expecto
open Morphir.Mep

type private OneByteStream(bytes: byte array) =
    inherit MemoryStream(bytes)

    override this.ReadAsync(buffer: Memory<byte>, ct: CancellationToken) =
        base.ReadAsync(buffer.Slice(0, min 1 buffer.Length), ct)

[<Tests>]
let tests =
    testList
        "Content-Length framing"
        [ testCaseAsync "split reads preserve UTF-8 bytes and an empty string id"
          <| async {
              let body =
                  Encoding.UTF8.GetBytes """{"jsonrpc":"2.0","id":"","result":"héllo 🌍"}"""

              let header = Encoding.ASCII.GetBytes $"Content-Length: {body.Length}\r\n\r\n"
              use stream = new OneByteStream(Array.append header body)

              let! result =
                  ContentLengthFraming.ReadAsync(stream, CancellationToken.None)
                  |> Async.AwaitTask

              match result with
              | FrameRead.Message actual -> Expect.sequenceEqual actual body "Complete UTF-8 body"
              | _ -> failtest "Expected a frame"
          }
          testCaseAsync "writes one Content-Length frame using the UTF-8 byte count"
          <| async {
              let body = Encoding.UTF8.GetBytes "héllo 🌍"
              use stream = new MemoryStream()

              do!
                  ContentLengthFraming.WriteAsync(stream, body.AsMemory(), CancellationToken.None)
                  |> Async.AwaitTask

              let expected =
                  Array.append (Encoding.ASCII.GetBytes $"Content-Length: {body.Length}\r\n\r\n") body

              Expect.sequenceEqual (stream.ToArray()) expected "Exact framed bytes"
          }
          for label, header, expected in
              [ "negative length", "Content-Length: -1\r\n\r\n", FramingFailure.InvalidLength
                "fractional length", "Content-Length: 1.5\r\n\r\n", FramingFailure.InvalidLength
                "repeated length", "Content-Length: 1\r\nContent-Length: 1\r\n\r\n", FramingFailure.InvalidLength
                "length overflow", "Content-Length: 18446744073709551616\r\n\r\n", FramingFailure.InvalidLength
                "missing length", "Content-Type: application/json\r\n\r\n", FramingFailure.MissingLength
                "log with CRLF", "some log message\r\n", FramingFailure.NotFramed
                "log with LF", "some log message\n", FramingFailure.NotFramed
                "non-ASCII header", "Content-Length: é\r\n\r\n", FramingFailure.NotFramed
                "truncated header", "Content-Length: 2\r\n", FramingFailure.TruncatedHeader
                "truncated body", "Content-Length: 2\r\n\r\nx", FramingFailure.TruncatedBody ] do
              testCaseAsync label
              <| async {
                  use stream = new MemoryStream(Encoding.UTF8.GetBytes header)

                  let! result =
                      ContentLengthFraming.ReadAsync(stream, CancellationToken.None)
                      |> Async.AwaitTask

                  Expect.equal result (FrameRead.Refused expected) "Terminal protocol failure"
              }
          testCaseAsync "refuses a declared oversized body before reading it"
          <| async {
              let header =
                  Encoding.ASCII.GetBytes $"Content-Length: {ContentLengthFraming.MaxBodyBytes + 1}\r\n\r\n"

              use stream = new MemoryStream(Array.append header [| 42uy |])

              let! result =
                  ContentLengthFraming.ReadAsync(stream, CancellationToken.None)
                  |> Async.AwaitTask

              Expect.equal result (FrameRead.Refused FramingFailure.BodyTooLarge) "Bound before allocation"
              Expect.equal stream.Position (int64 header.Length) "Body has not been read"
          }
          testCaseAsync "refuses an oversized header without consuming its body"
          <| async {
              use stream =
                  new MemoryStream(Encoding.ASCII.GetBytes("X: " + String('a', 8192) + "\r\n\r\nbody"))

              let! result =
                  ContentLengthFraming.ReadAsync(stream, CancellationToken.None)
                  |> Async.AwaitTask

              Expect.equal result (FrameRead.Refused FramingFailure.HeaderTooLarge) "Header is bounded"
              Expect.equal stream.Position 8193L "Stops at the first excessive header byte"
          }
          testCaseAsync "clean EOF between frames is distinct from truncation"
          <| async {
              use stream = new MemoryStream()

              let! result =
                  ContentLengthFraming.ReadAsync(stream, CancellationToken.None)
                  |> Async.AwaitTask

              Expect.equal result FrameRead.EndOfStream "No partial header"
          }
          testCaseAsync "accepts the exact header bound and an empty body"
          <| async {
              let prefix = "Content-Length: 0\r\nX: "
              let suffix = "\r\n\r\n"

              let header =
                  prefix
                  + String('a', ContentLengthFraming.MaxHeaderBytes - prefix.Length - suffix.Length)
                  + suffix

              use stream = new MemoryStream(Encoding.ASCII.GetBytes header)

              let! result =
                  ContentLengthFraming.ReadAsync(stream, CancellationToken.None)
                  |> Async.AwaitTask

              Expect.equal result (FrameRead.Message Array.empty) "The bound is inclusive"
          }
          testCaseAsync "reads adjacent frames without consuming the next header"
          <| async {
              use stream =
                  new MemoryStream(Encoding.ASCII.GetBytes "Content-Length: 1\r\n\r\nxcontent-length: 1\r\n\r\ny")

              let! first =
                  ContentLengthFraming.ReadAsync(stream, CancellationToken.None)
                  |> Async.AwaitTask

              let! second =
                  ContentLengthFraming.ReadAsync(stream, CancellationToken.None)
                  |> Async.AwaitTask

              Expect.equal first (FrameRead.Message [| byte 'x' |]) "First frame"
              Expect.equal second (FrameRead.Message [| byte 'y' |]) "Header names are case insensitive"
          }
          testCase "pre-cancellation leaves the stream unread"
          <| fun _ ->
              use stream = new MemoryStream(Encoding.ASCII.GetBytes "Content-Length: 1\r\n\r\nx")
              use ct = new CancellationTokenSource()
              ct.Cancel()

              Expect.throwsT<OperationCanceledException>
                  (fun () ->
                      ContentLengthFraming.ReadAsync(stream, ct.Token).GetAwaiter().GetResult()
                      |> ignore)
                  "Cancellation propagates"

              Expect.equal stream.Position 0L "No bytes read"
          testProperty "arbitrary binary bodies round-trip exactly"
          <| fun (input: byte array) ->
              let body = input |> Array.truncate 4096

              use stream = new MemoryStream()
              ContentLengthFraming.WriteAsync(stream, body.AsMemory(), CancellationToken.None).GetAwaiter().GetResult()
              stream.Position <- 0L

              ContentLengthFraming.ReadAsync(stream, CancellationToken.None).GetAwaiter().GetResult() = FrameRead.Message
                  body ]
