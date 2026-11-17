namespace Morphir.IR.Trees

/// <summary>A document of a tree other than the manifest.</summary>
type TreeDocument<'moduleDoc, 'typ, 'value> =
    /// <summary>A module document, at a <c>Module</c> address.</summary>
    | ModuleDoc of 'moduleDoc
    /// <summary>A type document, at a <c>TypeDef</c> address.</summary>
    | TypeDoc of 'typ
    /// <summary>A value document, at a <c>ValueDef</c> address.</summary>
    | ValueDoc of 'value

/// <summary>
/// A model as a document tree: the manifest, and one decoded document per module, type and value, each at its
/// address. The tree holds no file path or bytes; a store maps it to and from files.
/// </summary>
type DocumentTree<'manifest, 'moduleDoc, 'typ, 'value> = {
    /// <summary>The manifest: the distribution's kind, package and dependencies.</summary>
    Manifest: 'manifest
    /// <summary>
    /// The documents by address. A <c>Manifest</c> key is not a document
    /// of a tree, which keeps its manifest in <c>Manifest</c>.
    /// </summary>
    Documents: Map<DocumentAddress, TreeDocument<'moduleDoc, 'typ, 'value>>
}

/// <summary>Content that a join or a read dropped, which a caller reports.</summary>
type TreeNotice =
    /// <summary>A manifest member with no place in the single-file model, such as <c>version</c> or <c>created</c>.</summary>
    | DroppedManifestMember of name: string
    /// <summary>
    /// A file that a read ignored, by its physical path: a package metadata file (<c>package.json</c>), which the
    /// parent's v4 tree page describes and which is not part of the model.
    /// </summary>
    | IgnoredFile of path: string

    /// <summary>The notice as text.</summary>
    override this.ToString() =
        match this with
        | DroppedManifestMember name ->
            $"The manifest member '{name}' has no place in the model and was dropped."
        | IgnoredFile path -> $"The file '{path}' is not part of the model and was ignored."

/// <summary>Why a tree does not join: a stable dotted code, the document it concerns if any, and a message.</summary>
type TreeError =
    {
        /// <summary>The stable code, such as <c>tree.missing_definition</c>.</summary>
        Code: string
        /// <summary>The document the error concerns, or none for an error of the whole tree.</summary>
        Address: DocumentAddress option
        /// <summary>What is wrong, naming the module, type or value.</summary>
        Message: string
    }

    /// <summary>The code and the message.</summary>
    override this.ToString() = $"{this.Code}: {this.Message}"
