namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System.Text.Json

/// <summary>Writes a classic v3 document tree to the files of the parent's v3 tree specification.</summary>
module internal ClassicTreeWriter =
    let private writers
        (tree: ClassicDocumentTree<JsonElement, JsonElement>)
        : TreeFileWriters<_, _, _> =
        {
            Manifest = fun budget -> ClassicTreeFiles.writeManifest budget tree.Manifest
            Module = ClassicTreeFiles.writeModule
            Type = ClassicTreeFiles.writeType
            Value = ClassicTreeFiles.writeValue
            Listed = fun m -> m.TypeNames, m.ValueNames
        }

    let writeClassic
        (options: WriteOptions)
        (tree: ClassicDocumentTree<JsonElement, JsonElement>)
        : Result<Morphir.Files.VFileTree, StoreError list> =
        let joinErrors =
            match ClassicDocumentTree.join tree with
            | Ok _ -> []
            | Error errors -> errors

        TreeWriter.write
            options
            tree.Manifest.Package
            tree.Documents
            joinErrors
            (fun _ -> [])
            (writers tree)
