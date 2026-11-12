// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module internal Morphirup.LocalCandidate

open System
open System.IO
open Morphir.Toolchain
open Morphir.Extensions.Installation

exception Refused of code: string * message: string

let refuse code message = raise (Refused(code, message))

let reader =
    function
    | Ok value -> value
    | Error(failure: ReaderRefusal) ->
        refuse
            ("local."
             + Code.readerName failure.Code)
            ("Refused local document at "
             + DocumentPath.render failure.Path)

let readBytes maximum path =
    use input = FileSafety.openInput path

    if input.Length > maximum then
        refuse "local.document_too_large" "Local document exceeds its recorded bound."

    use output = new MemoryStream()
    let buffer = Array.zeroCreate<byte> 65536
    let mutable reading = true

    while reading do
        let count = input.Read(buffer, 0, buffer.Length)

        if count = 0 then
            reading <- false
        else
            if
                output.Length
                + int64 count > maximum
            then
                refuse "local.document_too_large" "Local document grew past its bound."

            output.Write(buffer, 0, count)

    output.ToArray()

let homePath (home: string) =
    if not (Path.IsPathFullyQualified home) then
        refuse "local.home_not_absolute" "--home must be an absolute isolated Morphir Home."

    Path.TrimEndingDirectorySeparator(Path.GetFullPath home)

let result command status (candidate: Sha256 option) diagnostic =
    Value.Object [
        "$schema", Value.String(LocalCandidateDocument.schema "result")
        "command", Value.String command
        "status", Value.String status
        "origin", Value.String "local-developer"
        match candidate with
        | Some identity ->
            "candidateSha256", Value.String identity.Text

            if
                status
                <> "planned"
                && status
                   <> "refused"
            then
                "receiptPath", Value.String(LocalCandidateDocument.receiptPath identity)

            if
                status = "selected"
                || status = "shown"
            then
                "selectionPath", Value.String LocalCandidateDocument.selectionPath
        | None -> ()
        match diagnostic with
        | Some(code, message) ->
            "diagnostic",
            Value.Object [ "code", Value.String code; "message", Value.String message ]
        | None -> ()
    ]
    |> JsonText.toBytes
