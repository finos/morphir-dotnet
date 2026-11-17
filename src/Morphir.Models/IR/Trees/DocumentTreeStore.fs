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


/// <summary>
/// Maps a document tree to and from the files of the parent's document-tree specifications, on a
/// <see cref="VFileTree"/>. The store makes no file-system call: the same tree comes from or goes to the disk, an
/// embedded resource, an archive or memory.
/// </summary>
/// <remarks>
/// <para>
/// A v4 tree is written in the JSON profile: <c>manifest.json</c>, <c>module.json</c> for each module, and
/// <c>&lt;stem&gt;.type.json</c> and <c>&lt;stem&gt;.value.json</c> for each type and value, below
/// <c>pkg/&lt;package&gt;/</c> or <c>deps/&lt;package&gt;/@/</c>. Each stem is the escape of its name
/// (<see cref="TreeStems"/>). A path longer than the path budget has its stem cut, and its module file maps the
/// name to the stem in <c>fileNames</c>. A top-level <c>$meta</c> is ignored on read and never written.
/// </para>
/// <para>
/// A refused write or read gives every error it finds and no tree. The codes of the tree: those of
/// <c>V4DocumentTree.join</c>; <c>tree.stem_collision</c>, two documents whose files would have one path on a file
/// system that ignores case; <c>invalid_distribution_shape</c>, a module with annotations, which a 4.0.0 module
/// file cannot hold, or a path that no cut fits in the budget; <c>tree.missing_manifest</c>;
/// <c>tree.ambiguous_profile</c>, a JSON and a YAML manifest; <c>tree.unsupported_profile</c>, a YAML file, which
/// this store does not read; <c>tree.outside_package</c>, a file below <c>pkg/</c> of another package;
/// <c>tree.outside_tree</c>, a module, type or value file outside <c>pkg/</c> and <c>deps/</c>;
/// <c>tree.versioned_dependency</c>, a dependency segment that carries a version;
/// <c>tree.misplaced_file</c>; <c>tree.unknown_file</c>; <c>tree.invalid_stem</c>; <c>tree.path_mismatch</c>, a
/// module file whose path is not its directory's; <c>tree.name_mismatch</c>, a file whose stem does not spell
/// its name; <c>tree.duplicate_document</c>; <c>tree.over_budget</c>, a path longer than the manifest's budget;
/// <c>tree.too_many_files</c>; <c>tree.file_too_large</c>; <c>tree.unreadable_file</c>; <c>invalid_json</c>;
/// <c>tree.unsupported_option</c>; and <c>tree.classic_not_supported</c>, as classic v3 trees come later. A
/// codec error keeps the codec's rule as its code.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module DocumentTreeStore =
    let private error code file message = StoreErrors.error code file message

    /// <summary>
    /// The physical path of each document of a tree that <c>write</c> made, set on the tree it returns.
    /// </summary>
    let locations: Morphir.Files.Property<Map<DocumentAddress, string>> =
        StoreProperties.locations

    let private classicNotSupported =
        error
            "tree.classic_not_supported"
            None
            "This store reads and writes v4 trees; classic v3 trees come with its next version."

    /// <summary>
    /// Writes a tree to files. The tree must join (<c>V4DocumentTree.join</c>); a module with annotations, two
    /// documents whose files would have one path, and a path that no cut fits are refused, before any file is
    /// encoded. The returned tree has the manifest first and then each document in the ordinal order of its logical
    /// path as its declared order, and <see cref="locations"/> gives each document's file. A refused write gives
    /// every error and no file. C# calls this <c>Write</c>.
    /// </summary>
    [<CompiledName("Write")>]
    let write
        (options: WriteOptions)
        (tree: StoredTree)
        : Result<Morphir.Files.VFileTree, StoreError list> =
        match options.Compact, tree with
        | Some _, _ ->
            Error [
                error
                    "tree.unsupported_option"
                    None
                    "Compacted trees are written by a later version of this store."
            ]
        | None, V4Tree v4 -> V4TreeWriter.writeV4 options v4
        | None, ClassicTree _ -> Error [ classicNotSupported ]

    /// <summary>
    /// Reads a tree from files. The files are taken by their location in the tree, whatever order the tree lists
    /// them in. A JSON file is part of the tree, a <c>.yaml</c> or <c>.yml</c> file is refused as this store reads
    /// the JSON profile only, and any other file is ignored. The tree read must join (<c>V4DocumentTree.join</c>).
    /// A v4 manifest's <c>version</c> and <c>created</c> are dropped with a notice each; its <c>layout</c> and
    /// <c>pathBudget</c> describe the tree and are dropped silently. A package metadata file,
    /// <c>package.json</c> in a package directory of <c>pkg/</c> or <c>deps/</c>, is ignored with an
    /// <c>IgnoredFile</c> notice. Only the files of the tree count against the path budget. A refused read gives
    /// every error it finds and no tree. C# calls this <c>Read</c>.
    /// </summary>
    [<CompiledName("Read")>]
    let read
        (options: ReadOptions)
        (files: Morphir.Files.VFileTree)
        : Result<StoredTree * ReadReport, StoreError list> =
        TreeFileSource.discover options files
        |> Result.bind (fun ((manifestLocation, manifestFile), others) ->
            TreeFileSource.contentBytes options.MaxFileBytes manifestLocation manifestFile
            |> Result.bind (fun bytes ->
                let classic =
                    TreeFileSource.decode
                        manifestLocation
                        (fun root -> Ok(TreeFileSource.isClassicManifest root))
                        bytes

                match classic with
                | Ok true -> Error classicNotSupported
                | Ok false ->
                    TreeFileSource.decode manifestLocation V4TreeFiles.readManifest bytes
                | Error e -> Error e
            )
            |> TreeFileSource.one
            |> Result.bind (fun manifest -> V4TreeReader.readV4 options manifest others)
        )
