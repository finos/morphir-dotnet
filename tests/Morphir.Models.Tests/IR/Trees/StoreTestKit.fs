/// What the store tests of both IR versions share: the kit's tree cases as trees of files, JSON compared by content
/// and order, and the refusal of a read or a write.
module Morphir.IR.Tests.Trees.StoreTestKit

open System.IO
open System.Text
open System.Text.Json
open Expecto
open Morphir.IR.Trees
open Morphir.Models.TestSupport
open Morphir.TestSupport.Fixtures

module Driver = DocumentTreeStoreDriver

/// JSON text with no whitespace and members in document order, so two spellings compare by content and order.
let compact (text: string) =
    use document = JsonDocument.Parse text
    use buffer = new MemoryStream()

    do
        use writer = new Utf8JsonWriter(buffer)
        document.RootElement.WriteTo writer

    Encoding.UTF8.GetString(buffer.ToArray())

/// The errors of a refused read or write, or a failure if it succeeds.
let refusal (result: Result<'a, StoreError list>) =
    match result with
    | Ok value -> failtestf "Expected a refusal, got %A" value
    | Result.Error errors -> errors

/// The code and the file of each error.
let codesAndFiles (errors: StoreError list) =
    errors
    |> List.map (fun e -> e.Code, e.File)

/// Whether a kit file says formatVersion 4, the integer, rather than a v3 release string.
let isV4Json (text: string) =
    use document = JsonDocument.Parse text
    document.RootElement.GetProperty("formatVersion").ValueKind = JsonValueKind.Number

/// Whether a kit tree case is a v4 tree: one whose manifest says formatVersion 4.
let isV4Case (kitCase: KitTreeCase) =
    match kitCase.Files.TryGetValue "manifest" with
    | true, manifest -> isV4Json manifest
    | _ -> false

/// The kit's v4 tree cases, pinned: 0003 to 0009.
let v4KitTreeIds = [ for number in 3..9 -> $"document-tree-%04d{number}" ]

/// The kit's v3 tree cases, pinned: 0011 to 0017.
let v3KitTreeIds = [ for number in 11..17 -> $"document-tree-%04d{number}" ]

/// The kit's v4 file spellings, pinned: 0001 and 0002.
let v4KitSpellingIds = [ "document-tree-0001"; "document-tree-0002" ]

/// The kit's v3 file spelling, pinned: 0010.
let v3KitSpellingIds = [ "document-tree-0010" ]

/// The kit's file spelling with this id.
let kitSpelling (id: string) =
    FixtureCatalog.Kit.FileSpellings
    |> Seq.tryFind (fun spelling -> spelling.Id = id)
    |> Option.defaultWith (fun () -> failtestf "The pinned kit has no file spelling '%s'" id)

/// The files of a kit tree case, each at its logical path with the extension of the JSON profile.
let kitFiles (kitCase: KitTreeCase) =
    [
        for KeyValue(logicalPath, text) in kitCase.Files ->
            logicalPath
            + ".json",
            text
    ]
    |> Driver.ofTexts

/// The path budget that a kit tree case's manifest says.
let kitBudget (kitCase: KitTreeCase) =
    use document = JsonDocument.Parse kitCase.Files["manifest"]
    document.RootElement.GetProperty("pathBudget").GetInt32()

/// The files a store wrote, each at its logical path and compacted, in the ordinal order of their paths.
let writtenAsKitFiles (files: Morphir.Files.VFileTree) =
    Driver.texts files
    |> Map.toList
    |> List.map (fun (location, text) ->
        location.Substring(
            0,
            location.Length
            - ".json".Length
        ),
        compact text
    )

/// The files of a kit tree case, each at its logical path and compacted, in the ordinal order of their paths.
let kitFilesCompacted (kitCase: KitTreeCase) =
    [ for KeyValue(logicalPath, text) in kitCase.Files -> logicalPath, compact text ]
    |> List.sortWith (fun (first, _) (second, _) -> System.String.CompareOrdinal(first, second))
