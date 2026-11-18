namespace Morphir.IR.Files

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Collections.Generic
open System.IO
open System.Runtime.CompilerServices
open System.Text.Json
open Morphir.IR
open Morphir.IR.Trees
open Morphir.Json
open ContextExpansion

/// <summary>
/// The writer policy <c>morphir.compact/0.1.0-draft.1</c> of the parent's <c>ir/name-compaction</c>: which names of a
/// plain Morphir file get an alias in the file's own context, with which id, and whether the envelope is written at
/// all. The parent fixes the policy and the bytes of its cases; this module carries the policy out and defines
/// nothing of it.
/// </summary>
/// <remarks>
/// <para>
/// A candidate is a table with a spelling, and its occurrences are the outermost typed name positions that hold it:
/// the positions where expansion (<c>ContextExpansion</c>) resolves a reference, of the IR family of the file. A
/// position is a candidate's occurrence only when expansion would give its value back: a v4 string, or a v3 tree
/// file's envelope member, whose text is a spelling of the table's type within the limits of draft.1; or a classic
/// slot whose word arrays read as a spelling that reads back as those same words. A spelling that a classic slot cannot
/// hold, such as <c>FR2052A</c>, is never aliased there. An occurrence weighs the bytes of its value in the canonical
/// JSON (RFC 8785) of the plain file: a string with its quotes, or the classic word arrays.
/// </para>
/// <para>
/// Candidates are taken in the order of their first occurrence in the canonical JSON of the plain file, which is the
/// order of a walk of the document with each object's members sorted as JCS sorts them. Two outermost positions never
/// start at the same offset, so the further keys of the policy (the spelling, then the table) never decide. Each
/// candidate takes the next id of the bijective base-26 sequence <c>a</c> … <c>z</c>, <c>aa</c>, … when its saving, the
/// sum over its occurrences of the occurrence's bytes less the 12 + len(id) bytes of a reference, is greater than the
/// len(id) + len(spelling) + 6 bytes of its table entry; a candidate that stays spelled out takes no id, and a table
/// holds at most 16,384 entries. The envelope is written only when the savings are greater than what it adds: 118
/// fixed bytes, len(table) + 5 for each table that holds an entry, len(id) + len(spelling) + 5 for each entry and a
/// comma between two entries. Otherwise the file is the plain document in canonical JSON.
/// </para>
/// <para>
/// The file is written in canonical JSON, with one exception (the controller's ruling of add-model-convert, to which
/// the parent's policy is being amended): an object whose members' order the IR schema says is the model's keeps its
/// members in document order. In v4 these are the parameters of a value, a definition's <c>inputTypes</c> and a
/// specification's <c>inputs</c> (<c>Shape.KeepsOrder</c>). Strings, numbers and whitespace are canonical everywhere,
/// every size is measured on the file so written, and first use is the order of that file.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module CompactionPolicy =
    /// <summary>
    /// The name of the policy, which a writer gives in its result: <c>morphir.compact/0.1.0-draft.1</c>. C# calls this
    /// <c>Id</c>.
    /// </summary>
    [<CompiledName("Id")>]
    let id: string = "morphir.compact/0.1.0-draft.1"

    /// <summary>
    /// The bytes that the envelope adds besides its tables and its document: <c>{"$schema":"</c> (12), the URL,
    /// <c>","context":{</c> (13), <c>},"document":</c> (13) and <c>}</c> (1), 118 for the URL of draft.1.
    /// </summary>
    let envelopeBytes: int =
        12
        + (CanonicalJson.byteLength MorphirFile.Draft1Schema - 2)
        + 13
        + 13
        + 1

    /// <summary>
    /// The alias id of a 1-based position in the sequence: <c>a</c> to <c>z</c>, then <c>aa</c>, <c>ab</c> and on, the
    /// bijective base-26 numerals over <c>a</c> to <c>z</c>.
    /// </summary>
    let aliasId (position: int) : string =
        if position < 1 then
            invalidArg (nameof position) "An alias position starts at 1."

        let rec digits (n: int) (acc: char list) =
            if n = 0 then
                acc
            else
                let digit = (n - 1) % 26
                digits ((n - 1) / 26) (char (int 'a' + digit) :: acc)

        String(Array.ofList (digits position []))

    let private limits = ContextLimits.draft1

    /// The length of a text as JCS writes it inside a string, without its quotes.
    let private len (text: string) = CanonicalJson.byteLength text - 2

    /// The order of the tables when two candidates tie on their first use and their spelling.
    let private rank table =
        match table with
        | NamesTable -> 0
        | PackagePathsTable -> 1
        | ModulePathsTable -> 2
        | FqNamesTable -> 3

    /// The value of a spelling that a table admits: of the table's type by the segment grammar, read by its parser, and
    /// within the spelling limit.
    let private admits (isSpelling: string -> bool) (parse: string -> Result<'v, string>) (spelling: string) =
        if len spelling <= limits.MaxNameLength && isSpelling spelling then
            match parse spelling with
            | Ok value -> Some value
            | Error _ -> None
        else
            None

    /// Whether a table admits a spelling.
    let private admitted table (spelling: string) =
        match table with
        | NamesTable -> (admits NameContext.isName CanonicalNames.parseName spelling).IsSome
        | PackagePathsTable
        | ModulePathsTable -> (admits NameContext.isPath CanonicalNames.parsePath spelling).IsSome
        | FqNamesTable -> (admits NameContext.isFqName CanonicalNames.parseFQName spelling).IsSome

    /// The words of a classic name: an array of strings.
    let private classicName (element: JsonElement) =
        if element.ValueKind = JsonValueKind.Array then
            let words = [ for item in element.EnumerateArray() -> item ]

            if words |> List.forall (fun word -> word.ValueKind = JsonValueKind.String) then
                Some(Name [ for word in words -> nonNull (word.GetString()) ])
            else
                None
        else
            None

    /// A classic path: an array of classic names.
    let private classicPath (element: JsonElement) =
        if element.ValueKind = JsonValueKind.Array then
            let names = [ for item in element.EnumerateArray() -> classicName item ]

            if names |> List.forall Option.isSome then
                Some(Path(List.choose (fun name -> name) names))
            else
                None
        else
            None

    /// A classic FQName: the package path, the module path and the local name.
    let private classicFqName (element: JsonElement) =
        if element.ValueKind = JsonValueKind.Array && element.GetArrayLength() = 3 then
            match classicPath element[0], classicPath element[1], classicName element[2] with
            | Some package, Some modulePath, Some local -> Some(FQName.fqNameFromPaths package modulePath local)
            | _ -> None
        else
            None

    /// <summary>
    /// The spelling of a classic slot's word arrays, when the slot can hold it: the spelling is admitted by its table,
    /// and it reads back as exactly the words of the slot, so expansion gives the same arrays.
    /// </summary>
    let private classicSpelling table (element: JsonElement) =
        let spell
            (decode: JsonElement -> 'v option)
            (print: 'v -> string)
            (isSpelling: string -> bool)
            (parse: string -> Result<'v, string>)
            =
            decode element
            |> Option.bind (fun value ->
                let spelling = print value

                match admits isSpelling parse spelling with
                | Some parsed when parsed = value -> Some spelling
                | _ -> None
            )

        match table with
        | NamesTable -> spell classicName CanonicalNames.nameToString NameContext.isName CanonicalNames.parseName
        | PackagePathsTable
        | ModulePathsTable -> spell classicPath CanonicalNames.pathToString NameContext.isPath CanonicalNames.parsePath
        | FqNamesTable -> spell classicFqName CanonicalNames.fqNameToString NameContext.isFqName CanonicalNames.parseFQName

    /// The bytes of a classic slot's arrays in canonical JSON.
    let rec private classicBytes (element: JsonElement) : int =
        match element.ValueKind with
        | JsonValueKind.Array ->
            let items = [ for item in element.EnumerateArray() -> classicBytes item ]
            2 + max 0 (items.Length - 1) + List.sum items
        | _ -> CanonicalJson.byteLength (nonNull (element.GetString()))

    /// An occurrence at a position: its table, its spelling and its bytes in canonical JSON.
    let private occurrence (shape: Shape) (element: JsonElement) =
        match shape.Reference with
        | Some(table, CanonicalString) when element.ValueKind = JsonValueKind.String ->
            let spelling = nonNull (element.GetString())

            if admitted table spelling then
                Some(table, spelling, CanonicalJson.byteLength spelling)
            else
                None
        | Some(table, ClassicArrays) ->
            classicSpelling table element
            |> Option.map (fun spelling -> table, spelling, classicBytes element)
        | _ -> None

    /// A candidate: a table with a spelling, the index of its first occurrence in the walk, and its occurrences.
    type private Candidate(table: Table, spelling: string, first: int) =
        member _.Table = table
        member _.Spelling = spelling
        member _.First = first
        member val Count = 0L with get, set
        member val Bytes = 0L with get, set

    /// What a walk of the plain document found: its candidates, the references that a plain document cannot hold, and
    /// the JSON Pointers of the objects whose members keep their order.
    type private Found = {
        Candidates: Dictionary<struct (Table * string), Candidate>
        mutable Next: int
        References: ResizeArray<Table * string>
        Ordered: HashSet<string>
    }

    /// A member name as a JSON Pointer reference token (RFC 6901).
    let private token (name: string) = name.Replace("~", "~0").Replace("/", "~1")

    /// <summary>
    /// Walks the plain document in the order of the file as written, and records each occurrence of a candidate and
    /// each object that keeps its members' order (<c>Shape.KeepsOrder</c>). The file as written is canonical JSON,
    /// whose objects have their members sorted, except an object that keeps its members' order. A position inside an
    /// occurrence is not walked: candidates are taken at the outermost positions.
    /// </summary>
    let rec private collect (found: Found) (pointer: string) (shape: Shape) (element: JsonElement) =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        match occurrence shape element with
        | Some(table, spelling, bytes) ->
            let key = struct (table, spelling)

            let candidate =
                match found.Candidates.TryGetValue key with
                | true, candidate -> candidate
                | false, _ ->
                    let candidate = Candidate(table, spelling, found.Next)
                    found.Next <- found.Next + 1
                    found.Candidates[key] <- candidate
                    candidate

            candidate.Count <- candidate.Count + 1L
            candidate.Bytes <- candidate.Bytes + int64 bytes
        | None ->
            match shape.Reference, referenceId element with
            | Some(table, _), Some referenced -> found.References.Add((table, referenced))
            | _ ->
                match element.ValueKind with
                | JsonValueKind.Object ->
                    let members =
                        if shape.KeepsOrder then
                            found.Ordered.Add pointer |> ignore
                            Seq.ofArray (Array.ofSeq (element.EnumerateObject()))
                        else
                            element.EnumerateObject()
                            |> Seq.sortWith (fun a b -> String.CompareOrdinal(a.Name, b.Name))

                    for property in members do
                        match shape.Member element property.Name property.Value with
                        | Some inner -> collect found $"{pointer}/{token property.Name}" inner property.Value
                        | None -> ()
                | JsonValueKind.Array ->
                    element.EnumerateArray()
                    |> Seq.iteri (fun index item ->
                        match shape.Item element index with
                        | Some inner -> collect found $"{pointer}/{index}" inner item
                        | None -> ()
                    )
                | _ -> ()

    /// Writes the document with each occurrence of an aliased candidate replaced by its reference.
    let rec private substitute
        (aliases: Dictionary<struct (Table * string), string>)
        (writer: Utf8JsonWriter)
        (shape: Shape)
        (element: JsonElement)
        =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        let alias =
            occurrence shape element
            |> Option.bind (fun (table, spelling, _) ->
                match aliases.TryGetValue(struct (table, spelling)) with
                | true, aliasId -> Some aliasId
                | false, _ -> None
            )

        match alias with
        | Some aliasId ->
            writer.WriteStartObject()
            writer.WriteString("$name", aliasId)
            writer.WriteEndObject()
        | None ->
            match element.ValueKind with
            | JsonValueKind.Object ->
                writer.WriteStartObject()

                for property in element.EnumerateObject() do
                    writer.WritePropertyName property.Name

                    match shape.Member element property.Name property.Value with
                    | Some inner -> substitute aliases writer inner property.Value
                    | None -> property.Value.WriteTo writer

                writer.WriteEndObject()
            | JsonValueKind.Array ->
                writer.WriteStartArray()

                element.EnumerateArray()
                |> Seq.iteri (fun index item ->
                    match shape.Item element index with
                    | Some inner -> substitute aliases writer inner item
                    | None -> item.WriteTo writer
                )

                writer.WriteEndArray()
            | _ -> element.WriteTo writer

    /// The nesting that the policy's writers go to; the canonical JSON writer refuses a deeper value itself.
    let private maxDepth = 64 * 1024

    let private error code message : StoreError = StoreErrors.error code None message

    let private tooDeep () =
        [ error "depth_limit_exceeded" "The document is nested too deeply to write in canonical JSON." ]

    /// <summary>
    /// The bytes of a plain Morphir file of <paramref name="family"/> under the policy, and whether an alias was worth
    /// its cost: the envelope of <c>ir-context</c> 0.1.0-draft.1 in canonical JSON (RFC 8785) with the aliases the
    /// policy chooses, or, when no alias is worth its cost or the envelope would not make the file smaller, the plain
    /// document in canonical JSON. In both, an object that carries its members' order, such as a v4 value's
    /// parameters, keeps it. Expanding the bytes gives the document back. The same document always gives the same
    /// bytes. C# calls this <c>Compact</c>.
    /// </summary>
    /// <remarks>
    /// The document must be plain: a top-level <c>$schema</c>, which marks an envelope, is refused with
    /// <c>unknown_member</c>, and a reference object at a typed name position with <c>invalid_type</c>. A value that
    /// canonical JSON cannot hold (a lone surrogate, a number outside the range of a double, a member named twice) is
    /// <c>invalid_json</c>, and a document nested more deeply than the canonical JSON writer reads (1,024 levels), or
    /// than the stack holds, is <c>depth_limit_exceeded</c>. The errors name no file; a caller adds it.
    /// </remarks>
    [<CompiledName("Compact")>]
    let compact (family: IrFamily) (document: JsonElement) : Result<byte array * bool, StoreError list> =
        let mutable schema = Unchecked.defaultof<JsonElement>

        if document.ValueKind = JsonValueKind.Object && document.TryGetProperty("$schema", &schema) then
            Error [
                error
                    "unknown_member"
                    "$schema: a plain document has no $schema member, which marks a compaction envelope."
            ]
        else
            let root = rootOf family

            let found = {
                Candidates = Dictionary()
                Next = 0
                References = ResizeArray()
                Ordered = HashSet(StringComparer.Ordinal)
            }

            let walked =
                try
                    collect found "" root document
                    true
                with :? InsufficientExecutionStackException ->
                    false

            // The file as written: canonical JSON, with the members of an order-carrying object in document order.
            let plain =
                if not walked then
                    Error(tooDeep ())
                else
                    try
                        Ok(CanonicalJson.writeKeepingOrder found.Ordered.Contains document)
                    with
                    | :? JsonException -> Error(tooDeep ())
                    | :? InsufficientExecutionStackException -> Error(tooDeep ())
                    | :? ArgumentException as problem -> Error [ error "invalid_json" problem.Message ]

            plain
            |> Result.bind (fun plain ->
                if found.References.Count > 0 then
                    found.References
                    |> Seq.distinct
                    |> Seq.map (fun (table, referenced) ->
                        error
                            "invalid_type"
                            $"A plain document holds the reference {{\"$name\": \"{referenced}\"}} at a position of context.{tableName table}, where it needs the spelling itself."
                    )
                    |> List.ofSeq
                    |> Error
                else
                    // The alias decision, in first-use order.
                    let ordered =
                        found.Candidates.Values
                        |> Seq.sortWith (fun a b ->
                            match compare a.First b.First with
                            | 0 ->
                                match String.CompareOrdinal(a.Spelling, b.Spelling) with
                                | 0 -> compare (rank a.Table) (rank b.Table)
                                | bySpelling -> bySpelling
                            | byFirst -> byFirst
                        )
                        |> List.ofSeq

                    let aliases = Dictionary<struct (Table * string), string>()
                    let entries = Dictionary<Table, ResizeArray<string * string>>()
                    let mutable savings = 0L
                    let mutable entryBytes = 0L

                    for candidate in ordered do
                        let next = aliasId (aliases.Count + 1)
                        let reference = int64 (12 + len next)
                        let saving = candidate.Bytes - candidate.Count * reference
                        let entry = int64 (len next + len candidate.Spelling + 6)

                        let held =
                            match entries.TryGetValue candidate.Table with
                            | true, table -> table.Count
                            | false, _ -> 0

                        if saving > entry && held < limits.MaxEntries then
                            aliases[struct (candidate.Table, candidate.Spelling)] <- next

                            match entries.TryGetValue candidate.Table with
                            | true, table -> table.Add((next, candidate.Spelling))
                            | false, _ -> entries[candidate.Table] <- ResizeArray [ next, candidate.Spelling ]

                            savings <- savings + saving
                            entryBytes <- entryBytes + int64 (len next + len candidate.Spelling + 5)

                    // The loss decision: the envelope's own bytes against the savings.
                    let headers =
                        entries.Keys
                        |> Seq.sumBy (fun table -> int64 (len (tableName table) + 5))

                    let overhead =
                        int64 envelopeBytes
                        + headers
                        + entryBytes
                        + int64 (aliases.Count - 1)

                    if aliases.Count = 0 || savings <= overhead then
                        Ok(plain, false)
                    else
                        try
                            use buffer = new MemoryStream()

                            do
                                use writer = new Utf8JsonWriter(buffer, JsonWriterOptions(MaxDepth = maxDepth))
                                writer.WriteStartObject()
                                writer.WriteString("$schema", MorphirFile.Draft1Schema)
                                writer.WriteStartObject "context"

                                for KeyValue(table, written) in entries do
                                    writer.WriteStartObject(tableName table)

                                    for aliasId, spelling in written do
                                        writer.WriteString(aliasId, spelling)

                                    writer.WriteEndObject()

                                writer.WriteEndObject()
                                writer.WritePropertyName "document"
                                substitute aliases writer root document
                                writer.WriteEndObject()

                            use envelope =
                                JsonDocument.Parse(ReadOnlyMemory(buffer.ToArray()), JsonDocumentOptions(MaxDepth = maxDepth))

                            // The document is the envelope's member document, so its objects are below /document.
                            let keepsOrder (pointer: string) =
                                pointer.StartsWith("/document", StringComparison.Ordinal)
                                && found.Ordered.Contains(pointer.Substring "/document".Length)

                            Ok(CanonicalJson.writeKeepingOrder keepsOrder envelope.RootElement, true)
                        with
                        | :? JsonException
                        | :? InsufficientExecutionStackException -> Error(tooDeep ())
            )
