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
// At most 40 links are followed in total, as SYMLOOP_MAX allows on Linux and more than macOS allows.
let private physicalPath (path: string) =
    let mutable followed = 0

    let rec resolve (path: string) =
        let root =
            Path.GetPathRoot path
            |> Option.ofObj
            |> Option.defaultValue "/"

        let rec walk (current: string) parts =
            match parts with
            | [] -> current
            | (part: string) :: rest ->
                let next = Path.Combine(current, part)

                let target =
                    try
                        (FileInfo next).LinkTarget
                    with
                    | :? UnauthorizedAccessException
                    | :? IOException ->
                        refuse "local.home_unreadable" $"A parent folder of --home cannot be read: '{next}'."

                match target with
                | null -> walk next rest
                | target ->
                    followed <- followed + 1

                    if followed > 40 then
                        refuse "local.home_links" "The parent folders of --home have too many levels of links."

                    walk (resolve (Path.GetFullPath(target, current))) rest

        walk root (path.Substring(root.Length).Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray)

    resolve path

/// An absolute Home. Off Windows its parent folders are physical: the installed host refuses a source document below
/// a link, and setup reads its readiness sources below Home. On macOS, $TMPDIR is below the link /var. The last part
/// is not followed, so a Home that is itself a link is refused when it is opened, as before.
let homePath (home: string) =
    if not (Path.IsPathFullyQualified home) then
        refuse "local.home_not_absolute" "--home must be an absolute isolated Morphir Home."

    let full = Path.TrimEndingDirectorySeparator(Path.GetFullPath home)

    if OperatingSystem.IsWindows() then
        full
    else
        match Path.GetDirectoryName full, Path.GetFileName full with
        | null, _
        | _, "" -> full
        | parent, name -> Path.Combine(physicalPath parent, name)

/// The result of a completed step of the historical local route, whose candidate, receipt and selection are
/// local-toolchains 0.1.0-preview.1 documents. A refusal is written by <see cref="refusal"/>.
let result command status (candidate: Sha256) =
    Value.Object [
        "$schema", Value.String(LocalCandidateDocument.schema "result")
        "command", Value.String command
        "status", Value.String status
        "origin", Value.String "local-developer"
        "candidateSha256", Value.String candidate.Text

        if status <> "planned" then
            "receiptPath", Value.String(LocalCandidateDocument.receiptPath candidate)

        if
            status = "selected"
            || status = "shown"
        then
            "selectionPath", Value.String LocalCandidateDocument.selectionPath
    ]
    |> JsonText.toBytes

/// A refused setup, select or show: a local-toolchains 0.1.0-preview.4 result. The origin is the origin of the route
/// or of the generation the command acted on, local-developer or release-checksum.
let refusal command origin code message =
    Value.Object [
        "$schema", Value.String(ComposedDocuments.schemaFor "0.1.0-preview.4" "result")
        "command", Value.String command
        "status", Value.String "refused"
        "origin", Value.String origin
        "diagnostic", Value.Object [ "code", Value.String code; "message", Value.String message ]
    ]
    |> JsonText.toBytes
