/// <summary>
/// Morphir files, plain and compacted, from the parent's name-context examples that <c>FixtureCatalog.Context</c>
/// pins. The examples share one small model, package <c>my-org/billing</c> with module <c>invoicing</c>, the type
/// <c>invoice-line</c> and the value <c>line-total</c>, so their tree-file examples are also the files of one tree.
/// A test takes its files from here and never writes an envelope by hand.
/// </summary>
module Morphir.Models.TestSupport.MorphirFileMother

open System.Text.Json.Nodes
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

/// <summary>
/// The examples' model as a tree of files, by location, for IR <paramref name="ir"/> (3 or 4): each file the
/// example of its kind when it is compacted, or the example's plain twin otherwise
/// (<c>FixtureCatalog.Context.Tree</c>).
/// </summary>
let tree (ir: int) (compacted: CompactedFiles) : (string * string) list =
    let which =
        match compacted with
        | NoFile -> ContextTreeCompaction.NoFile
        | ValueFileOnly -> ContextTreeCompaction.ValueFileOnly
        | EveryFile -> ContextTreeCompaction.EveryFile

    [
        for file in FixtureCatalog.Context.Tree(ir, which).Files ->
            file.Location, FixtureCatalog.ReadText file.Entry
    ]

/// <summary>The parent's compaction policy case with this id, such as <c>name-used-once</c>.</summary>
let policyCase (id: string) : PolicyCase = FixtureCatalog.Context.Policy.Case id

/// <summary>Every case of the parent's compaction policy, in its order.</summary>
let policyCases: PolicyCase list = List.ofSeq FixtureCatalog.Context.Policy.All

/// <summary>
/// The plain document of a policy case, the input a writer of the policy compacts. Its <c>formatVersion</c> gives
/// its IR version.
/// </summary>
let policyInput (id: string) : System.Text.Json.JsonElement =
    use document = System.Text.Json.JsonDocument.Parse(FixtureCatalog.ReadText (policyCase id).Input)
    document.RootElement.Clone()

/// <summary>The bytes a writer of the policy must produce from a case's input, as the parent fixes them.</summary>
let policyExpectedBytes (id: string) : byte array = FixtureCatalog.ReadBytes (policyCase id).Expected

/// <summary>
/// A pinned valid example with one context entry spelled anew, and the plain document a reader must give for it.
/// </summary>
type RespelledFile = {
    /// <summary>The example's file name, which diagnostics name.</summary>
    File: string
    /// <summary>The IR version of the enclosed document: 3 or 4.</summary>
    Ir: int
    /// <summary>The file's bytes: the example with the entry's spelling replaced.</summary>
    Bytes: byte array
    /// <summary>The example's twin with the value at each reference to the entry replaced by its new expansion.</summary>
    ExpandedText: string
}

/// The twin with each value that a reference to <paramref name="id"/> in the compacted document stands for replaced by
/// <paramref name="expansion"/>. The two documents have the same shape except at references, so they are walked
/// together.
let rec private replaceReferences (id: string) (expansion: JsonNode) (compacted: JsonNode) (twin: JsonNode) : JsonNode =
    match compacted with
    | :? JsonObject as reference when
        reference.Count = 1
        && reference.ContainsKey "$name"
        && reference["$name"].GetValueKind() = System.Text.Json.JsonValueKind.String
        ->
        // A reference to another entry stands for the twin's value there.
        if reference["$name"].GetValue<string>() = id then
            expansion.DeepClone()
        else
            twin.DeepClone()
    | :? JsonObject as compactedObject ->
        let result = JsonObject()

        for KeyValue(name, value) in twin.AsObject() do
            result[name] <-
                match value with
                | null -> null
                | value -> replaceReferences id expansion (nonNull compactedObject[name]) value

        result
    | :? JsonArray as compactedArray ->
        let result = JsonArray()

        twin.AsArray()
        |> Seq.iteri (fun index item ->
            result.Add(
                match item with
                | null -> null
                | item -> replaceReferences id expansion (nonNull compactedArray[index]) item
            )
        )

        result
    | _ -> twin.DeepClone()

/// The example <paramref name="exampleId"/> whose entry <paramref name="id"/> of <paramref name="table"/> is
/// <paramref name="spelling"/>, where a reference to it expands to <paramref name="expansion"/>.
let private respelled exampleId (table: string) (id: string) (spelling: string) (expansion: JsonNode) =
    let example = example exampleId
    let envelope = nonNull (JsonNode.Parse(FixtureCatalog.ReadText example.File))
    let context = nonNull envelope["context"]
    let entries: JsonNode = nonNull context[table]

    if not (entries.AsObject().ContainsKey id) then
        failwith $"The context example '{exampleId}' has no entry '{id}' in {table}."

    entries[id] <- JsonValue.Create spelling

    let twin = nonNull (JsonNode.Parse(expandedTextOf exampleId))
    let expanded = replaceReferences id expansion (nonNull envelope["document"]) twin

    {
        File = System.IO.Path.GetFileName example.File.RelativePath
        Ir = example.Ir
        Bytes = System.Text.Encoding.UTF8.GetBytes(envelope.ToJsonString())
        ExpandedText = expanded.ToJsonString()
    }

/// <summary>
/// The v4 type file whose <c>names</c> entry for the type's own name is <paramref name="spelling"/>; the name expands
/// to exactly that text.
/// </summary>
let withNameSpelling (spelling: string) : RespelledFile =
    respelled "v4-type" "names" "t" spelling (JsonValue.Create spelling)

/// <summary>
/// The v4 type file whose <c>fqNames</c> entry for its field types is <paramref name="spelling"/>; each field type
/// expands to exactly that text.
/// </summary>
let withFqNameSpelling (spelling: string) : RespelledFile =
    respelled "v4-type" "fqNames" "i" spelling (JsonValue.Create spelling)

/// <summary>
/// The v4 module file whose <c>modulePaths</c> entry for its path is <paramref name="spelling"/>; the path expands to
/// exactly that text.
/// </summary>
let withModulePathSpelling (spelling: string) : RespelledFile =
    respelled "v4-module" "modulePaths" "m" spelling (JsonValue.Create spelling)

/// <summary>
/// The v4 manifest whose <c>packagePaths</c> entry for its package is <paramref name="spelling"/>; the package expands
/// to exactly that text.
/// </summary>
let withPackagePathSpelling (spelling: string) : RespelledFile =
    respelled "v4-manifest" "packagePaths" "p" spelling (JsonValue.Create spelling)

/// <summary>
/// The classic type file whose <c>names</c> entry for a record field's name is <paramref name="spelling"/>; the field
/// name, a classic slot, expands to the word array <paramref name="words"/>.
/// </summary>
let withClassicNameSpelling (spelling: string) (words: string seq) : RespelledFile =
    let array = JsonArray()

    for word in words do
        array.Add(JsonValue.Create word)

    respelled "v3-type" "names" "u" spelling array

/// <summary>
/// The v3 manifest whose <c>packagePaths</c> entry for its package is <paramref name="spelling"/>. The package is an
/// envelope member of a tree file, which holds a canonical string, so it expands to exactly that text.
/// </summary>
let withTreeManifestPackagePathSpelling (spelling: string) : RespelledFile =
    respelled "v3-manifest" "packagePaths" "p" spelling (JsonValue.Create spelling)

/// <summary>A classic v3 slot that a context entry's references fill, by the table it resolves through.</summary>
type ClassicSlot =
    /// <summary>A record field's name in the classic type file (<c>context.names.u</c>).</summary>
    | ClassicName
    /// <summary>The package of the classic single file's distribution (<c>context.packagePaths.p</c>).</summary>
    | ClassicPackagePath
    /// <summary>The path of the classic single file's module (<c>context.modulePaths.v</c>).</summary>
    | ClassicModulePath
    /// <summary>The field types of the classic type file (<c>context.fqNames.i</c>).</summary>
    | ClassicFqName

/// <summary>A pinned classic example with the entry that fills one classic slot spelled anew.</summary>
type ClassicSlotFile = {
    /// <summary>The example's file name, which diagnostics name.</summary>
    File: string
    /// <summary>The file's bytes: the example with the entry's spelling replaced.</summary>
    Bytes: byte array
    /// <summary>The entry, as a diagnostic names it, such as <c>context.names.u</c>.</summary>
    Entry: string
}

/// <summary>
/// The classic example whose entry for <paramref name="slot"/> is <paramref name="spelling"/>. Every reference to the
/// entry is at a classic slot, which holds word arrays; use it for a spelling that a reader must refuse there.
/// </summary>
let inClassicSlot (slot: ClassicSlot) (spelling: string) : ClassicSlotFile =
    let exampleId, table, id =
        match slot with
        | ClassicName -> "v3-type", "names", "u"
        | ClassicPackagePath -> "v3-single-file", "packagePaths", "p"
        | ClassicModulePath -> "v3-single-file", "modulePaths", "v"
        | ClassicFqName -> "v3-type", "fqNames", "i"

    // The expansion is not used: the file is refused.
    let file = respelled exampleId table id spelling (JsonValue.Create spelling)

    {
        File = file.File
        Bytes = file.Bytes
        Entry = $"context.{table}.{id}"
    }

/// <summary>The parent's naming conformance corpus, in its shipped style.</summary>
let namingCorpus () : NamingConformance = FixtureCatalog.Context.Naming.Corpus
