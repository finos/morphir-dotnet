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
