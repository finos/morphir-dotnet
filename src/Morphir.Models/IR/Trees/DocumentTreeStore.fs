namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Text.Json
open System.Threading
open Morphir.IR
open Morphir.IR.Model


/// <summary>
/// Maps a document tree to and from the files of the parent's document-tree specifications, on a
/// <see cref="VFileTree"/>. The store makes no file-system call: the same tree comes from or goes to the disk, an
/// embedded resource, an archive or memory.
/// </summary>
/// <remarks>
/// <para>
/// A tree is written in the JSON profile: <c>manifest.json</c>, <c>module.json</c> for each module, and
/// <c>&lt;stem&gt;.type.json</c> and <c>&lt;stem&gt;.value.json</c> for each type and value, below
/// <c>pkg/&lt;package&gt;/</c> or <c>deps/&lt;package&gt;/@/</c>. Each stem is the escape of its name
/// (<see cref="TreeStems"/>). A path longer than the path budget has its stem cut, and its module file maps the
/// name to the stem in <c>fileNames</c>. A top-level <c>$meta</c> is ignored on read and never written. A v4 tree's
/// files say <c>"formatVersion": 4</c>; a classic v3 tree has the same layout, and every file says
/// <c>"formatVersion": "3.1.0"</c> around the classic v3 JSON of each entry.
/// </para>
/// <para>
/// A refused write or read gives every error it finds and no tree. Where the parent's v3 or v4 tree page names a
/// diagnostic for a rule, both versions use it, with the file in <c>File</c> and the member's JSON location at the
/// start of the message: <c>version_mismatch</c>, a file of a v3 tree, the manifest included, that does not say
/// <c>"3.1.0"</c>; <c>missing_format_version</c>; <c>unknown_member</c>; <c>missing_member</c>, a required member,
/// or a name that a module file lists with no file, at the missing file's path; <c>invalid_type</c>, a member of
/// the wrong type, a path budget under 64, or a payload that is not the JSON of its kind;
/// <c>invalid_distribution_shape</c>, a dependency that is the own package, a node file without exactly the one of
/// <c>def</c> and <c>spec</c> that its role calls for, a v3 wrapper with another member or a <c>spec</c> with an
/// <c>access</c>, a v4 module with annotations, which a 4.0.0 module file cannot hold, or a path that no cut fits in
/// the budget; <c>duplicate_member</c>, a dependency listed twice; and <c>invalid_access</c>.
/// </para>
/// <para>
/// The <c>tree.</c> codes are for the rules that the parent names no diagnostic for: those of
/// <c>V4DocumentTree.join</c> and <c>ClassicDocumentTree.join</c>; <c>tree.stem_collision</c>, two documents whose
/// files would have one path on a file system that ignores case; <c>tree.missing_manifest</c>;
/// <c>tree.ambiguous_profile</c>, a JSON and a YAML manifest, which it names; <c>tree.unsupported_profile</c>, a YAML
/// file, which this store does not read; <c>tree.outside_package</c>, a file below <c>pkg/</c> of another package;
/// <c>tree.outside_tree</c>, a module, type or value file outside <c>pkg/</c> and <c>deps/</c>;
/// <c>tree.versioned_dependency</c>, a dependency segment that carries a version, named once by its directory;
/// <c>tree.misplaced_file</c>; <c>tree.unknown_file</c>; <c>tree.invalid_stem</c>; <c>tree.path_mismatch</c>, a
/// module file whose path is not its directory's; <c>tree.name_mismatch</c>, a file whose stem does not spell its
/// name; <c>tree.duplicate_document</c>; <c>tree.over_budget</c>, a path longer than the manifest's budget;
/// <c>tree.too_many_files</c>; <c>tree.file_too_large</c>; <c>tree.unreadable_file</c>; and
/// <c>tree.unsupported_option</c>, a path budget under the minimum. <c>invalid_json</c> is a file that is not JSON, and <c>invalid_file_name</c> a
/// <c>fileNames</c> entry that is not the cut stem of its name. Any other codec error keeps the codec's rule.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module DocumentTreeStore =
    let private error code file message = StoreErrors.error code file message

    /// <summary>
    /// The physical path of each document of a tree that <c>write</c> made, set on the tree it returns.
    /// </summary>
    let locations: Morphir.Files.Property<Map<DocumentAddress, string>> =
        TreeWriter.locations

    /// <summary>
    /// The files of a compacted write that the writer policy wrote as plain documents in its written form, because no
    /// alias was worth its cost there, in the declared order; set on the tree that a write with
    /// <c>Compact = Some Names</c> returns, and not on a plain write.
    /// </summary>
    let plainFiles: Morphir.Files.Property<string list> = TreeWriter.plainFiles

    /// <summary>
    /// The files that a compacted write left plain (<see cref="plainFiles"/>), or none for a tree that a plain write
    /// gave. C# calls this <c>PlainFilesOf</c>.
    /// </summary>
    [<CompiledName("PlainFilesOf")>]
    let plainFilesOf
        (files: Morphir.Files.VFileTree)
        : System.Collections.Generic.IReadOnlyList<string> =
        match Morphir.Files.VFileTree.tryGet plainFiles files with
        | Some paths -> Array.ofList paths :> System.Collections.Generic.IReadOnlyList<string>
        | None -> Array.empty<string> :> System.Collections.Generic.IReadOnlyList<string>

    /// <summary>
    /// Writes a tree to files: a v4 tree in the v4 files, a classic tree in the v3 files. The tree must join
    /// (<c>V4DocumentTree.join</c>, <c>ClassicDocumentTree.join</c>); a v4 module with annotations, two documents
    /// whose files would have one path, and a path that no cut fits are refused, before any file is encoded. The
    /// returned tree has the manifest first and then each document in the ordinal order of its logical path as its
    /// declared order, and <see cref="locations"/> gives each document's file. With <c>Compact = Some Names</c>,
    /// each file, the manifest included, is written under the writer policy <c>morphir.compact/0.1.0-draft.1</c>
    /// (<c>CompactionPolicy</c>) with its own context, in canonical JSON with no newline after it: an envelope when an
    /// alias is worth its cost, or else the plain document, which <see cref="plainFiles"/> lists. A file that the
    /// envelope would nest past what a read of a tree accepts is refused with <c>depth_limit_exceeded</c>. A refused
    /// write gives every error and no file. C# calls this <c>Write</c>.
    /// </summary>
    [<CompiledName("Write")>]
    let write
        (options: WriteOptions)
        (tree: StoredTree)
        : Result<Morphir.Files.VFileTree, StoreError list> =
        match tree with
        | V4Tree v4 -> V4TreeWriter.writeV4 options v4
        | ClassicTree classic -> ClassicTreeWriter.writeClassic options classic

    /// <summary>
    /// Reads a tree from files. The manifest's <c>formatVersion</c> chooses the version: a manifest of major 3 is
    /// read as a classic v3 tree, which says <c>"3.1.0"</c> in every file, and any other as a v4 tree. The files are
    /// taken by their location in the tree, whatever order the tree lists them in. A JSON file is part of the tree, a
    /// <c>.yaml</c> or <c>.yml</c> file is refused as the whole-tree read accepts only the JSON profile, and any other file is
    /// ignored. The tree read must join (<c>V4DocumentTree.join</c>, <c>ClassicDocumentTree.join</c>). A v4
    /// manifest's <c>version</c> and <c>created</c> are dropped with a notice each; its <c>layout</c> and
    /// <c>pathBudget</c> describe the tree and are dropped silently. A package metadata file, <c>package.json</c> in a
    /// package directory of <c>pkg/</c> or <c>deps/</c>, is ignored with an <c>IgnoredFile</c> notice. Only the files
    /// of the tree count against the path budget. A compacted file, an envelope with a name context
    /// (<c>ir/name-compaction</c>), is read through <c>MorphirFile</c>: its context is checked and its document
    /// expanded for the IR family of its own <c>formatVersion</c> before it is decoded, and the report lists its
    /// documents in <c>CompactedFiles</c> (the manifest first, then by file); a refused context is an error of the
    /// file, with the parent's <c>context.</c> code. A refused read gives every error it finds and no tree. C# calls
    /// this <c>Read</c>.
    /// </summary>
    [<CompiledName("Read")>]
    let read
        (options: ReadOptions)
        (files: Morphir.Files.VFileTree)
        : Result<StoredTree * ReadReport, StoreError list> =
        TreeFileSource.discover options files
        |> Result.bind (fun ((manifestLocation, manifestFile), others) ->
            TreeFileSource.contentBytes options.MaxFileBytes manifestLocation manifestFile
            |> TreeFileSource.one
            |> Result.bind (fun bytes ->
                TreeFileSource.decode
                    manifestLocation
                    (fun root -> Ok(TreeFileSource.isClassicManifest root))
                    bytes
                |> Result.map (fun (classic, compacted) -> classic, compacted, bytes)
            )
            |> Result.bind (fun (classic, compacted, bytes) ->
                if classic then
                    TreeFileSource.decode manifestLocation ClassicTreeFiles.readManifest bytes
                    |> Result.bind (fun (manifest, _) ->
                        ClassicTreeReader.readClassic compacted options manifest others
                    )
                else
                    TreeFileSource.decode manifestLocation V4TreeFiles.readManifest bytes
                    |> Result.bind (fun (manifest, _) ->
                        V4TreeReader.readV4 compacted options manifest others
                    )
            )
        )

    let private readModelCore
        (options: TreeReadOptions)
        (enforceNodes: bool)
        (ct: CancellationToken)
        (files: Morphir.Files.VFileTree)
        : Result<LoadedTreeModel, TreeDiagnostic list> =
        ct.ThrowIfCancellationRequested()

        TreeFileSource.discoverWithOptions true options.MaxFiles options.Profiles files
        |> Result.mapError (List.map StoreErrors.ofStoreError)
        |> Result.bind (fun (profile, (manifestLocation, manifestFile), others) ->
            let maxNodes = if enforceNodes then Some options.MaxNodes else None
            let maxTotalBytes = if options.MaxTotalBytes < Int64.MaxValue then Some options.MaxTotalBytes else None

            let ctx =
                TreeFileSource.createDecodeContext
                    profile
                    options.MaxFileBytes
                    maxTotalBytes
                    options.MaxDepth
                    maxNodes
                    ct

            TreeFileSource.contentBytes (TreeFileSource.readFileCap ctx) manifestLocation manifestFile
            |> Result.mapError (StoreErrors.ofStoreError >> List.singleton)
            |> Result.bind (TreeFileSource.decodeRawDetailed ctx manifestLocation)
            |> Result.bind (fun (manifestRoot, compacted) ->
                let declaredText = TreeFileSource.declaredFormatVersion manifestRoot
                let classic = TreeFileSource.isClassicManifest manifestRoot

                let decodeManifestResult reader =
                    reader manifestRoot
                    |> Result.mapError (fun (e: Morphir.Json.V4.V4JsonError) -> [
                        StoreErrors.located e.Rule (Some manifestLocation) e.Location e.Message
                    ])

                let buildModel (distribution: DistributionModel) (storedTree: StoredTree) (report: ReadReport) (compactedLocations: string list) =
                    match SourceFormatVersion.parse declaredText with
                    | Ok sourceVersion ->
                        let versioned: VersionedModel = {
                            SourceVersion = sourceVersion
                            Distribution = distribution
                        }

                        Ok(LoadedTreeModel(versioned, profile, storedTree, report, compactedLocations))
                    | Error _ ->
                        Error [
                            StoreErrors.located
                                "version_mismatch"
                                (Some manifestLocation)
                                "$.formatVersion"
                                $"'{declaredText}' is not a supported formatVersion."
                        ]

                if classic then
                    decodeManifestResult ClassicTreeFiles.readManifest
                    |> Result.bind (fun manifest ->
                        ClassicTreeReader.readClassicWith ctx manifestLocation compacted manifest others
                        |> Result.bind (fun (storedTree, joined, report, compactedLocations) ->
                            buildModel (DistributionModel.Classic joined) storedTree report compactedLocations
                        )
                    )
                else
                    decodeManifestResult V4TreeFiles.readManifest
                    |> Result.bind (fun manifest ->
                        V4TreeReader.readV4With ctx manifestLocation compacted manifest others
                        |> Result.bind (fun (storedTree, joined, report, compactedLocations) ->
                            buildModel (DistributionModel.V4 joined) storedTree report compactedLocations
                        )
                    )
            )
        )

    /// <summary>
    /// For F# callers only: reads a tree from files in any profile allowed by <paramref name="options"/> (JSON or
    /// YAML, plain or contextual, v3.1.0 or v4.0.0), enforcing file, byte, nesting-depth, and total node limits.
    /// Refuses mixed profile files and ambiguous multi-manifest trees (including <c>manifest.ion</c>). C# callers
    /// use <see cref="TryReadModel"/>, which returns <see cref="IReadOnlyList{TreeDiagnostic}"/>.
    /// </summary>
    let readAnyProfile
        (options: TreeReadOptions)
        (files: Morphir.Files.VFileTree)
        : Result<StoredTree * ReadReport, StoreError list> =
        readModelCore options true CancellationToken.None files
        |> Result.map (fun loaded -> loaded.Tree, loaded.Report)
        |> Result.mapError StoreErrors.toStoreErrors

    /// <summary>
    /// For C# callers: reads a whole document tree (JSON or YAML, plain or contextual, v3.1.0 or v4.0.0) under
    /// <paramref name="options"/> and <paramref name="ct"/>, joins it into a <see cref="VersionedModel"/> keeping
    /// the exact declared manifest <c>formatVersion</c>, and returns it with its <see cref="TreeProfile"/>.
    /// Sets exactly one of <paramref name="loaded"/> and <paramref name="errors"/>. C# calls this
    /// <c>TryReadModel</c>.
    /// </summary>
    [<CompiledName("TryReadModel")>]
    let tryReadModel
        (
            options: TreeReadOptions,
            files: Morphir.Files.VFileTree,
            ct: CancellationToken,
            [<Out; MaybeNullWhen(false)>] loaded: byref<LoadedTreeModel>,
            [<Out; MaybeNullWhen(true)>] errors: byref<IReadOnlyList<TreeDiagnostic>>
        ) : bool =
        match readModelCore options true ct files with
        | Ok model ->
            loaded <- model
            errors <- Unchecked.defaultof<_>
            true
        | Error found ->
            loaded <- Unchecked.defaultof<_>
            errors <- List.toArray found :> IReadOnlyList<TreeDiagnostic>
            false

    /// <summary>
    /// For C# callers: reads a single Morphir YAML document (plain or contextual envelope) under explicit byte,
    /// depth, node, and cancellation limits, returning its expanded <see cref="JsonElement"/> value tree. Sets
    /// exactly one of <paramref name="document"/> and <paramref name="errors"/>. C# calls this
    /// <c>TryReadYamlDocument</c>.
    /// </summary>
    [<CompiledName("TryReadYamlDocument")>]
    let tryReadYamlDocument
        (
            file: string,
            bytes: ReadOnlyMemory<byte>,
            maxBytes: int64,
            maxDepth: int,
            maxNodes: int,
            ct: CancellationToken,
            [<Out>] document: byref<JsonElement>,
            [<Out; MaybeNullWhen(true)>] errors: byref<IReadOnlyList<TreeDiagnostic>>
        ) : bool =
        match TreeFileSource.readSingleYamlDocument file bytes maxBytes maxDepth maxNodes ct with
        | Ok root ->
            document <- root
            errors <- Unchecked.defaultof<_>
            true
        | Error found ->
            document <- Unchecked.defaultof<_>
            errors <- List.toArray found :> IReadOnlyList<TreeDiagnostic>
            false

    /// <summary>
    /// For C# callers: inspects only the root YAML framing and root mapping events of <paramref name="bytes"/>
    /// in O(1) parser steps to determine whether an extensionless file looks like a YAML document rather than
    /// JSON, without decoding the body or masking downstream YAML errors. Standard JSON object starts
    /// (<c>{"...</c>, <c>{}</c>), JSON arrays (<c>[</c>), and binary Ion return <c>false</c>. C# calls this
    /// <c>LooksLikeYamlDocument</c>.
    /// </summary>
    [<CompiledName("LooksLikeYamlDocument")>]
    let looksLikeYamlDocument (bytes: ReadOnlyMemory<byte>, cancellationToken: CancellationToken) : bool =
        TreeYaml.looksLikeYamlDocument bytes cancellationToken

    /// <summary>
    /// For C# callers that check a tree and need no model: each error of <see cref="read"/>, in the order the read
    /// found them, or none when the tree reads. C# calls this <c>ReadErrors</c>.
    /// </summary>
    [<CompiledName("ReadErrors")>]
    let readErrors
        (
            options: ReadOptions,
            files: Morphir.Files.VFileTree
        ) : System.Collections.Generic.IReadOnlyList<StoreError> =
        match read options files with
        | Ok _ -> Array.empty<StoreError> :> System.Collections.Generic.IReadOnlyList<StoreError>
        | Error errors ->
            List.toArray errors :> System.Collections.Generic.IReadOnlyList<StoreError>

    /// Open a revision-bound tree with an explicit file-local name encoding.
    /// MaxFiles bounds directory entries inspected for each next-module lookup.
    /// The revision must identify an immutable view or a held read lease.
    [<CompiledName("OpenSnapshotWithEncoding")>]
    let openSnapshotWithEncoding
        (options: ReadOptions)
        (encoding: BoundedTreeEncoding)
        (revision: unit -> string)
        (provider: Microsoft.Extensions.FileProviders.IFileProvider)
        : Result<BoundedTreeSnapshot, StoreError list> =
        if options.MaxFiles < 1 then
            Error [ error "tree.invalid_limit" None "A module lookup limit must be positive." ]
        else
            TreeFileSource.fromFileProvider options.MaxFiles revision provider
            |> BoundedTreeReader.openTreeWithEncoding encoding options.MaxFileBytes

    /// Open a revision-bound plain tree without reading its modules or definitions.
    [<CompiledName("OpenSnapshot")>]
    let openSnapshot options revision provider =
        openSnapshotWithEncoding options BoundedTreeEncoding.Plain revision provider

    /// Open a revision-bound plain tree from a host callback.
    [<CompiledName("OpenPlainSnapshot")>]
    let openPlainSnapshot
        (
            options: ReadOptions,
            revision: System.Func<string>,
            provider: Microsoft.Extensions.FileProviders.IFileProvider
        ) : Result<BoundedTreeSnapshot, System.Collections.Generic.IReadOnlyList<StoreError>> =
        openSnapshot options (fun () -> revision.Invoke()) provider
        |> Result.mapError (fun errors -> List.toArray errors :> System.Collections.Generic.IReadOnlyList<StoreError>)

    /// Open a plain snapshot with a retained-path reservation for one active package.
    /// The caller must subtract this reservation from its other host-cache allowances.
    /// Zero preserves the scan fallback; a positive exhausted reservation refuses discovery.
    [<CompiledName("OpenSnapshotWithDiscoveryCache")>]
    let openSnapshotWithDiscoveryCache
        (options: ReadOptions)
        (cacheBytes: int64)
        (revision: System.Func<string>)
        (provider: Microsoft.Extensions.FileProviders.IFileProvider) =
        if options.MaxFiles < 1 then
            Error [ error "tree.invalid_limit" None "A module lookup limit must be positive." ]
        elif cacheBytes < 0L then
            Error [ error "tree.invalid_limit" None "A discovery reservation cannot be negative." ]
        else
            TreeFileSource.fromFileProviderWithDiscoveryCache options.MaxFiles cacheBytes (fun () -> revision.Invoke()) provider
            |> BoundedTreeReader.openTreeWithEncoding BoundedTreeEncoding.Plain options.MaxFileBytes

    /// Open a cached plain snapshot from a host callback with C#-safe admission errors.
    /// The host must reserve cacheBytes alongside its other retained host-cache data.
    [<CompiledName("OpenPlainSnapshotWithDiscoveryCache")>]
    let openPlainSnapshotWithDiscoveryCache
        (
            options: ReadOptions,
            cacheBytes: int64,
            revision: System.Func<string>,
            provider: Microsoft.Extensions.FileProviders.IFileProvider
        ) : Result<BoundedTreeSnapshot, System.Collections.Generic.IReadOnlyList<StoreError>> =
        openSnapshotWithDiscoveryCache options cacheBytes revision provider
        |> Result.mapError (fun errors -> List.toArray errors :> System.Collections.Generic.IReadOnlyList<StoreError>)

    /// Read the next page of modules in the selected package.
    [<CompiledName("PageModules")>]
    let pageModules limit cursor package snapshot =
        BoundedTreeReader.modules limit cursor package snapshot

    /// Read the next page of members listed by the selected module.
    [<CompiledName("PageMembers")>]
    let pageMembers limit cursor address snapshot =
        BoundedTreeReader.members limit cursor address snapshot

    /// Decode one selected type or value with its version-specific attributes.
    [<CompiledName("ReadMember")>]
    let readMember address snapshot =
        BoundedTreeReader.readMember address snapshot

    /// Read one selected member and retain its source file bytes for opaque pass-through.
    /// For an inline member the bytes are the original containing module.
    [<CompiledName("ReadMemberWithArtifact")>]
    let readMemberWithArtifact address snapshot =
        BoundedTreeReader.readMemberWithArtifact address snapshot

    /// Page the root modules or the members of a logical module in the same snapshot.
    [<CompiledName("PageInventory")>]
    let pageInventory
        (selection: string)
        (limit: int)
        (continuation: string option)
        (snapshot: BoundedTreeSnapshot)
        : Result<BoundedTreeInventoryPage, StoreError list> =
        BoundedTreeReader.pageInventory selection limit continuation snapshot

    /// Read the original bytes of a logical document in the admitted snapshot.
    [<CompiledName("ReadDocument")>]
    let readDocument
        (documentId: string)
        (snapshot: BoundedTreeSnapshot)
        : Result<System.ReadOnlyMemory<byte>, StoreError list> =
        BoundedTreeReader.readDocument documentId snapshot

    /// Read a logical document within an additional limit on its original bytes.
    [<CompiledName("ReadDocumentWithin")>]
    let readDocumentWithin documentId maxBytes snapshot =
        BoundedTreeReader.readDocumentWithin documentId maxBytes snapshot

    /// Read within the delivery budgets and report the greater of source and expanded node counts.
    [<CompiledName("ReadDocumentWithinCounted")>]
    let readDocumentWithinCounted documentId maxBytes maxNodes snapshot =
        BoundedTreeReader.readDocumentWithinCounted documentId maxBytes maxNodes snapshot

    /// Read one admitted document as plain JSON under an independent output byte limit.
    [<CompiledName("ReadExpandedDocument")>]
    let readExpandedDocument documentId maxOutputBytes snapshot =
        BoundedTreeReader.readExpandedDocument documentId maxOutputBytes snapshot

    /// Stage one expanded JSON file at a time. Publish the stage only after the walk succeeds.
    [<CompiledName("WriteExpandedTree")>]
    let writeExpandedTree limits stageFile snapshot =
        BoundedTreeReader.writeExpandedTree limits stageFile snapshot

    /// Resolve one typed alias without expanding the member around it.
    /// Path tokens within the selected payload are object keys or canonical array indices.
    /// The result uses the name spelling of the selected IR version.
    [<CompiledName("ResolveMemberAlias")>]
    let resolveMemberAlias address path snapshot =
        BoundedTreeReader.resolveMemberAlias address path snapshot
