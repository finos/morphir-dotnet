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

/// The files of a shared-name case have, for each name, one cut stem for its type file and its value file, each path
/// within the budget, and one fileNames entry in the module file that maps the name to that stem.
let expectOneStemForEachSharedName (case: DocumentTreeMother.SharedNameCase) (files: Morphir.Files.VFileTree) =
    let modulePath =
        match case.Module with
        | Morphir.IR.Trees.Own(Morphir.IR.Trees.Module path) -> path
        | other -> failtestf "The case's module is an own module, not %O" other

    let moduleFile = (Driver.fileAt case.Module files).Value
    let moduleText = (Driver.texts files)[moduleFile]
    let fileNames = (nonNull ((Driver.jsonAt moduleFile files)["fileNames"])).AsObject()

    let stemOf (address: Morphir.IR.Trees.DocumentAddress) (suffix: string) =
        let path = (Driver.fileAt address files).Value
        Expect.isLessThanOrEqual path.Length DocumentTreeMother.defaultBudget $"The path of {address} fits the budget"
        Expect.stringEnds path suffix $"The file of {address} ends with {suffix}"
        let fileName = path.Substring(path.LastIndexOf '/' + 1)
        fileName.Substring(0, fileName.Length - suffix.Length)

    Expect.equal fileNames.Count case.Names.Length "One fileNames entry for each shared name"

    for shared in case.Names do
        let key = Morphir.IR.CanonicalNames.nameToString shared
        let typeStem = stemOf (Morphir.IR.Trees.Own(Morphir.IR.Trees.TypeDef(modulePath, shared))) ".type.json"
        let valueStem = stemOf (Morphir.IR.Trees.Own(Morphir.IR.Trees.ValueDef(modulePath, shared))) ".value.json"

        Expect.equal typeStem valueStem $"The type and the value {key} have one stem"

        match TreeStems.parseStem valueStem with
        | Ok(CutStem(prefix, digest)) ->
            Expect.isTrue (TreeStems.matchesCut prefix digest shared) $"The cut stem of {key} belongs to its name"
        | other -> failtestf "Expected a cut stem for %s, got %A" key other

        Expect.equal (string fileNames[key]) valueStem $"fileNames maps {key} to its one stem"

        // The parser keeps the last of two equal keys, so the text is counted too.
        let quoted = "\"" + key + "\":"

        Expect.equal
            (moduleText.Replace(" ", "").Split([| quoted |], System.StringSplitOptions.None).Length - 1)
            1
            $"The module file names {key} once in fileNames"

/// A minimal tree carrying an undeclared contextual envelope $schema on its manifest.
let undeclaredManifestSchemaProvider (fileName: string) =
    let content =
        if fileName.EndsWith(".yaml", System.StringComparison.Ordinal) then
            "$schema: https://example.org/unknown.schema.json\ncontext: {}\ndocument:\n  formatVersion: 4\n"
        else
            """{"$schema":"https://example.org/unknown.schema.json","context":{},"document":{"formatVersion":4}}"""

    Map.ofList [ fileName, Morphir.Files.VFile.ofText fileName content ]
    |> Morphir.Files.VFileTree.fromFileMap
    |> Morphir.Files.VFileProviders.asFileProvider

/// A frozen bounded orders tree as an IFileProvider.
let ordersTreeProvider (version: string) (profile: string) =
    let prefix = Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.Directory + version + "/" + profile + "/"

    Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.Files
    |> Seq.filter (fun entry -> entry.RelativePath.StartsWith(prefix, System.StringComparison.Ordinal))
    |> Seq.map (fun entry ->
        let location = entry.RelativePath.Substring(prefix.Length)

        location,
        Morphir.Files.VFile.ofBytes
            location
            (System.ReadOnlyMemory(Morphir.TestSupport.Fixtures.FixtureCatalog.ReadBytes entry))
    )
    |> Map.ofSeq
    |> Morphir.Files.VFileTree.fromFileMap
    |> Morphir.Files.VFileProviders.asFileProvider

/// An IFileProvider wrapper that invokes a callback on each GetDirectoryContents call (for mid-page cancellation tests).
let interceptingDirectoryProvider
    (onGetDirectoryContents: string -> unit)
    (inner: Microsoft.Extensions.FileProviders.IFileProvider)
    : Microsoft.Extensions.FileProviders.IFileProvider =
    { new Microsoft.Extensions.FileProviders.IFileProvider with
        member _.GetDirectoryContents(subpath) =
            onGetDirectoryContents subpath
            inner.GetDirectoryContents(subpath)

        member _.GetFileInfo(subpath) = inner.GetFileInfo(subpath)
        member _.Watch(filter) = inner.Watch(filter)
    }

/// A valid flat v4 library tree containing <paramref name="moduleCount"/> modules under <c>pkg/acme/flat/</c>.
let flatPackageTreeProvider (moduleCount: int) =
    let manifest =
        """{"formatVersion":4,"distribution":"Library","package":"acme/flat","pathBudget":240,"dependencies":[]}"""

    let files =
        seq {
            yield "manifest.json", Morphir.Files.VFile.ofText "manifest.json" manifest

            for i in 0 .. moduleCount - 1 do
                let name = sprintf "mod-%04d" i
                let location = $"pkg/acme/flat/{name}/module.json"
                let content = $"{{\"formatVersion\":4,\"path\":\"{name}\",\"access\":\"Public\",\"types\":[],\"values\":[]}}"
                yield location, Morphir.Files.VFile.ofText location content
        }
        |> Map.ofSeq
        |> Morphir.Files.VFileTree.fromFileMap

    Morphir.Files.VFileProviders.asFileProvider files

/// An in-memory v4 plain JSON tree with <paramref name="moduleCount"/> modules nested under four shared
/// 100-character directory segments so logical paths overflow an entry-count-scaled discovery cache reservation.
let longPathFlatPackageTreeProvider (moduleCount: int) =
    let seg1 = System.String('a', 100)
    let seg2 = System.String('b', 100)
    let seg3 = System.String('c', 100)
    let seg4 = System.String('d', 100)
    let prefix = $"{seg1}/{seg2}/{seg3}/{seg4}"
    let manifest =
        """{"formatVersion":4,"distribution":"Library","package":"acme/flat","pathBudget":4000,"dependencies":[]}"""

    let files =
        seq {
            yield "manifest.json", Morphir.Files.VFile.ofText "manifest.json" manifest

            for i in 0 .. moduleCount - 1 do
                let name = sprintf "mod-%04d" i
                let modulePath = $"{prefix}/{name}"
                let location = $"pkg/acme/flat/{modulePath}/module.json"
                let content = $"{{\"formatVersion\":4,\"path\":\"{modulePath}\",\"access\":\"Public\",\"types\":[],\"values\":[]}}"
                yield location, Morphir.Files.VFile.ofText location content
        }
        |> Map.ofSeq
        |> Morphir.Files.VFileTree.fromFileMap

    Morphir.Files.VFileProviders.asFileProvider files

/// A frozen bounded orders tree with one named file replaced by an undeclared contextual envelope $schema.
let ordersTreeWithUndeclaredSchema (version: string) (profile: string) (targetFile: string) =
    let prefix = Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.Directory + version + "/" + profile + "/"

    let undeclared =
        if targetFile.EndsWith(".yaml", System.StringComparison.Ordinal) then
            "$schema: https://example.org/unknown.schema.json\ncontext: {}\ndocument:\n  formatVersion: 4\n"
        else
            """{"$schema":"https://example.org/unknown.schema.json","context":{},"document":{"formatVersion":4}}"""

    Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.Files
    |> Seq.filter (fun entry -> entry.RelativePath.StartsWith(prefix, System.StringComparison.Ordinal))
    |> Seq.map (fun entry ->
        let location = entry.RelativePath.Substring(prefix.Length)

        if location = targetFile then
            location, Morphir.Files.VFile.ofText location undeclared
        else
            location,
            Morphir.Files.VFile.ofBytes
                location
                (System.ReadOnlyMemory(Morphir.TestSupport.Fixtures.FixtureCatalog.ReadBytes entry))
    )
    |> Map.ofSeq
    |> Morphir.Files.VFileTree.fromFileMap
    |> Morphir.Files.VFileProviders.asFileProvider
