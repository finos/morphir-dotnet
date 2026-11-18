namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System.Text.Json

/// <summary>Reads a classic v3 document tree from the files of the parent's v3 tree specification.</summary>
module internal ClassicTreeReader =
    let private readers: TreeFileReaders<_, _, _> = {
        Module = ClassicTreeFiles.readModule
        Type = ClassicTreeFiles.readType
        Value = ClassicTreeFiles.readValue
        Listed = fun (m: ClassicModuleDocument) -> m.TypeNames, m.ValueNames
    }

    let readClassic
        (manifestCompacted: bool)
        (options: ReadOptions)
        (manifestFile: ClassicManifestFile)
        (files: (string * Morphir.Files.VFile) list)
        : Result<StoredTree * ReadReport, StoreError list> =
        let manifest = manifestFile.Manifest

        TreeReader.readTree
            manifestCompacted
            options
            manifestFile.PathBudget
            manifest.Package
            (ClassicDocumentTree.holdsDefinitions manifest.Kind)
            readers
            (fun documents ->
                ({
                    Manifest = manifest
                    Documents = documents
                }
                : ClassicDocumentTree<JsonElement, JsonElement>)
            )
            ClassicDocumentTree.join
            ClassicTree
            []
            files
