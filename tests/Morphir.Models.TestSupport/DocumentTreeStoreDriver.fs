/// <summary>
/// The test driver of <see cref="DocumentTreeStore"/>: a test writes, reads and reshapes trees of files through it,
/// with the store's default options, and never builds a file of a tree by hand.
/// </summary>
module Morphir.Models.TestSupport.DocumentTreeStoreDriver

open System.Text.Json
open System.Text.Json.Nodes
open Morphir.Files
open Morphir.IR.Trees

/// <summary>Writes a tree with the default options: a path budget of 4000 and no compaction.</summary>
let write (tree: StoredTree) : Result<VFileTree, StoreError list> =
    DocumentTreeStore.write WriteOptions.defaults tree

/// <summary>Reads a tree of files with the default limits.</summary>
let read (files: VFileTree) : Result<StoredTree * ReadReport, StoreError list> =
    DocumentTreeStore.read ReadOptions.defaults files

/// <summary>Writes a tree and reads the files back.</summary>
let roundTrip (tree: StoredTree) : Result<StoredTree * ReadReport, StoreError list> =
    write tree
    |> Result.bind read

/// <summary>The physical path that the store chose for a document of a tree it wrote.</summary>
let fileAt (address: DocumentAddress) (files: VFileTree) : string option =
    VFileTree.tryGet DocumentTreeStore.locations files
    |> Option.bind (Map.tryFind address)

/// <summary>The text of each file, by location.</summary>
let texts (files: VFileTree) : Map<string, string> =
    match VFileTree.toStringMap files with
    | Ok map -> map
    | Result.Error problem -> failwith (ContentError.describe problem)

/// <summary>A tree of the files, by location, with no metadata of the store.</summary>
let ofTexts (files: (string * string) list) : VFileTree =
    files
    |> List.map (fun (location, text) -> location, VFile.ofText location text)
    |> Map.ofList
    |> VFileTree.fromFileMap

/// <summary>The same files with every directory listing its entries in reverse order.</summary>
let rec reversed (files: VFileTree) : VFileTree =
    { files with
        Content =
            files.Content
            |> List.rev
            |> List.map (
                function
                | Directory subtree -> Directory(reversed subtree)
                | File file -> File file
            )
    }

/// <summary>The JSON of a file of a tree.</summary>
let jsonAt (location: string) (files: VFileTree) : JsonObject =
    match JsonNode.Parse((texts files)[location]) with
    | :? JsonObject as root -> root
    | _ -> failwith $"{location} does not hold a JSON object."

/// <summary>
/// The same files with one top-level member added to the file at a location: the member's JSON value is parsed
/// from <paramref name="json"/>.
/// </summary>
let withMember
    (location: string)
    (memberName: string)
    (json: string)
    (files: VFileTree)
    : VFileTree =
    let root = jsonAt location files
    root[memberName] <- JsonNode.Parse json

    texts files
    |> Map.add location (root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
    |> Map.toList
    |> ofTexts

/// <summary>The same files with one more file, of the given text, at a location, or that file replaced.</summary>
let withFile (location: string) (text: string) (files: VFileTree) : VFileTree =
    texts files
    |> Map.add location text
    |> Map.toList
    |> ofTexts

/// <summary>The same files without the file at a location.</summary>
let withoutFile (location: string) (files: VFileTree) : VFileTree =
    texts files
    |> Map.remove location
    |> Map.toList
    |> ofTexts

/// <summary>
/// The same files with every file whose location starts with <paramref name="fromPrefix"/> moved to start with
/// <paramref name="toPrefix"/> instead, such as a directory renamed.
/// </summary>
let moved (fromPrefix: string) (toPrefix: string) (files: VFileTree) : VFileTree =
    texts files
    |> Map.toList
    |> List.map (fun (location, text) ->
        if location.StartsWith(fromPrefix, System.StringComparison.Ordinal) then
            toPrefix
            + location.Substring fromPrefix.Length,
            text
        else
            location, text
    )
    |> ofTexts

/// <summary>The same files with a copy of the file at <paramref name="location"/> at <paramref name="copy"/>.</summary>
let copied (location: string) (copy: string) (files: VFileTree) : VFileTree =
    withFile copy ((texts files)[location]) files

/// <summary>
/// The same files with the doc of the module file at a location spelled as an array of lines, which the parent's
/// schema accepts beside a string (<c>ModuleManifestFile</c>, <c>doc</c>).
/// </summary>
let withModuleDocLines (location: string) (lines: string list) (files: VFileTree) : VFileTree =
    let doc = JsonArray()

    for line in lines do
        doc.Add(JsonValue.Create line)

    withMember location "doc" (doc.ToJsonString()) files

/// <summary>
/// The same files with a package metadata file, <c>package.json</c>, in a package directory such as
/// <c>pkg/acme/demo</c> or <c>deps/morphir/sdk/@</c>: the optional file of the parent's v4 tree page, "Package
/// Metadata", which is not part of the model.
/// </summary>
let withPackageMetadata (packageDirectory: string) (files: VFileTree) : VFileTree =
    withFile
        (packageDirectory
         + "/package.json")
        """{ "formatVersion": 4, "version": "1.2.0", "description": "Package metadata that the model has no place for." }"""
        files

/// <summary>
/// The same files with a <c>metadata</c> member in the module file at a location: the parent's v4 tree page shows
/// one under "Module Metadata", and the 4.0.0 schema of a module file has no such member.
/// </summary>
let withModuleMetadata (location: string) (files: VFileTree) : VFileTree =
    withMember location "metadata" """{ "tags": [ "domain" ], "deprecated": false }""" files

/// <summary>The same files with a JSON file at a location that is not part of the tree, such as notes beside it.</summary>
let withUnrelatedFile (location: string) (files: VFileTree) : VFileTree =
    withFile location """{ "note": "Not part of the tree." }""" files
