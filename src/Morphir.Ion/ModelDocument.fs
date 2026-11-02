namespace Morphir.Ion

open System
open System.Globalization
open System.Text.Json
open Morphir.IR.V4
open Morphir.Json.Classic
open Morphir.Json.V4

/// <summary>A member of a <c>morphir_model</c> document that a tool added for its own use.</summary>
type Enrichment = {
    /// <summary>The annotations on the enrichment struct itself, in order.</summary>
    Annotations: IonSymbol list
    /// <summary>The namespaced name of the tool that owns the data, such as <c>urn:vendor:tool</c>.</summary>
    Owner: string
    /// <summary>The data, which only the owner can interpret. Annotations and repeated field names are kept.</summary>
    Data: IonValue
}

/// <summary>A MEP <c>morphir_model</c> document. Reading keeps the exact bytes and the Ion values, and decodes the IR only on request.</summary>
type ModelDocument = {
    /// <summary>A copy of the bytes that were read, unchanged. Later changes to the caller's array do not reach it.</summary>
    Bytes: ReadOnlyMemory<byte>
    /// <summary>The <c>ir</c> member as Ion.</summary>
    Ir: IonValue
    /// <summary>The <c>enrichments</c> list, in order.</summary>
    Enrichments: Enrichment list
    /// <summary>The <c>provenance</c> list, in order.</summary>
    Provenance: IonValue list
}

/// <summary>Why a <c>morphir_model</c> document could not be read.</summary>
[<RequireQualifiedAccess>]
type ModelReadError =
    /// <summary>The bytes are not readable Ion, or exceed a limit.</summary>
    | Read of IonReadError
    /// <summary>The bytes are not a binary Ion payload with one top-level value. Code <c>invalid_model_payload</c>.</summary>
    | InvalidPayload of message: string
    /// <summary>The value is not a well-formed <c>morphir_model</c>. Code <c>invalid_model_document</c>.</summary>
    | InvalidDocument of message: string

/// <summary>Functions for working with model read errors.</summary>
module ModelReadError =
    /// <summary>The stable code for an error, using the MEP reference corpus codes where they exist.</summary>
    let code error =
        match error with
        | ModelReadError.Read inner -> IonReadError.code inner
        | ModelReadError.InvalidPayload _ -> "invalid_model_payload"
        | ModelReadError.InvalidDocument _ -> "invalid_model_document"

    /// <summary>A readable description of an error.</summary>
    let describe error =
        match error with
        | ModelReadError.Read inner -> IonReadError.describe inner
        | ModelReadError.InvalidPayload message -> message
        | ModelReadError.InvalidDocument message -> message

/// <summary>Reads the MEP <c>morphir_model</c> document.</summary>
module ModelDocument =
    let private binaryMarker = [| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy |]

    /// A copy the document owns, which is also what the reader reads, so the two cannot differ.
    let private owned (bytes: ReadOnlyMemory<byte>) : ReadOnlyMemory<byte> =
        ReadOnlyMemory<byte>(bytes.ToArray())

    let private isBinary (bytes: ReadOnlyMemory<byte>) =
        bytes.Length
        >= 4
        && bytes.Span.Slice(0, 4).SequenceEqual(ReadOnlySpan<byte> binaryMarker)

    let private invalid message =
        Result.Error(ModelReadError.InvalidDocument message)

    let private named name (members: (IonSymbol * IonValue) list) =
        members
        |> List.filter (fun (key, _) -> key = IonSymbol.Text name)
        |> List.map snd

    /// The one member with a name, or the reason there is not exactly one.
    let private exactlyOne where name members =
        match named name members with
        | [ value ] -> Result.Ok value
        | [] -> invalid $"{where} has no '{name}' member."
        | _ -> invalid $"{where} has more than one '{name}' member."

    let private enrichment index (value: IonValue) =
        let where = $"Enrichment {index}"

        match value.Data with
        | IonData.Struct members ->
            exactlyOne where "owner" members
            |> Result.bind (fun owner ->
                match owner.Data with
                | IonData.String text when
                    text.Length > 0
                    && text.Contains ':'
                    ->
                    Result.Ok text
                | _ ->
                    invalid $"{where} needs a nonempty namespaced string 'owner', such as a URN."
            )
            |> Result.bind (fun owner ->
                exactlyOne where "data" members
                |> Result.map (fun data -> {
                    Enrichment.Annotations = value.Annotations
                    Owner = owner
                    Data = data
                })
            )
        | _ -> invalid $"{where} is not a struct."

    let private listOf name (value: IonValue) =
        match value.Data with
        | IonData.List items -> Result.Ok items
        | _ -> invalid $"The '{name}' member is not a list."

    let private collect (results: Result<'a, ModelReadError> list) =
        List.foldBack
            (fun result state ->
                match result, state with
                | Result.Error error, _ -> Result.Error error
                | _, Result.Error error -> Result.Error error
                | Result.Ok value, Result.Ok values ->
                    Result.Ok(
                        value
                        :: values
                    )
            )
            results
            (Result.Ok [])

    let private shape (bytes: ReadOnlyMemory<byte>) (top: IonValue) =
        match top.Annotations, top.Data with
        | [ IonSymbol.Text "morphir_model" ], IonData.Struct members ->
            exactlyOne "The document" "ir" members
            |> Result.bind (fun ir ->
                exactlyOne "The document" "enrichments" members
                |> Result.bind (listOf "enrichments")
                |> Result.bind (fun items ->
                    items
                    |> List.mapi enrichment
                    |> collect
                )
                |> Result.bind (fun enrichments ->
                    exactlyOne "The document" "provenance" members
                    |> Result.bind (listOf "provenance")
                    |> Result.map (fun provenance -> {
                        Bytes = bytes
                        Ir = ir
                        Enrichments = enrichments
                        Provenance = provenance
                    })
                )
            )
        | _, IonData.Struct _ ->
            invalid "The top-level struct is not annotated 'morphir_model' alone."
        | _ -> invalid "The top-level value is not a struct annotated 'morphir_model'."

    let private single (notOne: string -> ModelReadError) bytes values =
        match values with
        | [ top ] -> shape bytes top
        | [] -> Result.Error(notOne "The input holds no value.")
        | _ -> Result.Error(notOne "The input holds more than one top-level value.")

    /// <summary>Reads a decoded <c>ion-binary</c> payload: Ion 1.0 binary with exactly one top-level value.</summary>
    /// <remarks>Base64, length and digest checks are the caller's. This reads the bytes they produce.</remarks>
    let readPayload
        (limits: IonReadLimits)
        (bytes: ReadOnlyMemory<byte>)
        : Result<ModelDocument, ModelReadError> =
        let bytes = owned bytes

        if not (isBinary bytes) then
            Result.Error(
                ModelReadError.InvalidPayload
                    "The payload does not start with the Ion 1.0 binary marker E0 01 00 EA."
            )
        else
            match IonReader.read limits bytes with
            | Result.Error error -> Result.Error(ModelReadError.Read error)
            | Result.Ok values -> single ModelReadError.InvalidPayload bytes values

    /// <summary>Reads a local file, in Ion text or binary, holding one <c>morphir_model</c>.</summary>
    let readFile
        (limits: IonReadLimits)
        (bytes: ReadOnlyMemory<byte>)
        : Result<ModelDocument, ModelReadError> =
        let bytes = owned bytes

        match IonReader.read limits bytes with
        | Result.Error error -> Result.Error(ModelReadError.Read error)
        | Result.Ok values -> single ModelReadError.InvalidDocument bytes values

    let private irError code message path location : Result<'a, IonIrError> =
        Result.Error
            {
                Code = code
                Message = message
                Path = path
                Location = location
            }

    /// The release string the ir value declares, with an integer N meaning N.0.0.
    let private declaredVersion (ir: IonValue) : Result<string, IonIrError> =
        match ir.Data with
        | IonData.Struct members ->
            match named "formatVersion" members with
            | [] ->
                irError
                    "missing_format_version"
                    "The ir value states no formatVersion."
                    (Some "$")
                    None
            | [ version ] ->
                match version.Data with
                | IonData.Int number when number.Sign > 0 ->
                    Result.Ok(
                        number.ToString(CultureInfo.InvariantCulture)
                        + ".0.0"
                    )
                | IonData.String text -> Result.Ok text
                | _ ->
                    irError
                        "invalid_format_version_type"
                        "The formatVersion is neither a positive integer nor a string."
                        (Some "$.formatVersion")
                        None
            | _ ->
                irError
                    "ir_not_json_compatible"
                    "The ir value has more than one formatVersion member."
                    (Some "$.formatVersion")
                    None
        | _ ->
            irError
                "missing_format_version"
                "The ir value is not a struct, so it states no formatVersion."
                (Some "$")
                None

    let private isV4 (version: string) =
        version = "4"
        || version.StartsWith("4.", StringComparison.Ordinal)

    let private parse (bytes: byte array) : Result<JsonDocument, IonIrError> =
        try
            // A JSON document is limited to 64 levels unless raised. The readers apply their own depth limits.
            let options = JsonDocumentOptions(MaxDepth = 4096)
            Result.Ok(JsonDocument.Parse(ReadOnlyMemory<byte> bytes, options))
        with :? JsonException as error ->
            irError
                "ir_not_json_compatible"
                $"The projected ir value is not readable JSON: {error.Message}"
                (Some "$")
                None

    let private read (version: string) (root: JsonElement) : Result<IrDocument, IonIrError> =
        try
            if isV4 version then
                match V4Json.readDistribution root with
                | Result.Ok document ->
                    Result.Ok(V4Ir(document.FormatVersion, document.Distribution))
                | Result.Error error -> irError error.Rule error.Message None (Some error.Location)
            else
                match ClassicJson.readDistribution root with
                | Result.Ok document -> Result.Ok(ClassicIr(version, document.Distribution))
                | Result.Error error -> irError error.Rule error.Message None (Some error.Location)
        with
        | :? InsufficientExecutionStackException ->
            irError
                "depth_limit_exceeded"
                "The ir value is nested too deeply to read."
                None
                (Some "$")
        | :? OutOfMemoryException -> reraise ()
        // The JSON readers return errors for bad input. This keeps a defect in one from escaping as an exception.
        | error ->
            irError
                "ir_decode_failed"
                $"The JSON reader failed unexpectedly: {error.Message}"
                None
                (Some "$")

    /// <summary>Decodes the <c>ir</c> member through the JSON reader for its version.</summary>
    /// <remarks>
    /// The expected release, such as <c>"4.0.0"</c>, is compared with the declared formatVersion first. An integer
    /// <c>N</c> declares <c>N.0.0</c>. A failure here leaves the model as it was read.
    /// </remarks>
    let decodeIr (expectedVersion: string) (model: ModelDocument) : Result<IrDocument, IonIrError> =
        declaredVersion model.Ir
        |> Result.bind (fun declared ->
            if
                declared
                <> expectedVersion
            then
                irError
                    "ir_version_mismatch"
                    $"The ir value declares version {declared}, but {expectedVersion} was expected."
                    (Some "$.formatVersion")
                    None
            else
                IrProjection.toJsonBytes model.Ir
                |> Result.bind parse
                |> Result.bind (fun document ->
                    use document = document
                    read declared document.RootElement
                )
        )
