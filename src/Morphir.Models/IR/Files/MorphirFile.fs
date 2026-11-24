namespace Morphir.IR.Files

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Text.Json
open Morphir.IR.Trees

/// <summary>
/// A Morphir file as it is stored: a plain document, or an envelope (<c>ir/name-compaction</c>) whose document uses
/// the names of its file-local context.
/// </summary>
/// <typeparam name="doc">The document: its JSON, or what a codec reads from it.</typeparam>
type MorphirFile<'doc> = {
    /// <summary>The envelope's <c>$schema</c> URL, or none for a plain file.</summary>
    Schema: Uri option
    /// <summary>The envelope's name tables, or none for a plain file.</summary>
    Context: FileContext option
    /// <summary>The document, which keeps its own <c>formatVersion</c>.</summary>
    Document: 'doc
}

[<RequireQualifiedAccess>]
type internal ContextOrder =
    | WholeFile
    | Bounded
    | Streaming

/// <summary>
/// Reads Morphir files, plain or compacted, and expands a compacted file's document to the plain document it stands
/// for. A file is compacted when its top-level object has <c>$schema</c>, which no plain Morphir document has.
/// </summary>
/// <remarks>
/// <para>
/// Every refusal names the file and gives no document. The codes are the parent's: <c>context.undeclared_schema</c>,
/// a <c>$schema</c> this reader does not declare; <c>context.after_document</c>, a <c>document</c> member before the
/// <c>context</c> member, which a whole-file reader refuses; <c>context.duplicate_entry</c>, a member twice in a
/// table, the context or the envelope; <c>context.unknown_critical</c>, a <c>critical</c> path draft.1 does not
/// recognize; <c>context.too_many_entries</c> and <c>context.name_too_long</c>, the limits; and
/// <c>context.unknown_alias</c>, a reference whose id its position's table does not define. The parent names no code
/// for a malformed envelope or entry, so this reader uses <c>context.invalid_envelope</c> (no <c>context</c> or
/// <c>document</c> object, or a <c>critical</c> that is not a list of strings) and <c>context.invalid_entry</c> (an
/// id that is not ASCII letters and digits starting with a letter, or a spelling that is not the canonical spelling
/// of its table's type). A file that is not JSON is <c>invalid_json</c>, as in the document-tree store, and a file
/// nested deeper than the <c>MaxDepth</c> of its limits, as written or once expanded, is <c>depth_limit_exceeded</c>.
/// </para>
/// <para>
/// Unknown optional members of the envelope and of the context are ignored. Context bytes count toward a caller's
/// limit on the size of a file, as they are part of its bytes.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module MorphirFile =
    /// <summary>The envelope schema URL of <c>ir-context</c> 0.1.0-draft.1.</summary>
    [<Literal>]
    let Draft1Schema =
        "https://morphir.finos.org/schemas/ir-context/0.1.0-draft.1/document.schema.json"

    /// <summary>The envelope schema URLs that this reader declares.</summary>
    let declaredSchemas: Uri list = [ Uri Draft1Schema ]

    /// <summary>
    /// Whether <paramref name="schema"/>, the text of an envelope's <c>$schema</c>, is a URL that this reader declares,
    /// compared ordinally. C# calls this <c>IsDeclared</c>.
    /// </summary>
    [<CompiledName("IsDeclared")>]
    let isDeclared (schema: string) =
        declaredSchemas
        |> List.exists (fun url ->
            String.Equals(url.OriginalString, schema, StringComparison.Ordinal)
        )

    let private error code file message : StoreError =
        StoreErrors.error code (Some file) message

    /// <summary>
    /// The IR family of a document by its <c>formatVersion</c>: classic v3 for the string <c>"3"</c> or one that starts
    /// with <c>"3."</c>, or a number from 3 up to 4, such as <c>3</c>; v4 for anything else, which the v4 reader then
    /// checks.
    /// </summary>
    let familyOf (document: JsonElement) : IrFamily =
        let mutable version = Unchecked.defaultof<JsonElement>
        let mutable number = 0.0

        let isClassic =
            document.ValueKind = JsonValueKind.Object
            && document.TryGetProperty("formatVersion", &version)
            && (
                match version.ValueKind with
                | JsonValueKind.String ->
                    let text = nonNull (version.GetString())

                    text = "3"
                    || text.StartsWith("3.", StringComparison.Ordinal)
                | JsonValueKind.Number ->
                    version.TryGetDouble(&number)
                    && number
                       >= 3.0
                    && number < 4.0
                | _ -> false
            )

        if isClassic then IrFamily.Classic3 else IrFamily.V4

    /// <summary>
    /// Whether a document is classic v3 by its <c>formatVersion</c>, as <see cref="familyOf"/> decides it: a tree's
    /// manifest that is classic makes a v3 tree. C# calls this <c>IsClassic</c>.
    /// </summary>
    [<CompiledName("IsClassic")>]
    let isClassic (document: JsonElement) : bool = familyOf document = IrFamily.Classic3

    /// The bytes after a UTF-8 byte order mark.
    let private withoutByteOrderMark (bytes: ReadOnlyMemory<byte>) =
        let span = bytes.Span

        if
            span.Length >= 3
            && span[0] = 0xEFuy
            && span[1] = 0xBBuy
            && span[2] = 0xBFuy
        then
            bytes.Slice 3
        else
            bytes

    /// Whether the bytes are JSON once no limit of depth applies: after a parse with a limit failed, it failed on the
    /// depth. A reader of JSON keeps no stack per level, so reading deep text is safe.
    let private isJsonAtAnyDepth (bytes: ReadOnlyMemory<byte>) =
        try
            let mutable reader = Utf8JsonReader(bytes.Span, JsonReaderOptions(MaxDepth = Int32.MaxValue))

            while reader.Read() do
                ()

            true
        with :? JsonException ->
            false

    let private envelope
        orderPolicy
        (limits: ContextLimits)
        file
        (root: JsonElement)
        : Result<MorphirFile<JsonElement>, StoreError list> =
        let members, duplicates = NameContext.membersOnce file "The envelope" root

        let find name =
            members
            |> List.tryFindIndex (fun (key, _) -> key = name)

        let schemaText =
            members
            |> List.pick (fun (key, value) -> if key = "$schema" then Some value else None)
            |> fun value ->
                NameContext.tryString value
                |> Option.defaultWith value.GetRawText

        let declared =
            declaredSchemas
            |> List.tryFind (fun url -> url.OriginalString = schemaText)

        match declared with
        | None ->
            let urls =
                declaredSchemas
                |> List.map (fun url -> url.OriginalString)
                |> String.concat ", "

            Error [
                error
                    "context.undeclared_schema"
                    file
                    $"The envelope's $schema is '{schemaText}', and this reader declares only {urls}."
            ]
        | Some schema ->
            let memberAt index = snd members[index]

            let order =
                match orderPolicy, find "context", find "document", find "$schema" with
                | ContextOrder.Bounded, _, _, _ -> []
                | _, Some contextAt, Some documentAt, _ when documentAt < contextAt -> [
                    error
                        "context.after_document"
                        file
                        "The document comes before the context; the context must come first."
                  ]
                | ContextOrder.Streaming, _, Some documentAt, Some schemaAt when
                    documentAt < schemaAt
                      ->
                      [
                          error
                              "context.after_document"
                              file
                              "The document comes before $schema; a streaming reader needs the envelope and context first."
                      ]
                | _ -> []

            let missing = [
                for name in [ "context"; "document" ] do
                    match find name with
                    | Some index when (memberAt index).ValueKind = JsonValueKind.Object -> ()
                    | Some _ ->
                        error
                            "context.invalid_envelope"
                            file
                            $"The envelope's {name} is not an object."
                    | None -> error "context.invalid_envelope" file $"The envelope has no {name}."
            ]

            let critical, criticalProblems =
                match find "critical" with
                | None -> [], []
                | Some index when (memberAt index).ValueKind = JsonValueKind.Array ->
                    let items = [ for item in (memberAt index).EnumerateArray() -> item ]
                    let paths = List.choose NameContext.tryString items

                    if
                        paths.Length
                        <> items.Length
                    then
                        [],
                        [
                            error
                                "context.invalid_envelope"
                                file
                                "The envelope's critical is not a list of strings."
                        ]
                    else
                        paths,
                        paths
                        |> List.filter (fun path ->
                            not (List.contains path NameContext.recognizedCritical)
                        )
                        |> List.map (fun path ->
                            error
                                "context.unknown_critical"
                                file
                                $"The envelope marks '{path}' critical, and this reader does not recognize it."
                        )
                | Some _ ->
                    [],
                    [
                        error
                            "context.invalid_envelope"
                            file
                            "The envelope's critical is not a list of strings."
                    ]

            let context, contextProblems =
                match find "context" with
                | Some index when (memberAt index).ValueKind = JsonValueKind.Object ->
                    NameContext.read limits file critical (memberAt index)
                | _ -> NameContext.empty critical, []

            match
                duplicates
                @ order
                @ missing
                @ criticalProblems
                @ contextProblems
            with
            | [] ->
                Ok
                    {
                        Schema = Some schema
                        Context = Some context
                        Document = (memberAt (Option.get (find "document"))).Clone()
                    }
            | problems -> Error problems

    let internal readEnvelopeRoot orderPolicy limits file (root: JsonElement) =
        let mutable schema = Unchecked.defaultof<JsonElement>

        if
            root.ValueKind = JsonValueKind.Object
            && root.TryGetProperty("$schema", &schema)
        then
            envelope orderPolicy limits file root
        else
            Ok
                {
                    Schema = None
                    Context = None
                    Document = root.Clone()
                }

    /// <summary>
    /// Inspects a raw JSON/YAML root before context expansion and returns the first optional envelope or context
    /// member (such as <c>context.prefixes</c>) that plain JSON IR cannot preserve.
    /// </summary>
    let internal findUnrepresentableEnvelopeMember (root: JsonElement) : string option =
        let mutable schema = Unchecked.defaultof<JsonElement>

        if root.ValueKind <> JsonValueKind.Object || not (root.TryGetProperty("$schema", &schema)) then
            None
        else
            let envelopeUnknown =
                root.EnumerateObject()
                |> Seq.tryPick (fun prop ->
                    match prop.Name with
                    | "$schema"
                    | "critical"
                    | "context"
                    | "document" -> None
                    | other -> Some $"envelope.{other}"
                )

            match envelopeUnknown with
            | Some found -> Some found
            | None ->
                let mutable ctx = Unchecked.defaultof<JsonElement>

                if root.TryGetProperty("context", &ctx) && ctx.ValueKind = JsonValueKind.Object then
                    ctx.EnumerateObject()
                    |> Seq.tryPick (fun prop ->
                        match prop.Name with
                        | "names"
                        | "packagePaths"
                        | "modulePaths"
                        | "fqNames" -> None
                        | other -> Some $"context.{other}"
                    )
                else
                    None

    let internal readEnvelopeRootAndInspect orderPolicy limits file (root: JsonElement) =
        let unrepresentable = findUnrepresentableEnvelopeMember root

        readEnvelopeRoot orderPolicy limits file root
        |> Result.map (fun parsed -> parsed, unrepresentable)

    let internal readEnvelopeWithOrderAndInspect
        orderPolicy
        (limits: ContextLimits)
        (file: string)
        (bytes: ReadOnlyMemory<byte>)
        : Result<MorphirFile<JsonElement> * string option, StoreError list> =
        try
            use document =
                JsonDocument.Parse(
                    withoutByteOrderMark bytes,
                    JsonDocumentOptions(MaxDepth = limits.MaxDepth)
                )

            readEnvelopeRootAndInspect orderPolicy limits file document.RootElement
        with :? JsonException as problem ->
            if isJsonAtAnyDepth (withoutByteOrderMark bytes) then
                Error [
                    error
                        "depth_limit_exceeded"
                        file
                        $"The file is nested more than {limits.MaxDepth} levels deep."
                ]
            else
                Error [ error "invalid_json" file problem.Message ]

    let internal readEnvelopeWithOrder
        orderPolicy
        (limits: ContextLimits)
        (file: string)
        (bytes: ReadOnlyMemory<byte>)
        : Result<MorphirFile<JsonElement>, StoreError list> =
        readEnvelopeWithOrderAndInspect orderPolicy limits file bytes
        |> Result.map fst

    /// <summary>
    /// Reads a Morphir file's bytes, named <paramref name="file"/> in every diagnostic: a plain file has no schema and
    /// no context, and an envelope has its declared schema, its checked context and its document, not yet expanded. A
    /// UTF-8 byte order mark is skipped. C# calls this <c>ReadEnvelope</c>.
    /// </summary>
    [<CompiledName("ReadEnvelope")>]
    let readEnvelope limits file bytes =
        readEnvelopeWithOrder ContextOrder.WholeFile limits file bytes

    /// <summary>
    /// The plain document of a file: a plain file's document as it is, or a compacted file's document with each
    /// reference at a typed name position of <paramref name="family"/> replaced by its spelling. Diagnostics name
    /// <paramref name="file"/>. An expanded document nested deeper than <c>limits.MaxDepth</c> is refused with
    /// <c>depth_limit_exceeded</c>. C# calls this <c>Expand</c>.
    /// </summary>
    [<CompiledName("Expand")>]
    let expand
        (limits: ContextLimits)
        (family: IrFamily)
        (file: string)
        (morphirFile: MorphirFile<JsonElement>)
        : Result<JsonElement, StoreError list> =
        match morphirFile.Context with
        | None -> Ok morphirFile.Document
        | Some context -> ContextExpansion.expand limits family file context morphirFile.Document

    /// <summary>
    /// The plain document of a Morphir file's bytes: <see cref="readEnvelope"/>, then <see cref="expand"/> in the IR
    /// family of the enclosed document (<see cref="familyOf"/>). Diagnostics name <paramref name="file"/>. C# calls
    /// this <c>ReadPlainDocument</c>.
    /// </summary>
    [<CompiledName("ReadPlainDocument")>]
    let readPlainDocument
        (limits: ContextLimits)
        (file: string)
        (bytes: ReadOnlyMemory<byte>)
        : Result<JsonElement, StoreError list> =
        readEnvelope limits file bytes
        |> Result.bind (fun morphirFile -> expand limits (familyOf morphirFile.Document) file morphirFile)

    /// <summary>
    /// For C# callers: <see cref="readPlainDocument"/>, as true with the plain document in <paramref name="document"/>,
    /// or as false with each refusal in <paramref name="errors"/>, in the order the reader found them. C# calls this
    /// <c>TryReadPlainDocument</c>.
    /// </summary>
    [<CompiledName("TryReadPlainDocument")>]
    let tryReadPlainDocument
        (
            limits: ContextLimits,
            file: string,
            bytes: ReadOnlyMemory<byte>,
            [<Out>] document: byref<JsonElement>,
            [<Out; MaybeNullWhen(true)>] errors: byref<IReadOnlyList<StoreError>>
        ) : bool =
        match readPlainDocument limits file bytes with
        | Ok plain ->
            document <- plain
            errors <- Unchecked.defaultof<_>
            true
        | Error found ->
            document <- Unchecked.defaultof<_>
            errors <- List.toArray found
            false
