// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Buffers
open System.Text.Json

/// A JSON-RPC 2.0 message, classified by its members. Params and results stay raw JSON until a method binds them.
[<RequireQualifiedAccess>]
type JsonRpcMessage =
    /// A message with an id and a method: it takes a reply.
    | Request of Id: RequestId * Method: string * Params: JsonElement option
    /// A message with a method and no id: it takes no reply.
    | Notification of Method: string * Params: JsonElement option
    /// A successful reply.
    | Response of Id: RequestId * Result: JsonElement
    /// An error reply. The id is absent when the request id could not be read.
    | ErrorResponse of Id: RequestId option * Error: RpcError

    /// Stable text for diagnostics.
    override this.ToString() =
        match this with
        | Request(id, method, _) -> $"Request {id} {method}"
        | Notification(method, _) -> $"Notification {method}"
        | Response(id, _) -> $"Response {id}"
        | ErrorResponse(Some id, error) -> $"Error response {id}: {error}"
        | ErrorResponse(None, error) -> $"Error response: {error}"

/// A message that the reader refused, and the error reply for it.
type MessageRefusal =
    {
        /// The request id to echo, when the reader could read it.
        Id: RequestId option
        /// The error.
        Error: RpcError
        /// False when the refused message was a response or a notification: JSON-RPC sends no reply to those.
        Answer: bool
    }

    /// For C# callers: the request id to echo, or null when the reader could not read it.
    member this.IdOrNull() : RequestId | null = Option.toObj this.Id

    /// Stable text for diagnostics.
    override this.ToString() = this.Error.ToString()

/// Thrown by a method handler to reply with this error object instead of a result. The dispatcher sends the
/// error as it is; any other exception gives -32603 with no exception text.
[<Sealed>]
type RpcException(error: RpcError) =
    inherit Exception(error.Message)

    /// The error object of the reply.
    member _.Error = error

/// The error objects of the pinned error codes.
[<RequireQualifiedAccess>]
module RpcErrors =
    /// The standard message of a code. The -32014 and -32601 texts are those of the pinned transcripts.
    let message (code: ErrorCode) =
        match code with
        | ErrorCode.ParseError -> "Parse error"
        | ErrorCode.InvalidRequest -> "Invalid Request"
        | ErrorCode.MethodNotFound -> "Method not found"
        | ErrorCode.InvalidParams -> "Invalid params"
        | ErrorCode.InternalError -> "Internal error"
        | ErrorCode.ExtensionError -> "Extension error"
        | ErrorCode.CompilationError -> "Compilation error"
        | ErrorCode.GenerationError -> "Generation error"
        | ErrorCode.ValidationError -> "Validation error"
        | ErrorCode.TransformationError -> "Transformation error"
        | ErrorCode.ExtensionFailure -> "Extension failure"
        | ErrorCode.ProtocolVersionMismatch -> "No common protocol version"
        | ErrorCode.PermissionDenied -> "Permission denied"
        | ErrorCode.CapabilityUnavailable -> "Capability unavailable"
        | ErrorCode.NotInitialized -> "Request is not allowed before initialization or after shutdown"
        | ErrorCode.RequestCancelled -> "Request cancelled"
        | other -> $"Error {int other}"

    /// The error with the standard message.
    [<CompiledName "Create">]
    let create (code: ErrorCode) : RpcError =
        { Code = int code
          Message = message code
          Data = None }

    /// The error with a specific message.
    [<CompiledName "WithMessage">]
    let withMessage (code: ErrorCode) (text: string) : RpcError =
        { Code = int code
          Message = text
          Data = None }

    /// The error with a specific message and structured data. No data is the same as withMessage.
    [<CompiledName "WithData">]
    let withData (code: ErrorCode) (text: string) (data: Nullable<JsonElement>) : RpcError =
        { Code = int code
          Message = text
          Data = Option.ofNullable data }

/// Reads and writes JSON-RPC 2.0 messages. This is the only hand-written JSON of the MEP layer (design RPC-1).
[<AbstractClass; Sealed>]
type JsonRpc private () =
    static let refuse id answer code =
        Error
            { Id = id
              Error = RpcErrors.create code
              Answer = answer }

    static let readId (element: JsonElement) =
        try
            match JsonSerializer.Deserialize(element, MepJson.Default.RequestId :> Serialization.Metadata.JsonTypeInfo) with
            | :? RequestId as id -> Some id
            | _ -> None
        with :? JsonException ->
            None

    static let tryMember (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | true, value -> Some value
        | false, _ -> None

    static let classify (root: JsonElement) : Result<JsonRpcMessage, MessageRefusal> =
        let idMember = tryMember root "id"
        let methodMember = tryMember root "method"
        let paramsMember = tryMember root "params"
        let resultMember = tryMember root "result"
        let errorMember = tryMember root "error"

        let id =
            idMember
            |> Option.bind (fun value -> if value.ValueKind = JsonValueKind.Null then None else readId value)

        let isResponse = methodMember.IsNone && (resultMember.IsSome || errorMember.IsSome)
        let answer = not isResponse && idMember.IsSome

        let version =
            match tryMember root "jsonrpc" with
            | Some value when value.ValueKind = JsonValueKind.String && value.GetString() = "2.0" -> true
            | _ -> false

        let badId =
            match idMember with
            | Some value when value.ValueKind = JsonValueKind.Null -> not isResponse || resultMember.IsSome
            | Some _ -> id.IsNone
            | None -> false

        if not version || badId then
            refuse id answer ErrorCode.InvalidRequest
        else
            match methodMember with
            | Some methodValue ->
                let parameters =
                    match paramsMember with
                    | None -> Ok None
                    | Some value when
                        value.ValueKind = JsonValueKind.Object || value.ValueKind = JsonValueKind.Array
                        ->
                        Ok(Some(value.Clone()))
                    | Some _ -> Error()

                match methodValue.ValueKind, parameters, resultMember, errorMember with
                | JsonValueKind.String, Ok parameters, None, None ->
                    let name = methodValue.GetString() |> nonNull

                    match id with
                    | Some id -> Ok(JsonRpcMessage.Request(id, name, parameters))
                    | None -> Ok(JsonRpcMessage.Notification(name, parameters))
                | _ -> refuse id answer ErrorCode.InvalidRequest
            | None ->
                match id, resultMember, errorMember with
                | Some id, Some result, None -> Ok(JsonRpcMessage.Response(id, result.Clone()))
                | _, None, Some error when paramsMember.IsNone ->
                    try
                        match JsonSerializer.Deserialize(error, MepJson.Default.RpcError :> Serialization.Metadata.JsonTypeInfo) with
                        | :? RpcError as error -> Ok(JsonRpcMessage.ErrorResponse(id, error))
                        | _ -> refuse id false ErrorCode.InvalidRequest
                    with :? JsonException ->
                        refuse id false ErrorCode.InvalidRequest
                | _ -> refuse id answer ErrorCode.InvalidRequest

    /// Reads one message. Bad JSON gives -32700; JSON that is not a JSON-RPC 2.0 message gives -32600.
    static member Read(bytes: ReadOnlyMemory<byte>) : Result<JsonRpcMessage, MessageRefusal> =
        let document =
            try
                Some(JsonDocument.Parse(bytes))
            with :? JsonException ->
                None

        match document with
        | None -> refuse None true ErrorCode.ParseError
        | Some document ->
            use document = document

            if document.RootElement.ValueKind <> JsonValueKind.Object then
                refuse None true ErrorCode.InvalidRequest
            else
                classify document.RootElement

    /// The error reply for a refused message.
    static member Reply(refusal: MessageRefusal) =
        JsonRpcMessage.ErrorResponse(refusal.Id, refusal.Error)

    /// Parses a JSON constant, such as a fixed result.
    static member Constant(json: string) : JsonElement =
        use document = JsonDocument.Parse json
        document.RootElement.Clone()

    /// Writes one message as compact UTF-8 JSON, members in the order jsonrpc, id, method, params, result, error.
    static member Write(message: JsonRpcMessage) : byte array =
        let buffer = ArrayBufferWriter<byte>()

        do
            use writer = new Utf8JsonWriter(buffer)
            writer.WriteStartObject()
            writer.WriteString("jsonrpc", "2.0")

            let writeId (id: RequestId option) =
                writer.WritePropertyName "id"

                match id with
                | Some id -> JsonSerializer.Serialize(writer, id, MepJson.Default.RequestId)
                | None -> writer.WriteNullValue()

            let writeParams (parameters: JsonElement option) =
                parameters
                |> Option.iter (fun value ->
                    writer.WritePropertyName "params"
                    value.WriteTo writer)

            match message with
            | JsonRpcMessage.Request(id, method, parameters) ->
                writeId (Some id)
                writer.WriteString("method", method)
                writeParams parameters
            | JsonRpcMessage.Notification(method, parameters) ->
                writer.WriteString("method", method)
                writeParams parameters
            | JsonRpcMessage.Response(id, result) ->
                writeId (Some id)
                writer.WritePropertyName "result"
                result.WriteTo writer
            | JsonRpcMessage.ErrorResponse(id, error) ->
                writeId id
                writer.WritePropertyName "error"
                JsonSerializer.Serialize(writer, error, MepJson.Default.RpcError)

            writer.WriteEndObject()

        buffer.WrittenSpan.ToArray()
