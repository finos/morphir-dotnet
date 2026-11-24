namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open Morphir.IR


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

    /// <summary>
    /// Open a revision-bound tree from a host callback, detecting whether any admitted file in the tree carries
    /// the declared contextual draft.1 envelope (<c>ir-context/0.1.0-draft.1</c>) or whether all files are plain,
    /// while refusing undeclared <c>$schema</c> values across own and dependency files under explicit node, work,
    /// and cancellation bounds. For C# callers.
    /// </summary>
    [<CompiledName("OpenDetectedSnapshotBounded")>]
    let openDetectedSnapshotBounded
        (
            options: ReadOptions,
            maxNodes: int,
            maxWorkUnits: int64,
            revision: System.Func<string>,
            provider: Microsoft.Extensions.FileProviders.IFileProvider,
            cancellationToken: System.Threading.CancellationToken
        ) : Result<BoundedTreeSnapshot, System.Collections.Generic.IReadOnlyList<StoreError>> =
        let opened =
            if options.MaxFiles < 1 then
                Error [ error "tree.invalid_limit" None "A module lookup limit must be positive." ]
            elif maxNodes < 0 then
                Error [ error "tree.invalid_limit" None "A node limit cannot be negative." ]
            elif maxWorkUnits < 0L then
                Error [ error "tree.invalid_limit" None "A work budget cannot be negative." ]
            else
                let nodeLimit = if maxNodes > 0 then Some maxNodes else None
                let workBudget = if maxWorkUnits > 0L then Some(BoundedTreeWorkBudget maxWorkUnits) else None

                TreeFileSource.fromFileProvider options.MaxFiles (fun () -> revision.Invoke()) provider
                |> BoundedTreeReader.openTreeDetectedBounded options.MaxFileBytes nodeLimit workBudget cancellationToken

        opened
        |> Result.mapError (fun errors -> List.toArray errors :> System.Collections.Generic.IReadOnlyList<StoreError>)

    /// <summary>
    /// Open a revision-bound tree from a host callback, detecting whether any admitted file carries the declared
    /// contextual draft.1 envelope (<c>ir-context/0.1.0-draft.1</c>) or whether all files are plain. For C# callers.
    /// </summary>
    [<CompiledName("OpenDetectedSnapshot")>]
    let openDetectedSnapshot
        (
            options: ReadOptions,
            revision: System.Func<string>,
            provider: Microsoft.Extensions.FileProviders.IFileProvider
        ) : Result<BoundedTreeSnapshot, System.Collections.Generic.IReadOnlyList<StoreError>> =
        openDetectedSnapshotBounded (options, 0, 0L, revision, provider, System.Threading.CancellationToken.None)

    /// <summary>
    /// Open a cached snapshot with an explicit file-local name encoding from a host callback with C#-safe
    /// admission errors. The host must reserve <paramref name="cacheBytes"/> alongside its other retained
    /// host-cache data. For C# callers.
    /// </summary>
    [<CompiledName("OpenSnapshotWithDiscoveryCacheAndEncoding")>]
    let openSnapshotWithDiscoveryCacheAndEncoding
        (
            options: ReadOptions,
            encoding: BoundedTreeEncoding,
            cacheBytes: int64,
            revision: System.Func<string>,
            provider: Microsoft.Extensions.FileProviders.IFileProvider
        ) : Result<BoundedTreeSnapshot, System.Collections.Generic.IReadOnlyList<StoreError>> =
        let opened =
            if options.MaxFiles < 1 then
                Error [ error "tree.invalid_limit" None "A module lookup limit must be positive." ]
            elif cacheBytes < 0L then
                Error [ error "tree.invalid_limit" None "A discovery reservation cannot be negative." ]
            else
                TreeFileSource.fromFileProviderWithDiscoveryCache options.MaxFiles cacheBytes (fun () -> revision.Invoke()) provider
                |> BoundedTreeReader.openTreeWithEncoding encoding options.MaxFileBytes

        opened
        |> Result.mapError (fun errors -> List.toArray errors :> System.Collections.Generic.IReadOnlyList<StoreError>)

    /// <summary>
    /// Open a cached snapshot from a host callback, detecting whether the manifest carries the declared
    /// contextual draft.1 envelope or is plain. For C# callers.
    /// </summary>
    [<CompiledName("OpenDetectedSnapshotWithDiscoveryCache")>]
    let openDetectedSnapshotWithDiscoveryCache
        (
            options: ReadOptions,
            cacheBytes: int64,
            revision: System.Func<string>,
            provider: Microsoft.Extensions.FileProviders.IFileProvider
        ) : Result<BoundedTreeSnapshot, System.Collections.Generic.IReadOnlyList<StoreError>> =
        let opened =
            if options.MaxFiles < 1 then
                Error [ error "tree.invalid_limit" None "A module lookup limit must be positive." ]
            elif cacheBytes < 0L then
                Error [ error "tree.invalid_limit" None "A discovery reservation cannot be negative." ]
            else
                TreeFileSource.fromFileProviderWithDiscoveryCache options.MaxFiles cacheBytes (fun () -> revision.Invoke()) provider
                |> BoundedTreeReader.openTreeDetected options.MaxFileBytes

        opened
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

    /// <summary>
    /// Exports an admitted snapshot as expanded JSON documents within <paramref name="maxDocumentBytes"/>,
    /// <paramref name="maxTotalBytes"/> and <paramref name="maxFiles"/>, verifies that no tree metadata
    /// (<c>$meta</c>, manifest <c>version</c>/<c>created</c>, or unconsumed/ignored files from
    /// <paramref name="originalFiles"/>) would be silently dropped, then joins it losslessly into a typed
    /// <see cref="Morphir.IR.Model.VersionedModel"/> alongside the staged expanded file bytes. For C# callers.
    /// </summary>
    [<CompiledName("TryJoinSnapshot")>]
    let tryJoinSnapshot
        (
            snapshot: BoundedTreeSnapshot,
            originalFiles: System.Collections.Generic.IReadOnlyList<string>,
            maxFiles: int,
            maxDocumentBytes: int64,
            maxTotalBytes: int64,
            cancellationToken: System.Threading.CancellationToken,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)>] model: byref<Morphir.IR.Model.VersionedModel>,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)>] files: byref<System.Collections.Generic.IReadOnlyList<System.Collections.Generic.KeyValuePair<string, byte array>>>,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(true)>] problems: byref<System.Collections.Generic.IReadOnlyList<StoreError>>
        ) : bool =
        cancellationToken.ThrowIfCancellationRequested()
        let staged = ResizeArray<string * Morphir.Files.VFile>()
        let retained = ResizeArray<System.Collections.Generic.KeyValuePair<string, byte array>>()
        let consumedSources = System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)

        let normalizePath (path: string) = path.Replace('\\', '/').Trim('/')

        let stageFile (sourceLocation: string) (doc: BoundedTreeExportDocument) =
            cancellationToken.ThrowIfCancellationRequested()

            if staged.Count >= maxFiles then
                Error [ error "tree.too_many_files" (Some doc.File) $"The tree exceeds its limit of {maxFiles} files." ]
            else
                let bytes = doc.Bytes.ToArray()
                let jsonOptions = System.Text.Json.JsonDocumentOptions(MaxDepth = Morphir.Json.V4.V4JsonDecoding.MaxDepth)
                use parsed = System.Text.Json.JsonDocument.Parse(bytes, jsonOptions)
                let mutable meta = Unchecked.defaultof<System.Text.Json.JsonElement>

                if
                    parsed.RootElement.ValueKind = System.Text.Json.JsonValueKind.Object
                    && parsed.RootElement.TryGetProperty("$meta", &meta)
                then
                    Error [
                        error
                            "data.lossy_conversion"
                            (Some sourceLocation)
                            $"Converting document tree to single-document JSON would drop '$meta' in {sourceLocation}; lossy conversion is refused by default."
                    ]
                else
                    consumedSources.Add(normalizePath sourceLocation) |> ignore
                    staged.Add(doc.File, Morphir.Files.VFile.ofBytes doc.File (System.ReadOnlyMemory bytes))
                    retained.Add(System.Collections.Generic.KeyValuePair(doc.File, bytes))
                    Ok()

        let exportLimits: BoundedTreeExportLimits = {
            MaxDocumentBytes = maxDocumentBytes
            MaxTotalBytes = maxTotalBytes
            PageSize = 64
        }

        let joined =
            BoundedTreeReader.writeExpandedTreeWithSources exportLimits stageFile snapshot
            |> Result.bind (fun () ->
                cancellationToken.ThrowIfCancellationRequested()

                let unconsumed =
                    originalFiles
                    |> Seq.map normalizePath
                    |> Seq.filter (fun path -> not (System.String.IsNullOrEmpty path) && not (consumedSources.Contains path))
                    |> Seq.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
                    |> Seq.tryHead

                match unconsumed with
                | Some droppedFile ->
                    Error [
                        error
                            "data.lossy_conversion"
                            (Some droppedFile)
                            $"Converting document tree to single-document JSON would drop unconsumed file '{droppedFile}'; lossy conversion is refused by default."
                    ]
                | None ->
                    let tree = Morphir.Files.VFileTree.fromFileMap (Map.ofSeq staged)
                    let readOptions: ReadOptions = { MaxFiles = maxFiles; MaxFileBytes = maxDocumentBytes }
                    read readOptions tree
            )
            |> Result.bind (fun (stored, report) ->
                cancellationToken.ThrowIfCancellationRequested()

                let lossyNotice =
                    report.Notices
                    |> List.tryPick (function
                        | DroppedManifestMember memberName ->
                            Some(
                                error
                                    "data.lossy_conversion"
                                    (Some "manifest.json")
                                    $"Converting document tree to single-document JSON would drop manifest member '{memberName}'; lossy conversion is refused by default."
                            )
                        | IgnoredFile file ->
                            Some(
                                error
                                    "data.lossy_conversion"
                                    (Some file)
                                    $"Converting document tree to single-document JSON would drop '{file}'; lossy conversion is refused by default."
                            ))

                match lossyNotice with
                | Some loss -> Error [ loss ]
                | None ->
                    let version = Morphir.IR.Model.SourceFormatVersion.Create snapshot.IrVersion

                    match stored with
                    | V4Tree v4 ->
                        V4DocumentTree.join v4
                        |> Result.map (fun dist ->
                            ({ SourceVersion = version; Distribution = Morphir.IR.Model.DistributionModel.V4 dist }: Morphir.IR.Model.VersionedModel))
                        |> Result.mapError (List.map (fun err -> error err.Code None err.Message))
                    | ClassicTree classic ->
                        ClassicDocumentTree.join classic
                        |> Result.map (fun dist ->
                            ({ SourceVersion = version; Distribution = Morphir.IR.Model.DistributionModel.Classic dist }: Morphir.IR.Model.VersionedModel))
                        |> Result.mapError (List.map (fun err -> error err.Code None err.Message))
            )

        match joined with
        | Ok joinedModel ->
            model <- joinedModel
            files <- retained.ToArray() :> System.Collections.Generic.IReadOnlyList<_>
            problems <- Unchecked.defaultof<_>
            true
        | Error errors ->
            model <- Unchecked.defaultof<_>
            files <- Unchecked.defaultof<_>
            problems <- List.toArray errors :> System.Collections.Generic.IReadOnlyList<_>
            false
