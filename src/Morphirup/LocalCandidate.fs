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

// The path with each link among its existing parts replaced by its target. The parts that do not exist yet stay.
let rec private physicalPath depth (path: string) =
    if depth > 32 then
        refuse "local.home_links" "--home has too many levels of links."

    let root =
        Path.GetPathRoot path
        |> Option.ofObj
        |> Option.defaultValue "/"

    let rec walk (current: string) parts =
        match parts with
        | [] -> current
        | (part: string) :: rest ->
            let next = Path.Combine(current, part)

            match (FileInfo next).LinkTarget with
            | null -> walk next rest
            | target -> walk (physicalPath (depth + 1) (Path.GetFullPath(target, current))) rest

    walk root (path.Substring(root.Length).Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray)

/// An absolute Home. Off Windows it is the physical path: the installed host refuses a source document below a link,
/// and setup reads its readiness sources below Home. On macOS, $TMPDIR is below the link /var.
let homePath (home: string) =
    if not (Path.IsPathFullyQualified home) then
        refuse "local.home_not_absolute" "--home must be an absolute isolated Morphir Home."

    let full = Path.TrimEndingDirectorySeparator(Path.GetFullPath home)

    if OperatingSystem.IsWindows() then full
    else Path.TrimEndingDirectorySeparator(physicalPath 0 full)

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
