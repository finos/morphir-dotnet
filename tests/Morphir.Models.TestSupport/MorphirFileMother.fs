/// <summary>
/// Morphir files, plain and compacted, from the parent's name-context examples that <c>FixtureCatalog.Context</c>
/// pins. The examples share one small model, package <c>my-org/billing</c> with module <c>invoicing</c>, the type
/// <c>invoice-line</c> and the value <c>line-total</c>, so their tree-file examples are also the files of one tree.
/// A test takes its files from here and never writes an envelope by hand.
/// </summary>
module Morphir.Models.TestSupport.MorphirFileMother

open Morphir.TestSupport.Fixtures

/// <summary>The parent's example with this id, such as <c>v4-type</c>.</summary>
let example (id: string) : ContextExample = FixtureCatalog.Context.Example id

/// <summary>The bytes of an example's file.</summary>
let bytesOf (id: string) : byte array = FixtureCatalog.ReadBytes (example id).File

/// <summary>The text of the plain twin of a valid example: the document a reader must produce.</summary>
let expandedTextOf (id: string) : string =
    match (example id).Expanded with
    | null -> failwith $"The context example '{id}' has no expanded twin."
    | twin -> FixtureCatalog.ReadText twin

/// <summary>Every valid example, which expands to its twin.</summary>
let validExamples: ContextExample list =
    FixtureCatalog.Context.Examples
    |> Seq.filter (fun e -> e.IsValid)
    |> List.ofSeq

/// <summary>Every invalid example, which a reader refuses with its expected diagnostic.</summary>
let invalidExamples: ContextExample list =
    FixtureCatalog.Context.Examples
    |> Seq.filter (fun e -> not e.IsValid)
    |> List.ofSeq

/// <summary>Which files of the examples' tree are compacted.</summary>
type CompactedFiles =
    /// <summary>None: every file is its plain twin.</summary>
    | NoFile
    /// <summary>Only the value file of <c>line-total</c>.</summary>
    | ValueFileOnly
    /// <summary>The manifest, the module file, the type file and the value file.</summary>
    | EveryFile

/// The location in a tree of each tree-file example, by its kind.
let private locations =
    [
        "manifest", "manifest.json"
        "module", "pkg/my-org/billing/invoicing/module.json"
        "type", "pkg/my-org/billing/invoicing/invoice-line.type.json"
        "value", "pkg/my-org/billing/invoicing/line-total.value.json"
    ]

/// <summary>
/// The examples' model as a tree of files, by location, for IR <paramref name="ir"/> (3 or 4): each file the
/// example of its kind when it is compacted, or the example's plain twin otherwise.
/// </summary>
let tree (ir: int) (compacted: CompactedFiles) : (string * string) list =
    [
        for kind, location in locations ->
            let id = $"v{ir}-{kind}"

            let isCompacted =
                match compacted with
                | NoFile -> false
                | ValueFileOnly -> kind = "value"
                | EveryFile -> true

            location,
            if isCompacted then
                FixtureCatalog.ReadText (example id).File
            else
                expandedTextOf id
    ]
