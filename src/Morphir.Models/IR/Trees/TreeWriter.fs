namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open Morphir.IR
open Morphir.Json.V4

/// <summary>How one IR version writes the files of a tree.</summary>
type internal TreeFileWriters<'moduleDoc, 'typ, 'value> = {
    /// <summary>Writes the manifest file, with the path budget of the tree.</summary>
    Manifest: int -> Result<byte array, V4WriteError>
    /// <summary>Writes a module file, with the stems that the budget cut, in document order.</summary>
    Module: ModulePath -> 'moduleDoc -> (Name * string) list -> Result<byte array, V4WriteError>
    /// <summary>Writes a type file.</summary>
    Type: Name -> 'typ -> Result<byte array, V4WriteError>
    /// <summary>Writes a value file.</summary>
    Value: Name -> 'value -> Result<byte array, V4WriteError>
    /// <summary>The names of a module document's types and of its values, in document order.</summary>
    Listed: 'moduleDoc -> Name list * Name list
}

/// <summary>
/// Writes the files of a tree, for either IR version: the path of each document with the stems that the path budget
/// cuts, the refusals that come before any file is encoded, and the tree of files with its declared order and the
/// location of each document. The version gives its codecs (<see cref="TreeFileWriters`3"/>) and the errors of its
/// join.
/// </summary>
module internal TreeWriter =
    open TreeLayout

    let private error code file message = StoreErrors.error code file message

    /// <summary>The physical path of each document of a tree that a store wrote.</summary>
    let locations: Morphir.Files.Property<Map<DocumentAddress, string>> =
        Morphir.Files.Property.create "morphir.document-tree.locations"

    let private encoded path (written: Result<byte array, V4WriteError>) =
        match written with
        | Ok bytes -> Ok(path, TreeFileCodec.toText bytes)
        | Error e -> Error(error e.Rule (Some path) $"{e.Location}: {e.Message}")

    let private fileName (path: string) =
        path.Substring(
            path.LastIndexOf '/'
            + 1
        )

    /// <summary>
    /// Writes a tree's files, or every error: a path budget under the minimum, the errors of the tree's join
    /// (<paramref name="joinErrors"/>), those of its shape (<paramref name="shapeErrors"/>, given the file of each
    /// address), two documents whose files would have one path, and a path that no cut fits. The manifest is first in
    /// the declared order, then each document in the ordinal order of its logical path.
    /// </summary>
    let write
        (options: WriteOptions)
        (own: PackageName)
        (documents: Map<DocumentAddress, TreeDocument<'moduleDoc, 'typ, 'value>>)
        (joinErrors: TreeError list)
        (shapeErrors: (DocumentAddress -> string option) -> StoreError list)
        (writers: TreeFileWriters<'moduleDoc, 'typ, 'value>)
        : Result<Morphir.Files.VFileTree, StoreError list> =
        let documents = Map.toList documents

        let whole =
            (Manifest, ManifestFile)
            :: (documents
                |> List.map (fun (address, _) ->
                    address,
                    DocumentAddress.logicalPath own address
                    + Extension
                ))

        let wholePath address =
            whole
            |> List.tryFind (fun (candidate, _) -> candidate = address)
            |> Option.map snd

        let joinErrors =
            joinErrors
            |> List.map (fun (e: TreeError) ->
                error
                    e.Code
                    (e.Address
                     |> Option.bind wholePath)
                    e.Message
            )

        let budgetErrors =
            if options.PathBudget < TreeFileCodec.MinimumPathBudget then
                [
                    error
                        "tree.unsupported_option"
                        None
                        $"The path budget is {options.PathBudget}; a tree's budget is at least {TreeFileCodec.MinimumPathBudget}."
                ]
            else
                []

        match
            budgetErrors
            @ joinErrors
            @ shapeErrors wholePath
            @ collisions whole
        with
        | _ :: _ as errors -> Error errors
        | [] ->
            layout options.PathBudget own whole
            |> Result.bind (fun (paths, fileNames) ->
                let pathOf address = paths[address]

                let files =
                    encoded ManifestFile (writers.Manifest options.PathBudget)
                    :: (documents
                        |> List.map (fun (address, document) ->
                            let path = pathOf address

                            match address, document with
                            | (Own(Module modulePath) | Dependency(_, Module modulePath)),
                              ModuleDoc m ->
                                let cut =
                                    Map.tryFind address fileNames
                                    |> Option.defaultValue Map.empty

                                let typeNames, valueNames = writers.Listed m

                                let entries =
                                    typeNames
                                    @ valueNames
                                    |> List.choose (fun name ->
                                        Map.tryFind name cut
                                        |> Option.map (fun stem -> name, stem)
                                    )

                                encoded path (writers.Module modulePath m entries)
                            | _, TypeDoc t ->
                                let name, _ = (memberOf address).Value
                                encoded path (writers.Type name t)
                            | _, ValueDoc v ->
                                let name, _ = (memberOf address).Value
                                encoded path (writers.Value name v)
                            // join refuses a document that does not fit its address, so this is not reached.
                            | _, ModuleDoc _ ->
                                Error(
                                    error
                                        "tree.mismatched_document"
                                        (Some path)
                                        $"The document at {address} does not fit its address."
                                )
                        ))

                match
                    files
                    |> List.choose (
                        function
                        | Error e -> Some e
                        | Ok _ -> None
                    )
                with
                | _ :: _ as errors -> Error errors
                | [] ->
                    let written =
                        files
                        |> List.choose (
                            function
                            | Ok file -> Some file
                            | Error _ -> None
                        )

                    // The manifest first, then every document in the ordinal order of its logical path.
                    let order =
                        ManifestFile
                        :: (documents
                            |> List.map (fun (address, _) ->
                                address, DocumentAddress.logicalPath own address
                            )
                            |> byOrdinal
                            |> List.map (fun (address, _) -> pathOf address))

                    let byPath = Map.ofList written

                    Morphir.Files.VFileTree.fromFileMap (
                        written
                        |> List.map (fun (path, text) ->
                            path, Morphir.Files.VFile.ofText (fileName path) text
                        )
                        |> Map.ofList
                    )
                    |> Morphir.Files.VFileTree.set
                        Morphir.Files.VFileTree.declaredOrder
                        (order
                         |> List.filter byPath.ContainsKey)
                    |> Morphir.Files.VFileTree.set locations (Map.add Manifest ManifestFile paths)
                    |> Ok
            )
