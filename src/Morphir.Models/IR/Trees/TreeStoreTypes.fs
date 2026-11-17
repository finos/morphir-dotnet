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

/// <summary>How a store compacts the names of the files it writes.</summary>
type CompactMode =
    /// <summary>Names are written through a name-compaction context (<c>ir/name-compaction</c>).</summary>
    | Names

/// <summary>How a store writes a tree.</summary>
type WriteOptions = {
    /// <summary>
    /// The longest physical path, in characters from the tree's root with the extension, that the store writes. A
    /// longer path has its stem cut, and the module file records the cut in <c>fileNames</c> (decision 0012).
    /// </summary>
    PathBudget: int
    /// <summary>The compaction of names, or none for plain files.</summary>
    Compact: CompactMode option
}

/// <summary>The limits of a read.</summary>
type ReadOptions = {
    /// <summary>The most tree files (JSON or YAML) that a read accepts.</summary>
    MaxFiles: int
    /// <summary>The most bytes of one file that a read accepts.</summary>
    MaxFileBytes: int64
}

/// <summary>What a read found besides the tree.</summary>
type ReadReport = {
    /// <summary>
    /// Content that the read dropped: the manifest's <c>version</c> and <c>created</c>, and each package metadata
    /// file that it ignored.
    /// </summary>
    Notices: TreeNotice list
    /// <summary>The documents whose files were compacted. A plain tree has none.</summary>
    CompactedFiles: DocumentAddress list
}

/// <summary>Why a store did not read or write a tree: a stable code, the file it concerns, and a message.</summary>
type StoreError =
    {
        /// <summary>
        /// The stable code: a <c>tree.</c> code for a rule of the tree, such as <c>tree.stem_collision</c>, or the
        /// rule of the JSON codec, such as <c>unknown_member</c>.
        /// </summary>
        Code: string
        /// <summary>The file, as a physical path from the tree's root, or none for an error of the whole tree.</summary>
        File: string option
        /// <summary>What is wrong.</summary>
        Message: string
    }

    /// <summary>The code, the file and the message.</summary>
    override this.ToString() =
        match this.File with
        | Some file -> $"{this.Code}: {file}: {this.Message}"
        | None -> $"{this.Code}: {this.Message}"

/// <summary>A tree that a store reads or writes: a v4 tree or a classic v3 tree.</summary>
type StoredTree =
    /// <summary>A v4 tree.</summary>
    | V4Tree of V4DocumentTree
    /// <summary>A classic v3 tree, with the attributes as <c>ClassicJson</c> reads them.</summary>
    | ClassicTree of ClassicDocumentTree<JsonElement, JsonElement>

/// <summary>Functions for <see cref="WriteOptions"/>.</summary>
[<RequireQualifiedAccess>]
module WriteOptions =
    /// <summary>The path budget of a tree when the caller gives none: 4000 characters (decision 0012).</summary>
    [<Literal>]
    let DefaultPathBudget = 4000

    /// <summary>A path budget of 4000 and no compaction.</summary>
    let defaults: WriteOptions = {
        PathBudget = DefaultPathBudget
        Compact = None
    }

/// <summary>Functions for <see cref="ReadOptions"/>.</summary>
[<RequireQualifiedAccess>]
module ReadOptions =
    /// <summary>At most a million files of at most 256 MiB each.</summary>
    let defaults: ReadOptions = {
        MaxFiles = 1_000_000
        MaxFileBytes =
            256L
            * 1024L
            * 1024L
    }

/// <summary>
/// What the module file of a tree holds, for the module, type and value documents of one IR version: the module's
/// path, its document, the stems that the path budget cut, and the types and values it holds inline.
/// </summary>
type internal TreeModuleFile<'moduleDoc, 'typ, 'value> = {
    /// <summary>The module path that the file states.</summary>
    Path: ModulePath
    /// <summary>The module document: access, doc, and the names of the types and values in document order.</summary>
    Document: 'moduleDoc
    /// <summary>Each name whose stem was cut for the path budget, with its stem, in document order.</summary>
    FileNames: (Name * string) list
    /// <summary>The types that the file holds inline rather than in files of their own, in document order.</summary>
    InlineTypes: (Name * 'typ) list
    /// <summary>The values that the file holds inline, in document order.</summary>
    InlineValues: (Name * 'value) list
}

/// The helper of the store's modules: a store error.
module internal StoreErrors =
    /// <summary>A store error.</summary>
    let error code file message : StoreError = {
        Code = code
        File = file
        Message = message
    }
