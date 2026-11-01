module Morphir.IR.Pipeline.Tests.ContentTests

open System
open System.IO
open System.Text
open Expecto
open Morphir.IR.Pipeline

/// A stream source that counts how many times it has been opened.
let private countingSource (bytes: byte[]) =
    let mutable opened = 0
    StreamSource(fun () ->
        opened <- opened + 1
        new MemoryStream(bytes) :> Stream), (fun () -> opened)

let private failingSource (failure: exn) =
    StreamSource(fun () -> raise failure)

let private textOf (file: VFile) =
    match VFile.readText file with
    | Ok text -> text
    | Result.Error error -> failtestf "expected text, got %A" error

[<Tests>]
let contentConstructionTests =
    testList "VFile content construction" [
        test "create should hold a string as text" {
            let file = VFile.create "a.txt" "hello"

            Expect.equal file.Content (Some (VFileContent.Text "hello")) "should be text"
            Expect.equal file.Path (Some "a.txt") "should keep the path"
        }

        test "create should hold a byte array as bytes" {
            let file = VFile.create "a.bin" [| 1uy; 2uy |]

            match file.Content with
            | Some (VFileContent.Bytes bytes) -> Expect.equal (bytes.ToArray()) [| 1uy; 2uy |] "should keep the bytes"
            | other -> failtestf "expected bytes, got %A" other
        }

        test "create should hold a stream source as a stream" {
            let source = StreamSource(fun () -> new MemoryStream() :> Stream)

            Expect.equal (VFile.create "a.bin" source).Content (Some (VFileContent.Stream source)) "should keep the source"
        }

        test "fromContent should have no path" {
            let file = VFile.fromContent "hello"

            Expect.equal file.Content (Some (VFileContent.Text "hello")) "should be text"
            Expect.isNone file.Path "should have no path"
        }

        test "non-inline constructors should match create" {
            Expect.equal (VFile.ofText "a" "x").Content (Some (VFileContent.Text "x")) "ofText"

            match (VFile.ofBytes "a" (ReadOnlyMemory [| 9uy |])).Content with
            | Some (VFileContent.Bytes bytes) -> Expect.equal (bytes.ToArray()) [| 9uy |] "ofBytes"
            | other -> failtestf "expected bytes, got %A" other

            let source = StreamSource(fun () -> new MemoryStream() :> Stream)
            Expect.equal (VFile.ofStream "a" source).Content (Some (VFileContent.Stream source)) "ofStream"
        }

        test "ofDiskFile should keep its own path and read another file" {
            let temp = Path.GetTempFileName()

            try
                File.WriteAllText(temp, "on disk")
                let file = VFile.ofDiskFile "sub/a.fs" temp

                Expect.equal file.Path (Some "sub/a.fs") "the VFile's path is not the disk path"
                Expect.equal (textOf file) "on disk" "content comes from the disk path"
            finally
                File.Delete temp
        }

        test "fromDisk should not open the file" {
            let file = VFile.fromDisk "does/not/exist.txt"

            Expect.equal file.Path (Some "does/not/exist.txt") "should keep the path"
            Expect.isSome file.Content "should have a source"
        }
    ]

[<Tests>]
let contentReadingTests =
    testList "VFile content reading" [
        test "readText should return text content" {
            Expect.equal (textOf (VFile.create "a" "hello")) "hello" "text"
        }

        test "readText should decode bytes as UTF-8" {
            Expect.equal (textOf (VFile.create "a" (Encoding.UTF8.GetBytes "café"))) "café" "bytes"
        }

        test "readText should read stream content" {
            let source, _ = countingSource (Encoding.UTF8.GetBytes "from a stream")

            Expect.equal (textOf (VFile.create "a" source)) "from a stream" "stream"
        }

        test "readText should strip a UTF-8 byte order mark" {
            let bytes = Array.append [| 0xEFuy; 0xBBuy; 0xBFuy |] (Encoding.UTF8.GetBytes "hi")

            Expect.equal (textOf (VFile.create "a" bytes)) "hi" "UTF-8 mark"
        }

        test "readText should honour a UTF-16 byte order mark" {
            let bytes = Array.append [| 0xFFuy; 0xFEuy |] (Encoding.Unicode.GetBytes "hi")

            Expect.equal (textOf (VFile.create "a" bytes)) "hi" "UTF-16 mark"
        }

        test "readText should keep line endings" {
            Expect.equal (textOf (VFile.create "a" (Encoding.UTF8.GetBytes "a\r\nb\n"))) "a\r\nb\n" "line endings"
        }

        test "readText should return empty text for absent content" {
            Expect.equal (textOf (VFile.fromPath "a")) "" "absent content"
        }

        test "reading a stream source twice should read from the start each time" {
            let source, opened = countingSource (Encoding.UTF8.GetBytes "again")
            let file = VFile.create "a" source

            Expect.equal (textOf file) "again" "first read"
            Expect.equal (textOf file) "again" "second read"
            Expect.equal (opened ()) 2 "each read should open a new stream"
        }

        test "a read failure should return the original exception and the file's path" {
            let failure = IOException "gone"
            let file = VFile.create "a.txt" (failingSource failure)

            match VFile.readText file with
            | Result.Error (ContentError.ReadFailed (path, cause)) ->
                Expect.equal path (Some "a.txt") "should name the file"
                Expect.isTrue (obj.ReferenceEquals(cause, failure)) "should keep the original exception"
            | other -> failtestf "expected a read failure, got %A" other
        }

        test "a missing file on disk should return a read failure" {
            match VFile.readText (VFile.fromDisk (Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()))) with
            | Result.Error (ContentError.ReadFailed (_, cause)) ->
                Expect.isTrue (cause :? IOException) "should keep the I/O exception"
            | other -> failtestf "expected a read failure, got %A" other
        }

        test "openRead should encode text as UTF-8 without a byte order mark" {
            match VFileContent.openRead (VFileContent.Text "é") with
            | Ok stream ->
                use stream = stream
                use buffer = new MemoryStream()
                stream.CopyTo buffer
                Expect.equal (buffer.ToArray()) [| 0xC3uy; 0xA9uy |] "two UTF-8 bytes and no mark"
            | Result.Error error -> failtestf "expected a stream, got %A" error
        }

        test "openRead should return bytes exactly, respecting the buffer's slice" {
            let slice = ReadOnlyMemory([| 1uy; 2uy; 3uy; 4uy |], 1, 2)

            match VFileContent.openRead (VFileContent.Bytes slice) with
            | Ok stream ->
                use stream = stream
                use buffer = new MemoryStream()
                stream.CopyTo buffer
                Expect.equal (buffer.ToArray()) [| 2uy; 3uy |] "only the slice"
            | Result.Error error -> failtestf "expected a stream, got %A" error
        }

        test "openRead should return a typed error when a source fails to open" {
            Expect.isError (VFileContent.openRead (VFileContent.Stream (failingSource (IOException "gone")))) "typed error"
        }

        test "describe should name the path and cause" {
            let description = ContentError.describe (ContentError.ReadFailed (Some "a.txt", IOException "gone"))

            Expect.stringContains description "a.txt" "path"
            Expect.stringContains description "gone" "cause"
        }
    ]

[<Tests>]
let messageDescriptionTests =
    let baseMessage = VMessage.create Error "it broke"

    testList "VMessage.describe" [
        test "should use path, rule, location and message" {
            let message =
                { baseMessage with
                    Path = Some "model.json"
                    RuleId = Some "parse"
                    Location = Some "$.modules"
                }

            Expect.equal (VMessage.describe message) "model.json: parse at $.modules: it broke" "all parts"
        }

        test "should leave out parts the message does not have" {
            Expect.equal (VMessage.describe baseMessage) "it broke" "message only"
            Expect.equal (VMessage.describe { baseMessage with Path = Some "a" }) "a: it broke" "path"
            Expect.equal (VMessage.describe { baseMessage with RuleId = Some "r" }) "r: it broke" "rule"
            Expect.equal (VMessage.describe { baseMessage with Location = Some "l" }) "at l: it broke" "location"
        }
    ]

[<Tests>]
let reportTests =
    testList "VFile.report" [
        test "should add a complete message after earlier ones" {
            let message = { VMessage.create Error "second" with RuleId = Some "r"; Location = Some "l" }
            let file = VFile.empty |> VFile.info "first" |> VFile.report message

            Expect.equal file.Messages.[1] message "should keep every field"
            Expect.equal file.Messages.[0].Message "first" "should keep order"
        }
    ]
