namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open Morphir.IR.V4

/// <summary>Writes a v4 document tree to the files of the parent's v4 tree specification.</summary>
module internal V4TreeWriter =
    let private error code file message = StoreErrors.error code file message

    let private writers (tree: V4DocumentTree) : TreeFileWriters<_, _, _> = {
        Manifest = fun budget -> V4TreeFiles.writeManifest budget tree.Manifest
        Module = V4TreeFiles.writeModule
        Type = V4TreeFiles.writeType
        Value = V4TreeFiles.writeValue
        Listed = fun m -> m.TypeNames, m.ValueNames
    }

    /// A module with annotations, which a 4.0.0 module file has no place for.
    let private annotationErrors (tree: V4DocumentTree) (pathOf: DocumentAddress -> string option) =
        tree.Documents
        |> Map.toList
        |> List.choose (fun (address, document) ->
            match document with
            | ModuleDoc m when not m.Annotations.IsEmpty ->
                Some(
                    error
                        "invalid_distribution_shape"
                        (pathOf address)
                        $"Module annotations cannot be written to a document tree: {TreeLayout.describe address} has {m.Annotations.Length}, and a 4.0.0 module file has no place for them."
                )
            | _ -> None
        )

    let writeV4
        (options: WriteOptions)
        (tree: V4DocumentTree)
        : Result<Morphir.Files.VFileTree, StoreError list> =
        let joinErrors =
            match V4DocumentTree.join tree with
            | Ok _ -> []
            | Error errors -> errors

        TreeWriter.write
            options
            tree.Manifest.Package
            tree.Documents
            joinErrors
            (annotationErrors tree)
            (writers tree)
