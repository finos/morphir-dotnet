// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization.Metadata
open System.Threading
open System.Threading.Tasks

/// A method handler bound to its params and result codecs. F# callers build one with the Handlers module; C# callers
/// use the static members Request, RequestWithoutParams, Constant and Notification. A handler throws RpcException to
/// reply with an error object.
[<Sealed>]
type MethodHandler
    internal (invoke: JsonElement option -> CancellationToken -> Task<Result<JsonElement, RpcError>>) =
    member internal _.Invoke(parameters, cancellation) = invoke parameters cancellation

    /// Stable text for diagnostics.
    override _.ToString() = "Method handler"

/// What a gate decides for an inbound request or notification.
[<RequireQualifiedAccess>]
type Admission =
    /// Run the handler.
    | Proceed
    /// Reply to a request with this error; drop a notification.
    | Refuse of RpcError

    /// Stable text for diagnostics.
    override this.ToString() =
        match this with
        | Proceed -> "Proceed"
        | Refuse error -> $"Refuse: {error}"

    /// For C# callers: an admission that refuses with this error.
    static member RefuseWith(error: RpcError) = Refuse error

/// Builds method handlers from typed functions. C# callers use the static members of MethodHandler.
[<RequireQualifiedAccess>]
module Handlers =
    let private bind<'P when 'P: not null> (info: JsonTypeInfo<'P>) (parameters: JsonElement option) =
        match parameters with
        | None -> None
        | Some element ->
            try
                match JsonSerializer.Deserialize(element, info :> JsonTypeInfo) with
                | :? 'P as value -> Some value
                | _ -> None
            with :? JsonException ->
                None

    let private invalidParams = Error(RpcErrors.create ErrorCode.InvalidParams)

    let private encode<'R> (info: JsonTypeInfo<'R>) (result: Result<'R, RpcError>) =
        result |> Result.map (fun value -> JsonSerializer.SerializeToElement(value, info))

    /// A request whose params object binds through its codec as one value. Absent or mismatched params give -32602.
    let request<'P, 'R when 'P: not null>
        (paramsInfo: JsonTypeInfo<'P>)
        (resultInfo: JsonTypeInfo<'R>)
        (handle: 'P -> CancellationToken -> Task<Result<'R, RpcError>>)
        =
        MethodHandler(fun parameters cancellation ->
            match bind paramsInfo parameters with
            | None -> Task.FromResult invalidParams
            | Some value ->
                task {
                    let! result = handle value cancellation
                    return encode resultInfo result
                })

    /// A request that takes no params. Params, when present, are ignored.
    let requestWithoutParams<'R> (resultInfo: JsonTypeInfo<'R>) (handle: CancellationToken -> Task<Result<'R, RpcError>>) =
        MethodHandler(fun _ cancellation ->
            task {
                let! result = handle cancellation
                return encode resultInfo result
            })

    /// A request with raw params and a raw result.
    let raw (handle: JsonElement option -> CancellationToken -> Task<Result<JsonElement, RpcError>>) =
        MethodHandler handle

    /// A request that always returns the same JSON result, such as {"ok":true} for morphir.ping.
    let constant (json: string) =
        let result = Ok(JsonRpc.Constant json)
        MethodHandler(fun _ _ -> Task.FromResult result)

    /// A notification with raw params. Its result is never sent.
    let notification (handle: JsonElement option -> Task) =
        MethodHandler(fun parameters _ ->
            task {
                do! handle parameters
                return Ok(JsonRpc.Constant "null")
            })

    /// A notification whose params bind through a codec. Absent or mismatched params drop the notification.
    let typedNotification<'P when 'P: not null> (paramsInfo: JsonTypeInfo<'P>) (handle: 'P -> CancellationToken -> Task) =
        MethodHandler(fun parameters cancellation ->
            match bind paramsInfo parameters with
            | None -> Task.FromResult invalidParams
            | Some value ->
                task {
                    do! handle value cancellation
                    return Ok(JsonRpc.Constant "null")
                })

/// The C# factories of method handlers. A handler replies with an error object by throwing RpcException.
type MethodHandler with

    /// A request whose params object binds through its codec as one value. Absent or mismatched params give -32602.
    static member Request<'P, 'R when 'P: not null>
        (
            paramsInfo: JsonTypeInfo<'P>,
            resultInfo: JsonTypeInfo<'R>,
            handle: Func<'P, CancellationToken, Task<'R>>
        ) =
        Handlers.request paramsInfo resultInfo (fun value cancellation ->
            task {
                let! result = handle.Invoke(value, cancellation)
                return Ok result
            })

    /// A request that takes no params. Params, when present, are ignored.
    static member RequestWithoutParams<'R>(resultInfo: JsonTypeInfo<'R>, handle: Func<CancellationToken, Task<'R>>) =
        Handlers.requestWithoutParams resultInfo (fun cancellation ->
            task {
                let! result = handle.Invoke cancellation
                return Ok result
            })

    /// A request that always returns the same JSON result, such as {"ok":true} for morphir.ping.
    static member Constant(json: string) = Handlers.constant json

    /// A notification whose params bind through a codec. Absent or mismatched params drop the notification.
    static member Notification<'P when 'P: not null>(paramsInfo: JsonTypeInfo<'P>, handle: Func<'P, CancellationToken, Task>) =
        Handlers.typedNotification paramsInfo (fun value cancellation -> handle.Invoke(value, cancellation))

/// Dispatches one message to a method table (design RPC-1). Streams and single messages use the same path.
[<Sealed>]
type Dispatcher(handlers: IReadOnlyDictionary<string, MethodHandler>, gate: Func<string, MethodKind, Admission>) =
    let failed (id: RequestId) (code: ErrorCode) =
        Some(JsonRpcMessage.ErrorResponse(Some id, RpcErrors.create code))

    /// A dispatcher that admits every message.
    new(handlers: IReadOnlyDictionary<string, MethodHandler>) =
        Dispatcher(handlers, Func<string, MethodKind, Admission>(fun _ _ -> Admission.Proceed))

    /// True when the gate admits the method now.
    member _.Admits(method: string, kind: MethodKind) =
        match gate.Invoke(method, kind) with
        | Admission.Proceed -> true
        | Admission.Refuse _ -> false

    /// Handles one classified message. A request gets a reply message; a notification or a response gets none.
    member internal _.DispatchMessageAsync(message: JsonRpcMessage, cancellation: CancellationToken) : Task<JsonRpcMessage option> =
        match message with
        | JsonRpcMessage.Response _
        | JsonRpcMessage.ErrorResponse _ -> Task.FromResult None
        | JsonRpcMessage.Notification(method, parameters) ->
            match gate.Invoke(method, MethodKind.Notification), handlers.TryGetValue method with
            | Admission.Proceed, (true, handler) ->
                task {
                    try
                        let! _ = handler.Invoke(parameters, cancellation)
                        ()
                    with _ ->
                        ()

                    return None
                }
            | _ -> Task.FromResult None
        | JsonRpcMessage.Request(id, method, parameters) ->
            match gate.Invoke(method, MethodKind.Request) with
            | Admission.Refuse error -> Task.FromResult(Some(JsonRpcMessage.ErrorResponse(Some id, error)))
            | Admission.Proceed ->
                match handlers.TryGetValue method with
                | false, _ -> Task.FromResult(failed id ErrorCode.MethodNotFound)
                | true, handler ->
                    task {
                        try
                            let! result = handler.Invoke(parameters, cancellation)

                            return
                                match result with
                                | Ok value -> Some(JsonRpcMessage.Response(id, value))
                                | Error error -> Some(JsonRpcMessage.ErrorResponse(Some id, error))
                        with
                        | :? RpcException as error -> return Some(JsonRpcMessage.ErrorResponse(Some id, error.Error))
                        | :? OperationCanceledException when cancellation.IsCancellationRequested ->
                            return failed id ErrorCode.RequestCancelled
                        | _ -> return failed id ErrorCode.InternalError
                    }

    /// Handles one message as bytes, as the WASM runtime passes it. Returns the reply bytes, or null for a
    /// notification or a response.
    member this.DispatchAsync(message: ReadOnlyMemory<byte>, cancellation: CancellationToken) : Task<byte array | null> =
        match JsonRpc.Read message with
        | Error refusal when refusal.Answer -> Task.FromResult<byte array | null>(JsonRpc.Write(JsonRpc.Reply refusal))
        | Error _ -> Task.FromResult<byte array | null> null
        | Ok message ->
            task {
                let! reply = this.DispatchMessageAsync(message, cancellation)

                return
                    match reply with
                    | Some reply -> JsonRpc.Write reply |> withNull
                    | None -> null
            }
