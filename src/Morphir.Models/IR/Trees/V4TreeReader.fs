namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open Morphir.IR.V4

/// <summary>Reads a v4 document tree from the files of the parent's v4 tree specification.</summary>
module internal V4TreeReader =
    let private readers: TreeFileReaders<_, _, _> = {
        Module = V4TreeFiles.readModule
        Type = V4TreeFiles.readType
        Value = V4TreeFiles.readValue
        Listed = fun (m: V4ModuleDocument) -> m.TypeNames, m.ValueNames
    }

    let readV4With
        (ctx: TreeFileSource.TreeDecodeContext)
        (manifestLocation: string)
        (manifestCompacted: bool)
        (manifestFile: V4ManifestFile)
        (files: (string * Morphir.Files.VFile) list)
        : Result<StoredTree * Distribution * ReadReport, StoreError list> =
        let manifest = manifestFile.Manifest

        TreeReader.readTreeWith
            ctx
            manifestLocation
            manifestCompacted
            manifestFile.PathBudget
            manifest.Package
            (V4DocumentTree.holdsDefinitions manifest.Kind)
            readers
            (fun documents ->
                ({
                    Manifest = manifest
                    Documents = documents
                }
                : V4DocumentTree)
            )
            V4DocumentTree.join
            V4Tree
            manifestFile.Dropped
            files

    let readV4
        (manifestCompacted: bool)
        (options: ReadOptions)
        (manifestFile: V4ManifestFile)
        (files: (string * Morphir.Files.VFile) list)
        : Result<StoredTree * ReadReport, StoreError list> =
        let ctx =
            TreeFileSource.createDecodeContext
                TreeProfile.Json
                options.MaxFileBytes
                Morphir.IR.Files.ContextLimits.draft1.MaxDepth
                None
                System.Threading.CancellationToken.None

        readV4With ctx TreeLayout.ManifestFile manifestCompacted manifestFile files
        |> Result.map (fun (tree, _, report) -> tree, report)
