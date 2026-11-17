namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.IO
open System.Text.Json
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4

/// <summary>Writes a v4 document tree to the files of the parent's v4 tree specification.</summary>
module internal V4TreeWriter =
    open TreeLayout

    let private error code file message = StoreErrors.error code file message

    let private encoded path (written: Result<byte array, V4WriteError>) =
        match written with
        | Ok bytes -> Ok(path, V4TreeFiles.toText bytes)
        | Error e -> Error(error e.Rule (Some path) $"{e.Location}: {e.Message}")

    let private fileName (path: string) =
        path.Substring(
            path.LastIndexOf '/'
            + 1
        )

    let writeV4
        (options: WriteOptions)
        (tree: V4DocumentTree)
        : Result<Morphir.Files.VFileTree, StoreError list> =
        let own = tree.Manifest.Package
        let documents = Map.toList tree.Documents

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
            match V4DocumentTree.join tree with
            | Ok _ -> []
            | Error errors ->
                errors
                |> List.map (fun (e: TreeError) ->
                    error
                        e.Code
                        (e.Address
                         |> Option.bind wholePath)
                        e.Message
                )

        let annotationErrors =
            documents
            |> List.choose (fun (address, document) ->
                match document with
                | ModuleDoc m when not m.Annotations.IsEmpty ->
                    Some(
                        error
                            "invalid_distribution_shape"
                            (wholePath address)
                            $"Module annotations cannot be written to a document tree: {describe address} has {m.Annotations.Length}, and a 4.0.0 module file has no place for them."
                    )
                | _ -> None
            )

        let budgetErrors =
            if options.PathBudget < V4TreeFiles.MinimumPathBudget then
                [
                    error
                        "tree.unsupported_option"
                        None
                        $"The path budget is {options.PathBudget}; a tree's budget is at least {V4TreeFiles.MinimumPathBudget}."
                ]
            else
                []

        match
            budgetErrors
            @ joinErrors
            @ annotationErrors
            @ collisions whole
        with
        | _ :: _ as errors -> Error errors
        | [] ->
            layout options.PathBudget own whole
            |> Result.bind (fun (paths, fileNames) ->
                let pathOf address = paths[address]

                let files =
                    encoded
                        ManifestFile
                        (V4TreeFiles.writeManifest options.PathBudget tree.Manifest)
                    :: (documents
                        |> List.map (fun (address, document) ->
                            let path = pathOf address

                            match address, document with
                            | (Own(Module modulePath) | Dependency(_, Module modulePath)),
                              ModuleDoc m ->
                                let cut =
                                    Map.tryFind address fileNames
                                    |> Option.defaultValue Map.empty

                                let entries =
                                    m.TypeNames
                                    @ m.ValueNames
                                    |> List.choose (fun name ->
                                        Map.tryFind name cut
                                        |> Option.map (fun stem -> name, stem)
                                    )

                                encoded path (V4TreeFiles.writeModule modulePath m entries)
                            | _, TypeDoc t ->
                                let name, _ = (memberOf address).Value
                                encoded path (V4TreeFiles.writeType name t)
                            | _, ValueDoc v ->
                                let name, _ = (memberOf address).Value
                                encoded path (V4TreeFiles.writeValue name v)
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
                    |> Morphir.Files.VFileTree.set
                        StoreProperties.locations
                        (Map.add Manifest ManifestFile paths)
                    |> Ok
            )
