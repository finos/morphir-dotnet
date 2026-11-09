// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Generic
open System.Text
open System.Text.Json

/// Why a host refuses a guest's model-transport selection.
[<RequireQualifiedAccess>]
type ModelTransportFailure =
    /// The guest selected a tuple that the host did not offer.
    | TupleNotOffered of tuple: ModelTransportTuple
    /// The guest selected the same tuple twice. Tuples are unique.
    | DuplicateTuple of tuple: ModelTransportTuple
    /// The guest selected this tuple after a tuple that the host offered later. A selection keeps host order.
    | TupleOutOfOrder of tuple: ModelTransportTuple
    /// The guest selected a limit above the host's own value. The limit is its wire name, such as maxDepth.
    | LimitAboveOffer of limit: string * selected: int * offered: int
    /// A limit is not positive. Values are positive and at most 2147483647.
    | InvalidLimit of limit: string * value: int

    /// Stable diagnostic text that names the tuple or the limit.
    override this.ToString() =
        match this with
        | TupleNotOffered tuple -> $"The selected model-transport tuple ({tuple}) was not offered."
        | DuplicateTuple tuple -> $"The model-transport tuple ({tuple}) is selected more than once."
        | TupleOutOfOrder tuple -> $"The selected model-transport tuple ({tuple}) is not in host offer order."
        | LimitAboveOffer(limit, selected, offered) ->
            $"The selected model-transport limit {limit} ({selected}) is above the offered value ({offered})."
        | InvalidLimit(limit, value) -> $"The model-transport limit {limit} ({value}) is not positive."

    /// An error diagnostic that names the extension and the refused tuple or limit. It has no code, because the
    /// pinned contract defines none for this refusal.
    member this.ToDiagnostic(extensionId: string) : Diagnostic =
        { Severity = DiagnosticSeverity.Error
          Code = None
          Message = $"Extension {extensionId}: {this}"
          Location = None
          Related = [||]
          Data = None }

/// An operation that has no agreed model-transport tuple. The host sends no request for it.
type TupleUnavailable =
    {
        /// The operation that cannot run.
        Operation: ModelOperation
    }

    /// The diagnostic code that the contract names: extension.tuple_unavailable.
    member _.Code = "extension.tuple_unavailable"

    /// An error diagnostic with the code extension.tuple_unavailable that names the extension and the operation.
    member this.ToDiagnostic(extensionId: string) : Diagnostic =
        { Severity = DiagnosticSeverity.Error
          Code = Some this.Code
          Message = $"Extension {extensionId} agreed no model-transport tuple for {this.Operation}."
          Location = None
          Related = [||]
          Data = None }

    /// Stable diagnostic text.
    override this.ToString() =
        $"No agreed model-transport tuple for {this.Operation}."

/// Why an envelope cannot be made. The contract requires nonempty identities.
[<RequireQualifiedAccess>]
type EnvelopeFailure =
    /// The model id is empty.
    | EmptyModelId
    /// The revision is empty.
    | EmptyRevision
    /// The tuple profile is empty.
    | EmptyProfile

    /// Stable diagnostic text.
    override this.ToString() =
        match this with
        | EmptyModelId -> "A model envelope needs a nonempty modelId."
        | EmptyRevision -> "A model envelope needs a nonempty revision."
        | EmptyProfile -> "A model envelope needs a nonempty profile."

/// Why a model request or envelope is refused. Each case has the code of the pinned model-transport cases.
/// Every failure gives -32602, except UnsupportedProtocol, which gives -32013.
[<RequireQualifiedAccess>]
type AdmissionFailure =
    /// The session's protocol version is not the exact draft that enables model operations.
    | UnsupportedProtocol of version: ProtocolVersion
    /// The tuple is not agreed in this session.
    | UnsupportedTuple of tuple: ModelTransportTuple
    /// The UTF-8 base64 content of the message is over the encoded limit.
    | EncodedLimitExceeded of encodedBytes: int64 * limit: int
    /// The content is not canonical padded RFC 4648 base64 without whitespace.
    | InvalidBase64
    /// The decoded payload bytes of the message are over the decoded limit.
    | DecodedLimitExceeded of decodedBytes: int64 * limit: int
    /// The declared decoded length is not the length of the decoded bytes.
    | LengthMismatch of declared: int * actual: int
    /// The declared SHA-256 is not the digest of the decoded bytes.
    | DigestMismatch of declared: Sha256Digest * actual: Sha256Digest
    /// The decoded bytes are not a document of the declared codec.
    | MalformedPayload of codec: ModelCodec * reason: string
    /// The decoded content is deeper than the depth limit.
    | DepthLimitExceeded of limit: int
    /// The decoded content of the message has more value nodes than the node limit.
    | NodeLimitExceeded of limit: int
    /// The envelope profile is not the profile of the tuple.
    | ProfileMismatch of expected: string * actual: string
    /// The payload codec is not the codec of the tuple.
    | CodecMismatch of expected: ModelCodec * actual: ModelCodec
    /// The envelope version is not the envelope version of the tuple.
    | EnvelopeVersionMismatch of expected: SemanticVersion * actual: SemanticVersion
    /// The IR version is not the IR version of the tuple.
    | IrVersionMismatch of expected: SemanticVersion * actual: SemanticVersion
    /// The envelope lists a critical path that this reader does not know.
    | UnknownCriticalPath of path: string
    /// An identity string of the envelope or its evidence is empty.
    | EmptyIdentity of memberPath: string
    /// A successful compilation result has no model.
    | MissingModel
    /// A failed compilation result has a model member, also when it is null.
    | ModelOnFailure
    /// A required guarantee is not one that this session can interpret.
    | UnsupportedGuarantee of guarantee: string
    /// No admitted evidence claims the required guarantee.
    | MissingEvidence of requirement: ModelGuaranteeRequirement
    /// The evidence for the required guarantee is for another model, revision or context.
    | StaleEvidence of requirement: ModelGuaranteeRequirement
    /// The evidence matches the model and context, but not the exact required scope.
    | WrongEvidenceScope of requirement: ModelGuaranteeRequirement

    /// The code of the pinned model-transport cases, such as digest_mismatch.
    member this.Code =
        match this with
        | UnsupportedProtocol _ -> "unsupported_protocol"
        | UnsupportedTuple _ -> "unsupported_tuple"
        | EncodedLimitExceeded _ -> "encoded_limit_exceeded"
        | InvalidBase64 -> "invalid_base64"
        | DecodedLimitExceeded _ -> "decoded_limit_exceeded"
        | LengthMismatch _ -> "length_mismatch"
        | DigestMismatch _ -> "digest_mismatch"
        | MalformedPayload _ -> "malformed_payload"
        | DepthLimitExceeded _ -> "depth_limit_exceeded"
        | NodeLimitExceeded _ -> "node_limit_exceeded"
        | ProfileMismatch _ -> "profile_mismatch"
        | CodecMismatch _ -> "codec_mismatch"
        | EnvelopeVersionMismatch _ -> "envelope_version_mismatch"
        | IrVersionMismatch _ -> "ir_version_mismatch"
        | UnknownCriticalPath _ -> "unknown_critical_path"
        | EmptyIdentity _ -> "empty_identity"
        | MissingModel -> "missing_model"
        | ModelOnFailure -> "model_on_failure"
        | UnsupportedGuarantee _ -> "unsupported_guarantee"
        | MissingEvidence _ -> "missing_evidence"
        | StaleEvidence _ -> "stale_evidence"
        | WrongEvidenceScope _ -> "wrong_evidence_scope"

    /// The JSON-RPC error code: -32602 (InvalidParams), or -32013 (CapabilityUnavailable) when the protocol
    /// version does not enable model operations.
    member this.ErrorCode =
        match this with
        | UnsupportedProtocol _ -> ErrorCode.CapabilityUnavailable
        | _ -> ErrorCode.InvalidParams

    /// The JSON-RPC error object, with the message "Model admission refused: " and the code, as the upstream
    /// guests send it. It has no data member.
    member this.ToRpcError() : RpcError =
        { Code = int this.ErrorCode
          Message = $"Model admission refused: {this.Code}"
          Data = None }

    /// Stable diagnostic text.
    override this.ToString() =
        match this with
        | UnsupportedProtocol version -> $"Protocol version {version} does not enable model operations."
        | UnsupportedTuple tuple -> $"The model-transport tuple ({tuple}) is not agreed in this session."
        | EncodedLimitExceeded(bytes, limit) -> $"The model content has {bytes} encoded bytes; the limit is {limit}."
        | InvalidBase64 -> "The model content is not canonical padded base64."
        | DecodedLimitExceeded(bytes, limit) -> $"The model payloads have {bytes} decoded bytes; the limit is {limit}."
        | LengthMismatch(declared, actual) -> $"The declared decodedByteLength {declared} is not the decoded length {actual}."
        | DigestMismatch(declared, actual) -> $"The declared sha256 {declared} is not the digest {actual} of the decoded bytes."
        | MalformedPayload(codec, reason) -> $"The {codec} payload is malformed: {reason}"
        | DepthLimitExceeded limit -> $"The model content is deeper than the depth limit {limit}."
        | NodeLimitExceeded limit -> $"The model content has more than {limit} value nodes."
        | ProfileMismatch(expected, actual) -> $"The profile {actual} is not the tuple profile {expected}."
        | CodecMismatch(expected, actual) -> $"The codec {actual} is not the tuple codec {expected}."
        | EnvelopeVersionMismatch(expected, actual) -> $"The envelope version {actual} is not the tuple envelope version {expected}."
        | IrVersionMismatch(expected, actual) -> $"The IR version {actual} is not the tuple IR version {expected}."
        | UnknownCriticalPath path -> $"The critical path {path} is not known to this reader."
        | EmptyIdentity path -> $"The member {path} must not be empty."
        | MissingModel -> "A successful compilation result has no model."
        | ModelOnFailure -> "A failed compilation result must not have a model member."
        | UnsupportedGuarantee guarantee -> $"The required guarantee {guarantee} is not one that this session can interpret."
        | MissingEvidence requirement -> $"No admitted evidence establishes {requirement}."
        | StaleEvidence requirement -> $"The evidence for {requirement} is for another model, revision or context."
        | WrongEvidenceScope requirement -> $"The evidence for {requirement} is for another scope."

/// What the admission scan found in decoded content. The facts decide which conversions lose information.
type PayloadFacts =
    {
        /// The deepest value; each document root has depth 1.
        Depth: int
        /// The number of value nodes. Annotations, field names, padding and Ion system values are not nodes.
        Nodes: int
        /// The content has Ion annotations on user values.
        HasAnnotations: bool
        /// The content has Ion integers outside the IEEE 754 safe range of ±(2^53 - 1).
        HasWideIntegers: bool
        /// The content has Ion decimals.
        HasDecimals: bool
        /// The content has Ion values that JSON has no type for: timestamps, symbols, blobs, clobs, s-expressions
        /// and typed nulls.
        HasIonOnlyValues: bool
    }

    /// Stable diagnostic text.
    override this.ToString() = $"Depth {this.Depth}, {this.Nodes} nodes"

/// Evidence whose payload bytes passed admission. It is a claim: establishing a guarantee needs more checks.
[<Sealed>]
type AdmittedEvidence internal (evidence: ModelGuaranteeEvidence, bytes: byte array, facts: PayloadFacts) =
    /// The evidence as it was received.
    member _.Evidence = evidence
    /// The verified evidence bytes.
    member _.Bytes = ReadOnlyMemory<byte> bytes
    /// What the scan found.
    member _.Facts = facts
    /// Stable diagnostic text.
    override _.ToString() = $"Admitted evidence {evidence}"

/// A model envelope that passed every admission check. Only admission makes it, so only verified bytes reach an
/// IR reader. It does not decode the IR.
[<Sealed>]
type AdmittedModel
    internal
    (
        envelope: MorphirModel,
        tuple: ModelTransportTuple,
        bytes: byte array,
        facts: PayloadFacts,
        evidence: IReadOnlyList<AdmittedEvidence>
    ) =
    /// The envelope as it was received.
    member _.Envelope = envelope
    /// The negotiated tuple that admitted it.
    member _.Tuple = tuple
    /// The verified payload bytes, before profile parsing.
    member _.Bytes = ReadOnlyMemory<byte> bytes
    /// What the scan found in the payload.
    member _.Facts = facts
    /// The admitted evidence, in envelope order.
    member _.Evidence = evidence
    /// Stable diagnostic text.
    override _.ToString() = $"Admitted {envelope}"

/// A required guarantee and the admitted evidence that establishes it.
type EstablishedGuarantee =
    {
        /// The requirement.
        Requirement: ModelGuaranteeRequirement
        /// The evidence that matches it exactly.
        Evidence: AdmittedEvidence
    }

    /// Stable diagnostic text.
    override this.ToString() = $"Established {this.Requirement}"

/// The model member of a compile result, as the wire has it: absent, null or present.
[<RequireQualifiedAccess>]
type ModelMember =
    /// The result has no model member.
    | Absent
    /// The model member is JSON null.
    | Null
    /// The model member is an envelope.
    | Present of model: MorphirModel

    /// Stable diagnostic text.
    override this.ToString() =
        match this with
        | Absent -> "absent"
        | Null -> "null"
        | Present model -> model.ToString()

/// Information that a conversion to another tuple cannot keep.
[<RequireQualifiedAccess>]
type ConversionLoss =
    /// Ion annotations, such as ordered source_name::target_name:: wrappers.
    | Annotations
    /// Integers outside the IEEE 754 safe range.
    | IntegerPrecision
    /// Decimal precision, such as 123.4500.
    | DecimalPrecision
    /// Ion types that JSON does not have.
    | IonOnlyValues
    /// Content of another profile, such as enrichments and provenance of morphir-model.
    | ProfileContent

    /// Stable diagnostic text.
    override this.ToString() =
        match this with
        | Annotations -> "annotations"
        | IntegerPrecision -> "integer precision"
        | DecimalPrecision -> "decimal precision"
        | IonOnlyValues -> "Ion-only values"
        | ProfileContent -> "profile content"

/// How a model reaches an agreed tuple.
[<RequireQualifiedAccess>]
type ConversionPlan =
    /// The model is already in an agreed tuple.
    | Unchanged of tuple: ModelTransportTuple
    /// Convert to the target. The losses are empty for a lossless conversion; otherwise the user allowed them,
    /// and the host reports the conversion.
    | Convert of target: ModelTransportTuple * losses: IReadOnlyList<ConversionLoss>

    /// Stable diagnostic text.
    override this.ToString() =
        match this with
        | Unchanged tuple -> $"Unchanged ({tuple})"
        | Convert(target, losses) -> $"Convert to ({target}) with {losses.Count} losses"

/// Why a model cannot reach an agreed tuple.
[<RequireQualifiedAccess>]
type ConversionRefusal =
    /// No agreed tuple can be reached.
    | TupleUnavailable of unavailable: TupleUnavailable
    /// Each reachable tuple loses information, and the user did not allow loss. This is not tuple_unavailable.
    | LossyConversion of target: ModelTransportTuple * losses: IReadOnlyList<ConversionLoss>

    /// The code of the pinned cases: extension.tuple_unavailable or lossy_conversion.
    member this.Code =
        match this with
        | TupleUnavailable unavailable -> unavailable.Code
        | LossyConversion _ -> "lossy_conversion"

    /// An error diagnostic, with the code extension.tuple_unavailable or data.lossy_conversion.
    member this.ToDiagnostic() : Diagnostic =
        let code =
            match this with
            | TupleUnavailable unavailable -> unavailable.Code
            | LossyConversion _ -> "data.lossy_conversion"

        { Severity = DiagnosticSeverity.Error
          Code = Some code
          Message = this.ToString()
          Location = None
          Related = [||]
          Data = None }

    /// Stable diagnostic text.
    override this.ToString() =
        match this with
        | TupleUnavailable unavailable -> unavailable.ToString()
        | LossyConversion(target, losses) ->
            let names = losses |> Seq.map _.ToString() |> String.concat ", "
            $"Converting the model to ({target}) loses {names}."

/// Canonical padded RFC 4648 base64, checked before any byte is decoded.
module internal Base64 =
    /// The number of padding characters, or -1 when the length is not a multiple of four.
    let padding (content: string) =
        if content.Length % 4 <> 0 then -1
        elif content.EndsWith("==", StringComparison.Ordinal) then 2
        elif content.EndsWith('=') then 1
        else 0

    /// The decoded length of content whose length is a multiple of four.
    let decodedLength (content: string) =
        int64 (content.Length / 4) * 3L - int64 (padding content)

    /// The decoded bytes when the content is canonical: it decodes, and encoding the bytes again gives the same
    /// text. This refuses whitespace, the URL alphabet, misplaced padding and non-zero unused bits.
    let decodeCanonical (content: string) : byte array option =
        if padding content < 0 then
            None
        else
            let buffer = Array.zeroCreate<byte> (int (decodedLength content))
            let mutable written = 0

            if
                Convert.TryFromBase64String(content, Span buffer, &written)
                && written = buffer.Length
                && String.Equals(Convert.ToBase64String(ReadOnlySpan(buffer, 0, written)), content, StringComparison.Ordinal)
            then
                Some buffer
            else
                None

/// Bounded structural scans of decoded content. They do not build values and never recurse.
module internal PayloadScan =
    type Outcome =
        | Scanned of PayloadFacts
        | Malformed of string
        | TooDeep
        | TooManyNodes

    let private empty =
        { Depth = 0
          Nodes = 0
          HasAnnotations = false
          HasWideIntegers = false
          HasDecimals = false
          HasIonOnlyValues = false }

    let private scanJson (bytes: byte array) (maxDepth: int) (nodeBudget: int64) : Outcome =
        let options =
            JsonReaderOptions(
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                MaxDepth = (if maxDepth >= Int32.MaxValue - 1 then Int32.MaxValue else maxDepth + 1)
            )

        let mutable reader = Utf8JsonReader(ReadOnlySpan bytes, options)
        let mutable nodes = 0L
        let mutable deepest = 0
        let mutable outcome = ValueNone

        while outcome.IsNone && reader.Read() do
            match reader.TokenType with
            | JsonTokenType.StartObject
            | JsonTokenType.StartArray
            | JsonTokenType.String
            | JsonTokenType.Number
            | JsonTokenType.True
            | JsonTokenType.False
            | JsonTokenType.Null ->
                let depth = reader.CurrentDepth + 1
                nodes <- nodes + 1L
                deepest <- max deepest depth

                if depth > maxDepth then outcome <- ValueSome TooDeep
                elif nodes > nodeBudget then outcome <- ValueSome TooManyNodes
            | _ -> ()

        match outcome with
        | ValueSome failure -> failure
        | ValueNone when nodes = 0L -> Malformed "The document is empty."
        | ValueNone -> Scanned { empty with Depth = deepest; Nodes = int nodes }

    /// Scans one JSON document. Values count as nodes; member names do not.
    let json (bytes: byte array) (maxDepth: int) (nodeBudget: int64) : Outcome =
        try
            scanJson bytes maxDepth nodeBudget
        with :? JsonException as error ->
            Malformed error.Message

    exception private IonMalformed of string

    let private fail reason = raise (IonMalformed reason)

    let private versionMarker = [| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy |]

    /// A read position in Ion binary bytes.
    type private Cursor(bytes: byte array) =
        member val Position = 0 with get, set

        member _.Byte(index: int) = bytes[index]

        member this.IsVersionMarker(limit: int) =
            this.Position + 4 <= limit
            && bytes.AsSpan(this.Position, 4).SequenceEqual(ReadOnlySpan versionMarker)

        /// Reads a VarUInt that must end before the limit.
        member this.VarUInt(limit: int) =
            let mutable value = 0L
            let mutable fin = false

            while not fin do
                if this.Position >= limit then
                    fail "A VarUInt runs past its container."

                let b = bytes[this.Position]
                this.Position <- this.Position + 1
                value <- (value <<< 7) ||| int64 (b &&& 0x7Fuy)

                if value > int64 Int32.MaxValue then
                    fail "A VarUInt is too large."

                fin <- b &&& 0x80uy <> 0uy

            int value

        /// The length of a type descriptor: its low nibble, or a VarUInt when the nibble is 14.
        member this.Length(limit: int, low: int) =
            if low = 14 then this.VarUInt limit else low

        /// True when an integer body is outside the IEEE 754 safe range of ±(2^53 - 1).
        member _.IsWide(start: int, length: int) =
            let span = ReadOnlySpan(bytes, start, length)
            let first = span.IndexOfAnyExcept 0uy

            first >= 0
            && (let significant = length - first
                significant > 7 || (significant = 7 && span[first] >= 0x20uy))

    /// An open Ion container: where it ends, whether its children have field names, and whether it is a
    /// system value whose children are not counted.
    [<Struct>]
    type private Frame =
        { End: int
          IsStruct: bool
          IsSystem: bool }

    let private scanIon (bytes: byte array) (maxDepth: int) (nodeBudget: int64) : Outcome =
        let cursor = Cursor bytes
        let stack = Stack<Frame>()
        let mutable nodes = 0L
        let mutable facts = empty
        let mutable outcome = ValueNone

        while outcome.IsNone && (cursor.Position < bytes.Length || stack.Count > 0) do
            if stack.Count > 0 && cursor.Position = stack.Peek().End then
                stack.Pop() |> ignore
            elif stack.Count > 0 && cursor.Position > stack.Peek().End then
                fail "A value runs past its container."
            elif stack.Count = 0 && cursor.IsVersionMarker bytes.Length then
                cursor.Position <- cursor.Position + 4
            else
                let top = stack.Count = 0
                let limit = if top then bytes.Length else stack.Peek().End
                let parentSystem = not top && stack.Peek().IsSystem

                if not top && stack.Peek().IsStruct then
                    cursor.VarUInt limit |> ignore

                if cursor.Position >= limit then
                    fail "A struct field has no value."

                // An annotation wrapper: its length, the annotation symbols, then exactly one value.
                let mutable annotated = false
                let mutable firstAnnotation = -1
                let mutable wrapperEnd = -1
                let wrapper = int (cursor.Byte cursor.Position)

                if wrapper >>> 4 = 0xE then
                    let low = wrapper &&& 0x0F

                    if low < 3 || low = 15 then
                        fail "An annotation wrapper has an invalid length."

                    cursor.Position <- cursor.Position + 1
                    let size = cursor.Length(limit, low)

                    // Compare with the space left before adding, so that a large length cannot wrap the offset.
                    if size > limit - cursor.Position then
                        fail "An annotation wrapper runs past its container."

                    wrapperEnd <- cursor.Position + size

                    let annotationsLength = cursor.VarUInt wrapperEnd

                    if annotationsLength < 1 || annotationsLength >= wrapperEnd - cursor.Position then
                        fail "An annotation wrapper has no annotations or no value."

                    let annotationsEnd = cursor.Position + annotationsLength

                    firstAnnotation <- cursor.VarUInt annotationsEnd

                    while cursor.Position < annotationsEnd do
                        cursor.VarUInt annotationsEnd |> ignore

                    annotated <- true

                let valueLimit = if annotated then wrapperEnd else limit
                let descriptor = int (cursor.Byte cursor.Position)
                let kind = descriptor >>> 4
                let low = descriptor &&& 0x0F
                cursor.Position <- cursor.Position + 1
                let isNull = low = 15
                let isPad = kind = 0 && not isNull

                if annotated && (kind = 0xE || isPad) then
                    fail "An annotation wrapper holds another wrapper or padding."

                if kind = 0xE then
                    fail "An annotation wrapper or version marker is out of place."

                if kind = 0xF then
                    fail "A value has the reserved type 15."

                let bodyLength =
                    if isNull then 0
                    elif kind = 1 then (if low > 1 then fail "A bool has an invalid representation." else 0)
                    elif kind = 4 && low <> 0 && low <> 4 && low <> 8 then fail "A float has an invalid length."
                    elif kind = 0xD && low = 1 then
                        let size = cursor.VarUInt valueLimit

                        if size = 0 then
                            fail "An ordered struct is empty."

                        size
                    else
                        cursor.Length(valueLimit, low)

                let valueStart = cursor.Position

                // Compare with the space left before adding, so that a large length cannot wrap the offset.
                if bodyLength > valueLimit - valueStart then
                    fail "A value runs past its container."

                let valueEnd = valueStart + bodyLength

                if annotated && valueEnd <> wrapperEnd then
                    fail "An annotation wrapper length does not match its value."

                let isSystem =
                    parentSystem || (top && annotated && firstAnnotation = 3 && kind = 0xD && not isNull)

                if not isPad && not isSystem then
                    let depth = stack.Count + 1
                    nodes <- nodes + 1L

                    let wide = (kind = 2 || kind = 3) && not isNull && cursor.IsWide(valueStart, bodyLength)

                    let ionOnly =
                        (isNull && kind <> 0)
                        || (not isNull && (kind = 6 || kind = 7 || kind = 9 || kind = 0xA || kind = 0xC))

                    facts <-
                        { facts with
                            Depth = max facts.Depth depth
                            HasAnnotations = facts.HasAnnotations || annotated
                            HasWideIntegers = facts.HasWideIntegers || wide
                            HasDecimals = facts.HasDecimals || (kind = 5 && not isNull)
                            HasIonOnlyValues = facts.HasIonOnlyValues || ionOnly }

                    if depth > maxDepth then outcome <- ValueSome TooDeep
                    elif nodes > nodeBudget then outcome <- ValueSome TooManyNodes

                if not isPad && not isNull && (kind = 0xB || kind = 0xC || kind = 0xD) then
                    stack.Push
                        { End = valueEnd
                          IsStruct = kind = 0xD
                          IsSystem = isSystem }
                else
                    cursor.Position <- valueEnd

        match outcome with
        | ValueSome failure -> failure
        | ValueNone -> Scanned { facts with Nodes = int nodes }

    /// Scans an Ion 1.0 binary stream. A top-level struct annotated $ion_symbol_table (symbol 3) is a system
    /// value: its structure is checked, but it is not profile content, so it adds no depth and no nodes.
    let ion (bytes: byte array) (maxDepth: int) (nodeBudget: int64) : Outcome =
        if bytes.Length < 4 || not (bytes.AsSpan(0, 4).SequenceEqual(ReadOnlySpan versionMarker)) then
            Malformed "The payload does not start with the Ion 1.0 binary version marker."
        else
            try
                scanIon bytes maxDepth nodeBudget
            with IonMalformed reason ->
                Malformed reason

/// Model-transport negotiation, envelope creation and envelope admission (design MT-1). Every function is pure.
[<RequireQualifiedAccess>]
module ModelTransport =
    /// The exact MEP draft that enables model operations.
    [<CompiledName "ModelProtocol">]
    let modelProtocol = ProtocolVersion.Create "0.2.0-draft.1"

    /// The member paths of the envelope that this reader knows. A known parent never admits an unknown child.
    [<CompiledName "KnownCriticalPaths">]
    let knownCriticalPaths: IReadOnlyList<string> =
        [| "envelopeVersion"
           "modelId"
           "revision"
           "irVersion"
           "profile"
           "payload"
           "payload.codec"
           "payload.layout"
           "payload.decodedByteLength"
           "payload.sha256"
           "payload.content"
           "evidence"
           "critical" |]

    let private limitValues (limits: ModelTransportLimits) =
        [ "maxEncodedBytes", limits.MaxEncodedBytes
          "maxDecodedBytes", limits.MaxDecodedBytes
          "maxDepth", limits.MaxDepth
          "maxNodes", limits.MaxNodes ]

    let private containsOrdinal (values: IReadOnlyList<string>) (value: string) =
        values |> Seq.exists (fun item -> String.Equals(item, value, StringComparison.Ordinal))

    /// Fails unless the protocol version is exactly 0.2.0-draft.1. Legacy 0.1 never enables model operations.
    [<CompiledName "RequireModelProtocol">]
    let requireModelProtocol (version: ProtocolVersion) : Result<unit, AdmissionFailure> =
        if version.Equals modelProtocol then
            Ok()
        else
            Error(AdmissionFailure.UnsupportedProtocol version)

    /// The model operation of a method, or None for every other method.
    [<CompiledName "OperationOf">]
    let operationOf (method: Method) : ModelOperation option =
        match method with
        | Method.CompileModel -> Some ModelOperation.CompileModel
        | Method.GenerateModel -> Some ModelOperation.GenerateModel
        | _ -> None

    /// The guest side: the offered tuples that the guest supports, in host order and by exact equality; each
    /// limit at the smaller of the two values; and the offered guarantees that the guest can interpret, in host
    /// order.
    [<CompiledName "Select">]
    let select (offer: ModelTransportSupport) (support: ModelTransportSupport) : ModelTransportSupport =
        { Tuples = offer.Tuples |> Seq.distinct |> Seq.filter (fun tuple -> Seq.contains tuple support.Tuples) |> Array.ofSeq
          Limits =
            { MaxEncodedBytes = min offer.Limits.MaxEncodedBytes support.Limits.MaxEncodedBytes
              MaxDecodedBytes = min offer.Limits.MaxDecodedBytes support.Limits.MaxDecodedBytes
              MaxDepth = min offer.Limits.MaxDepth support.Limits.MaxDepth
              MaxNodes = min offer.Limits.MaxNodes support.Limits.MaxNodes }
          Guarantees =
            offer.Guarantees
            |> Seq.distinct
            |> Seq.filter (containsOrdinal support.Guarantees)
            |> Array.ofSeq }

    /// The host side: refuses a selection with a tuple that the host did not offer, a duplicate tuple, a tuple out
    /// of host offer order, or a limit that is above the host's own or not positive. The agreement has the
    /// selected tuples in host order, the selected limits, and the guarantees that both peers list, in host order.
    [<CompiledName "VerifySelection">]
    let verifySelection
        (offer: ModelTransportSupport)
        (selection: ModelTransportSupport)
        : Result<ModelTransportSupport, ModelTransportFailure> =
        let seen = HashSet<ModelTransportTuple>()
        let mutable lastIndex = -1

        let tupleFailure =
            selection.Tuples
            |> Seq.tryPick (fun tuple ->
                let index = offer.Tuples |> Seq.tryFindIndex (fun offered -> offered = tuple)

                match index with
                | None -> Some(ModelTransportFailure.TupleNotOffered tuple)
                | Some _ when not (seen.Add tuple) -> Some(ModelTransportFailure.DuplicateTuple tuple)
                | Some index when index < lastIndex -> Some(ModelTransportFailure.TupleOutOfOrder tuple)
                | Some index ->
                    lastIndex <- index
                    None)

        let limitFailure () =
            List.zip (limitValues selection.Limits) (limitValues offer.Limits)
            |> List.tryPick (fun ((name, selected), (_, offered)) ->
                if selected <= 0 then
                    Some(ModelTransportFailure.InvalidLimit(name, selected))
                elif selected > offered then
                    Some(ModelTransportFailure.LimitAboveOffer(name, selected, offered))
                else
                    None)

        match tupleFailure |> Option.orElseWith limitFailure with
        | Some failure -> Error failure
        | None ->
            Ok
                { Tuples = offer.Tuples |> Seq.distinct |> Seq.filter (fun tuple -> seen.Contains tuple) |> Array.ofSeq
                  Limits = selection.Limits
                  Guarantees =
                    offer.Guarantees
                    |> Seq.distinct
                    |> Seq.filter (containsOrdinal selection.Guarantees)
                    |> Array.ofSeq }

    /// The agreed tuples of an operation, in host order. None means that the session agreed no model transport.
    [<CompiledName "AgreedTuples">]
    let agreedTuples (agreed: ModelTransportSupport option) (operation: ModelOperation) : IReadOnlyList<ModelTransportTuple> =
        match agreed with
        | None -> [||]
        | Some support -> support.Tuples |> Seq.filter (fun tuple -> tuple.Operation = operation) |> Array.ofSeq :> _

    /// The first agreed tuple of an operation in host order, or extension.tuple_unavailable when there is none.
    [<CompiledName "FirstTuple">]
    let firstTuple (agreed: ModelTransportSupport option) (operation: ModelOperation) : Result<ModelTransportTuple, TupleUnavailable> =
        match Seq.tryHead (agreedTuples agreed operation) with
        | Some tuple -> Ok tuple
        | None -> Error { Operation = operation }

    /// Checks that a request names an agreed tuple.
    [<CompiledName "CheckTuple">]
    let checkTuple (agreed: ModelTransportSupport option) (tuple: ModelTransportTuple) : Result<ModelTransportTuple, AdmissionFailure> =
        if Seq.contains tuple (agreedTuples agreed tuple.Operation) then
            Ok tuple
        else
            Error(AdmissionFailure.UnsupportedTuple tuple)

    /// Checks the output tuple of a compileModel request, and that options.irVersion equals its exact IR version.
    [<CompiledName "CheckCompileOutput">]
    let checkCompileOutput
        (agreed: ModelTransportSupport option)
        (output: ModelTransportTuple)
        (optionsIrVersion: SemanticVersion)
        : Result<ModelTransportTuple, AdmissionFailure> =
        if output.Operation <> ModelOperation.CompileModel then
            Error(AdmissionFailure.UnsupportedTuple output)
        else
            checkTuple agreed output
            |> Result.bind (fun output ->
                if output.IrVersion.Equals optionsIrVersion then
                    Ok output
                else
                    Error(AdmissionFailure.IrVersionMismatch(output.IrVersion, optionsIrVersion)))

    /// The tuple that an envelope is in, for an operation.
    [<CompiledName "TupleOf">]
    let tupleOf (operation: ModelOperation) (model: MorphirModel) : ModelTransportTuple =
        { Operation = operation
          Profile = model.Profile
          Codec = model.Payload.Codec
          EnvelopeVersion = model.EnvelopeVersion
          IrVersion = model.IrVersion }

    /// A deterministic revision derived from a payload digest: sha256: and the 64 digits.
    [<CompiledName "DeriveRevision">]
    let deriveRevision (digest: Sha256Digest) : string = "sha256:" + digest.Value

    let private payloadOf (codec: ModelCodec) (bytes: ReadOnlyMemory<byte>) : ModelPayload =
        { Codec = codec
          Layout = ModelLayout.SingleDocument
          DecodedByteLength = bytes.Length
          Sha256 = Sha256Digest.Compute bytes.Span
          Content = Convert.ToBase64String bytes.Span }

    /// Makes an envelope in a tuple: canonical padded base64, the exact decoded length and the lowercase SHA-256
    /// of the bytes, no evidence and no critical paths.
    [<CompiledName "CreateEnvelope">]
    let createEnvelope
        (tuple: ModelTransportTuple)
        (modelId: string)
        (revision: string)
        (bytes: ReadOnlyMemory<byte>)
        : Result<MorphirModel, EnvelopeFailure> =
        if String.IsNullOrEmpty modelId then
            Error EnvelopeFailure.EmptyModelId
        elif String.IsNullOrEmpty revision then
            Error EnvelopeFailure.EmptyRevision
        elif String.IsNullOrEmpty tuple.Profile then
            Error EnvelopeFailure.EmptyProfile
        else
            Ok
                { EnvelopeVersion = tuple.EnvelopeVersion
                  ModelId = modelId
                  Revision = revision
                  IrVersion = tuple.IrVersion
                  Profile = tuple.Profile
                  Payload = payloadOf tuple.Codec bytes
                  Evidence = [||]
                  Critical = [||] }

    /// Makes an envelope whose revision is derived from the payload digest, as for a converted model.
    [<CompiledName "CreateDerivedEnvelope">]
    let createDerivedEnvelope
        (tuple: ModelTransportTuple)
        (modelId: string)
        (bytes: ReadOnlyMemory<byte>)
        : Result<MorphirModel, EnvelopeFailure> =
        createEnvelope tuple modelId (deriveRevision (Sha256Digest.Compute bytes.Span)) bytes

    /// Runs one check over each item and stops at the first failure.
    let private each (items: 'T list) (check: 'T -> Result<unit, AdmissionFailure>) =
        items |> List.fold (fun result item -> result |> Result.bind (fun () -> check item)) (Ok())

    /// Admits an envelope for a negotiated tuple and its limits. The checks run in the spec order and stop at the
    /// first failure: the encoded limit; base64 bounds and canonical form; the decoded length; the SHA-256; depth
    /// and node limits; the profile, codec, envelope version and IR version against the tuple; then critical paths
    /// and nonempty identities. The model and its evidence payloads share the byte and node limits. Only a
    /// successful result holds the bytes, and the IR is not decoded.
    [<CompiledName "Admit">]
    let admit (tuple: ModelTransportTuple) (limits: ModelTransportLimits) (model: MorphirModel) : Result<AdmittedModel, AdmissionFailure> =
        let payloads = model.Payload :: [ for evidence in model.Evidence -> evidence.Evidence ]

        let encoded =
            payloads |> List.sumBy (fun payload -> int64 (Encoding.UTF8.GetByteCount payload.Content))

        if encoded > int64 limits.MaxEncodedBytes then
            Error(AdmissionFailure.EncodedLimitExceeded(encoded, limits.MaxEncodedBytes))
        elif payloads |> List.exists (fun payload -> Base64.padding payload.Content < 0) then
            Error AdmissionFailure.InvalidBase64
        else
            let decoded = payloads |> List.sumBy (fun payload -> Base64.decodedLength payload.Content)

            if decoded > int64 limits.MaxDecodedBytes then
                Error(AdmissionFailure.DecodedLimitExceeded(decoded, limits.MaxDecodedBytes))
            else
                let canonical =
                    payloads
                    |> List.fold
                        (fun result payload ->
                            result
                            |> Result.bind (fun decoded ->
                                match Base64.decodeCanonical payload.Content with
                                | Some bytes -> Ok(decoded @ [ payload, bytes ])
                                | None -> Error AdmissionFailure.InvalidBase64))
                        (Ok [])

                canonical
                |> Result.bind (fun decodedPayloads ->
                    let lengths =
                        each decodedPayloads (fun (payload, bytes) ->
                            if payload.DecodedByteLength = bytes.Length then
                                Ok()
                            else
                                Error(AdmissionFailure.LengthMismatch(payload.DecodedByteLength, bytes.Length)))

                    let digests () =
                        each decodedPayloads (fun (payload, bytes) ->
                            let actual = Sha256Digest.Compute(ReadOnlySpan bytes)

                            if payload.Sha256.Equals actual then
                                Ok()
                            else
                                Error(AdmissionFailure.DigestMismatch(payload.Sha256, actual)))

                    let scans () =
                        let remaining = ref (int64 limits.MaxNodes)

                        decodedPayloads
                        |> List.fold
                            (fun result (payload, bytes) ->
                                result
                                |> Result.bind (fun facts ->
                                    let outcome =
                                        match payload.Codec with
                                        | ModelCodec.IonBinary -> PayloadScan.ion bytes limits.MaxDepth remaining.Value
                                        | ModelCodec.MorphirJson -> PayloadScan.json bytes limits.MaxDepth remaining.Value

                                    match outcome with
                                    | PayloadScan.Scanned scanned ->
                                        remaining.Value <- remaining.Value - int64 scanned.Nodes
                                        Ok(facts @ [ scanned ])
                                    | PayloadScan.Malformed reason -> Error(AdmissionFailure.MalformedPayload(payload.Codec, reason))
                                    | PayloadScan.TooDeep -> Error(AdmissionFailure.DepthLimitExceeded limits.MaxDepth)
                                    | PayloadScan.TooManyNodes -> Error(AdmissionFailure.NodeLimitExceeded limits.MaxNodes)))
                            (Ok [])

                    let matchesTuple () =
                        if not (String.Equals(model.Profile, tuple.Profile, StringComparison.Ordinal)) then
                            Error(AdmissionFailure.ProfileMismatch(tuple.Profile, model.Profile))
                        elif model.Payload.Codec <> tuple.Codec then
                            Error(AdmissionFailure.CodecMismatch(tuple.Codec, model.Payload.Codec))
                        elif not (model.EnvelopeVersion.Equals tuple.EnvelopeVersion) then
                            Error(AdmissionFailure.EnvelopeVersionMismatch(tuple.EnvelopeVersion, model.EnvelopeVersion))
                        elif not (model.IrVersion.Equals tuple.IrVersion) then
                            Error(AdmissionFailure.IrVersionMismatch(tuple.IrVersion, model.IrVersion))
                        else
                            Ok()

                    let critical () =
                        match model.Critical |> Seq.tryFind (fun path -> not (containsOrdinal knownCriticalPaths path)) with
                        | Some path -> Error(AdmissionFailure.UnknownCriticalPath path)
                        | None -> Ok()

                    let identities () =
                        let required =
                            [ "modelId", model.ModelId
                              "revision", model.Revision
                              "profile", model.Profile
                              for index, evidence in Seq.indexed model.Evidence do
                                  $"evidence[{index}].guarantee", evidence.Guarantee
                                  $"evidence[{index}].modelId", evidence.ModelId
                                  $"evidence[{index}].modelRevision", evidence.ModelRevision
                                  $"evidence[{index}].scope", evidence.Scope
                                  $"evidence[{index}].producer", evidence.Producer ]

                        match required |> List.tryFind (fun (_, value) -> String.IsNullOrEmpty value) with
                        | Some(path, _) -> Error(AdmissionFailure.EmptyIdentity path)
                        | None -> Ok()

                    lengths
                    |> Result.bind digests
                    |> Result.bind scans
                    |> Result.bind (fun facts ->
                        matchesTuple ()
                        |> Result.bind critical
                        |> Result.bind identities
                        |> Result.map (fun () ->
                            let evidence =
                                List.zip3 (List.ofSeq model.Evidence) (List.tail decodedPayloads) (List.tail facts)
                                |> List.map (fun (evidence, (_, bytes), facts) -> AdmittedEvidence(evidence, bytes, facts))
                                |> Array.ofList

                            AdmittedModel(model, tuple, snd decodedPayloads.Head, facts.Head, evidence))))

    /// Establishes each required guarantee of a request from the admitted evidence of its model. A required
    /// guarantee must be one that the session can interpret. The evidence must name the guarantee, bind the model
    /// id, the revision and the request contextDigest, match the exact scope, and pass the receiver's trust
    /// policy. Evidence that the policy does not trust counts as absent.
    [<CompiledName "Establish">]
    let establish
        (agreed: ModelTransportSupport)
        (contextDigest: Sha256Digest)
        (required: IReadOnlyList<ModelGuaranteeRequirement>)
        (trust: Func<ModelGuaranteeEvidence, bool>)
        (model: AdmittedModel)
        : Result<IReadOnlyList<EstablishedGuarantee>, AdmissionFailure> =
        let envelope = model.Envelope

        let current (evidence: ModelGuaranteeEvidence) =
            String.Equals(evidence.ModelId, envelope.ModelId, StringComparison.Ordinal)
            && String.Equals(evidence.ModelRevision, envelope.Revision, StringComparison.Ordinal)
            && evidence.ContextDigest.Equals contextDigest

        let establishOne (requirement: ModelGuaranteeRequirement) =
            let claims =
                model.Evidence
                |> Seq.filter (fun admitted ->
                    String.Equals(admitted.Evidence.Guarantee, requirement.Guarantee, StringComparison.Ordinal)
                    && trust.Invoke admitted.Evidence)
                |> List.ofSeq

            let inScope (admitted: AdmittedEvidence) =
                String.Equals(admitted.Evidence.Scope, requirement.Scope, StringComparison.Ordinal)

            match claims |> List.tryFind (fun admitted -> current admitted.Evidence && inScope admitted) with
            | Some admitted -> Ok { Requirement = requirement; Evidence = admitted }
            | None when claims |> List.exists (fun admitted -> current admitted.Evidence) ->
                Error(AdmissionFailure.WrongEvidenceScope requirement)
            | None when not claims.IsEmpty -> Error(AdmissionFailure.StaleEvidence requirement)
            | None -> Error(AdmissionFailure.MissingEvidence requirement)

        match required |> Seq.tryFind (fun requirement -> not (containsOrdinal agreed.Guarantees requirement.Guarantee)) with
        | Some requirement -> Error(AdmissionFailure.UnsupportedGuarantee requirement.Guarantee)
        | None ->
            required
            |> Seq.fold
                (fun result requirement ->
                    result |> Result.bind (fun established -> establishOne requirement |> Result.map (fun one -> one :: established)))
                (Ok [])
            |> Result.map (fun established -> List.rev established |> Array.ofList :> IReadOnlyList<_>)

    /// Checks the success tag and the model member of a compileModel result. A success needs a model; a failure
    /// must not have the member, also not as null. The returned model still needs admission for the output tuple.
    [<CompiledName "CheckCompileOutcome">]
    let checkCompileOutcome (success: bool) (model: ModelMember) : Result<MorphirModel option, AdmissionFailure> =
        match success, model with
        | true, ModelMember.Present model -> Ok(Some model)
        | true, _ -> Error AdmissionFailure.MissingModel
        | false, ModelMember.Absent -> Ok None
        | false, _ -> Error AdmissionFailure.ModelOnFailure

    /// The information that converting an admitted model to a target tuple loses. Ion binary to Morphir JSON loses
    /// annotations, wide integers, decimals and Ion-only values; a change of profile loses profile content.
    [<CompiledName "ConversionLosses">]
    let conversionLosses (source: AdmittedModel) (target: ModelTransportTuple) : IReadOnlyList<ConversionLoss> =
        let facts = source.Facts
        let toJson = source.Tuple.Codec = ModelCodec.IonBinary && target.Codec = ModelCodec.MorphirJson

        [| if toJson && facts.HasAnnotations then ConversionLoss.Annotations
           if toJson && facts.HasWideIntegers then ConversionLoss.IntegerPrecision
           if toJson && facts.HasDecimals then ConversionLoss.DecimalPrecision
           if toJson && facts.HasIonOnlyValues then ConversionLoss.IonOnlyValues
           if not (String.Equals(source.Tuple.Profile, target.Profile, StringComparison.Ordinal)) then
               ConversionLoss.ProfileContent |]

    /// Decides how an admitted model reaches an agreed tuple of an operation. A model in an agreed tuple is sent
    /// unchanged. Otherwise the target is the first agreed tuple in host order, with the same envelope and IR
    /// version, that the model reaches without loss; when there is none, the first reachable tuple if the user
    /// allows loss. A lossy conversion is refused with data.lossy_conversion, and only an unreachable agreement is
    /// extension.tuple_unavailable.
    [<CompiledName "PlanConversion">]
    let planConversion
        (agreed: ModelTransportSupport option)
        (operation: ModelOperation)
        (source: AdmittedModel)
        (allowLoss: bool)
        : Result<ConversionPlan, ConversionRefusal> =
        let current = { source.Tuple with Operation = operation }
        let tuples = agreedTuples agreed operation

        if Seq.contains current tuples then
            Ok(ConversionPlan.Unchanged current)
        else
            let reachable =
                tuples
                |> Seq.filter (fun tuple ->
                    tuple.EnvelopeVersion.Equals current.EnvelopeVersion && tuple.IrVersion.Equals current.IrVersion)
                |> Seq.map (fun tuple -> tuple, conversionLosses source tuple)
                |> List.ofSeq

            match reachable |> List.tryFind (fun (_, losses) -> losses.Count = 0), reachable with
            | Some(target, losses), _ -> Ok(ConversionPlan.Convert(target, losses))
            | None, [] -> Error(ConversionRefusal.TupleUnavailable { Operation = operation })
            | None, (target, losses) :: _ when allowLoss -> Ok(ConversionPlan.Convert(target, losses))
            | None, (target, losses) :: _ -> Error(ConversionRefusal.LossyConversion(target, losses))
