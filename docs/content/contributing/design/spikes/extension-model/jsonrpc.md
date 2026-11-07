---
title: "JSON-RPC library spike, 2026-11-07"
linkTitle: "JSON-RPC library spike"
description: >
  Native AOT probes of CurlyRpc and StreamJsonRpc for the Morphir Extension Protocol (MEP), and the decision for the JSON-RPC layer of Morphir.Mep.
---

# JSON-RPC library spike, 2026-11-07

Status: Decided. `Morphir.Mep` writes its own minimal JSON-RPC 2.0 layer on System.Text.Json. It does not use CurlyRpc or StreamJsonRpc.

This spike is part of step 1 of the [Morphir extension model PRD](../../prds/morphir-extension-model.md). The probe sources are in `spikes/extension-model/jsonrpc/`.

MEP needs these items from the JSON-RPC layer:

- `Content-Length` framing over stdio.
- Request ids that are `u64` or string (`RequestId.NumericRequestId` is `u64` in `spec/mep/README.md`).
- Notifications.
- `$/cancelRequest`, with the reply code `RequestCancelled` (`-32800`).
- For WASM guests, a single-message dispatch, `bytes -> bytes`, with no connection.

## Reproduction

The probes build outside the repository. Thus the repository `Directory.Build.props` and central package management do not apply.

Environment: macOS 26.6.2, arm64, .NET SDK 10.0.403, ILCompiler 10.0.14, RID `osx-arm64`.

```bash
export DOTNET_ROOT="$HOME/.local/share/mise/installs/dotnet/10.0.403"
export PATH="$DOTNET_ROOT:$PATH"
WORK="$(mktemp -d)"
cp -R spikes/extension-model/jsonrpc "$WORK/"
cd "$WORK/jsonrpc"
for p in BaselineProbe CurlyRpcProbe StreamJsonRpcProbe; do
  dotnet publish "$p/$p.csproj" -c Release -o "out/$p" > "$p.publish.log" 2>&1
  echo "$p: $(grep -c 'warning IL' "$p.publish.log") IL warnings"
  grep -oE 'warning IL[0-9]+' "$p.publish.log" | sort | uniq -c
done
stat -f '%z %N' out/*/*-probe
./out/BaselineProbe/baseline-probe
./out/CurlyRpcProbe/curly-probe
./out/StreamJsonRpcProbe/sjr-probe
```

Each probe prints one `PASS` or `FAIL` line for each check. A probe exits with code 1 if one check fails.

The probes:

- `RawWire.cs` is the shared harness. It connects two in-memory pipes. It writes and reads raw `Content-Length` frames, so it checks the bytes on the wire. Each raw check uses a new connection, so one fatal error does not hide the next check. Each check has a 10-second limit.
- `BaselineProbe` has no JSON-RPC library. It sends one frame with source-generated JSON. Its size is the floor for any MEP layer.
- `CurlyRpcProbe` uses CurlyRpc 0.9.9 with the typed `AddLocalRpcMethod<...>` overloads and a `JsonSerializerContext`.
- `StreamJsonRpcProbe` uses StreamJsonRpc 2.26.150 with the `SystemTextJsonFormatter`, `RpcTargetMetadata.FromShape<IServer>()` and a `JsonSerializerContext`. It follows the "UTF-8 JSON" sample of the [StreamJsonRpc Native AOT guide](https://microsoft.github.io/vs-streamjsonrpc/docs/nativeAOT.html). It does not suppress IL2026 or IL3050.

Each library probe serves one request method (`echo`), one notification (`log`) and one cancellable method (`slow`). The checks:

1. A library client calls the library server: a request, a notification and a cancelled request.
2. A raw peer sends requests with the ids `"req-abc"`, `42`, `18446744073709551615` (`u64` maximum) and `9223372036854775808` (`long` maximum + 1). The reply must carry the same id token.
3. A raw peer sends a notification. The server must not reply.
4. A raw peer sends `$/cancelRequest` for a string id and for the `u64` maximum id. The handler token must cancel, and the reply must be an error with the same id.
5. A raw peer sends a params object with one member, `{"text":"solo"}`. The server must bind it to a DTO with one member.
6. A raw peer calls an unknown method. The reply must be `-32601`.

## Results

### Baseline (no library)

| Item | Result |
| --- | --- |
| IL warnings | 0 |
| Binary size | 2,613,232 bytes |
| Correctness | The frame round trip passes. |

### CurlyRpc 0.9.9

| Item | Result |
| --- | --- |
| IL warnings | 0. The log has no `warning` line. |
| Binary size | 3,327,272 bytes (baseline + 714,040) |
| Dependencies | `Microsoft.Extensions.Logging.Abstractions` 10.0.9 |
| Correctness | 11 of 12 checks pass. |
| Request, notification, unknown method | Pass. |
| `$/cancelRequest` | Pass, with a string id and with the `u64` maximum id. The handler token cancels. The reply is `-32800`. |
| String id | Pass. The reply id is `"req-abc"`. |
| `u64` id | Pass. The reply echoes the raw token `18446744073709551615`. `9223372036854775808` also passes. |
| Single-member params object | Fail. The reply is `-32602` "Could not deserialize parameter at position 0". |
| Single-message dispatch | No. `JsonRpc` needs an `IJsonRpcMessageHandler` connection. There is no public dispatch member. |

The single-member defect: a typed handler with one parameter reads a params object with one member as a by-name wrapper. It binds the value of that member, not the object. A JIT probe showed this:

- `{"text":"hi"}` to a `OneField` handler: `-32602`.
- `{"text":"hi"}` to a `JsonElement` handler: the handler gets `"hi"`, not the object.
- `{"text":"hi","x":1}`, `[{"text":"hi"}]` and `{"anyName":{"text":"hi"}}`: the handler gets the object.

The CurlyRpc client has the same defect against its own server. `InvokeWithParameterObjectAsync` with a one-member DTO fails.

MEP has params objects with one member. `DescribeParams` for `morphir.extension.describe` has only `protocolVersions` (`spec/mep/smithy/model/handshake.smithy`). `GenerateRequest` has three members, but two are optional and absent members are left out. Thus a CurlyRpc guest cannot receive these requests through a typed handler. An inbound middleware can read the raw params and reply with `Respond(result)`, but then the handler does not get the cancellation token.

Other facts: CurlyRpc is pre-1.0. One owner publishes it on NuGet (sebastienros), with 2,385 downloads. Its defaults have no frame size limit; `JsonRpcOptions.CreateHardened()` sets limits.

### StreamJsonRpc 2.26.150

| Item | Result |
| --- | --- |
| IL warnings | 10: 8 × IL2026, 2 × IL3050. These are 5 call sites. The Roslyn analyzer and ILC each report each site once. |
| Warning sites | `new SystemTextJsonFormatter()`: IL2026 and IL3050. Every connection needs this call. `InvokeWithParameterObjectAsync(string, object)` (2 sites) and `NotifyWithParameterObjectAsync(string, object)`: IL2026. |
| Binary size | 5,218,376 bytes (baseline + 2,605,144) |
| Dependencies | MessagePack, Nerdbank.MessagePack, PolyType, Newtonsoft.Json, Nerdbank.Streams, Microsoft.VisualStudio.Threading.Only and others |
| Correctness | 9 of 12 checks pass. |
| Request, notification, unknown method | Pass. |
| `$/cancelRequest` | Pass with a string id (`-32800`). Fail with the `u64` maximum id: the connection closes. |
| String id | Pass. |
| `u64` id | Fail. An id above `long.MaxValue` closes the connection with `StreamError`. The server sends no error reply. Both `18446744073709551615` and `9223372036854775808` fail. |
| Single-member params object | Pass, with `UseSingleObjectParameterDeserialization = true`. |
| Single-message dispatch | No. `DispatchRequestAsync` and `HandleRpcRequestAsync` are protected members of a connected `JsonRpc`. They send the reply through the message handler. |

The probe passes only after these changes:

- The `JsonSerializerContext` must contain `StreamJsonRpc.Protocol.CommonErrorData`. Without it, the first error reply throws `NotSupportedException`, and the connection closes.
- The `JsonSerializerContext` must contain `StreamJsonRpc.RequestId`. Without it, the server drops each `$/cancelRequest`. Only a trace warning shows the failure.
- The client must not use the trim-safe `NamedArgs` overloads. They write the CLR member names (`"Text"`, `"Count"`) and ignore the JSON naming policy, so the server reads nulls.
- The client must not use the source-generated proxy (`Attach<IServer>()`). It sends a positional array. A `UseSingleObjectParameterDeserialization` method rejects the array with `-32602`.

The first two items put library-internal types into the application context. A library update can add more such types, and no build warning shows them.

### Summary

| Candidate | IL warnings | Size (bytes) | Correct | String id | `u64` id | Single-message dispatch |
| --- | --- | --- | --- | --- | --- | --- |
| Baseline | 0 | 2,613,232 | yes | n/a | n/a | n/a |
| CurlyRpc 0.9.9 | 0 | 3,327,272 | no: one-member params | yes | yes | no |
| StreamJsonRpc 2.26.150 | 10 (IL2026, IL3050) | 5,218,376 | no: `u64` ids close the connection | yes | no | no |

## Decision

`Morphir.Mep` writes its own minimal JSON-RPC 2.0 layer on System.Text.Json.

Reasons:

- StreamJsonRpc fails a MEP wire rule. A `u64` id above `long.MaxValue` closes the connection. It also has IL2026 and IL3050 on the formatter constructor, which every connection needs. Zero warnings is not possible without suppressions. It adds 2.6 MB.
- CurlyRpc passes the AOT checks and the id checks. But it cannot bind a params object with one member, and MEP has such params (`DescribeParams`). The only workaround bypasses typed dispatch and loses cancellation. CurlyRpc is pre-1.0, with one owner.
- Neither library has single-message dispatch. The WASM path needs our own dispatcher in all cases. With a library, `Morphir.Mep` has two dispatch paths. With our own layer, the stdio connection and the WASM guest use the same dispatcher.

Estimate for the own layer: 800 to 1,100 lines with tests, as the PRD says. Approximate parts:

| Part | Lines (estimate) |
| --- | --- |
| Messages, `RequestId` and a hand-written envelope converter | 200 |
| Dispatcher, `DispatchAsync(message bytes) -> response bytes` | 200 |
| `Content-Length` framer with a frame size limit | 100 |
| Connection: pending-request table, `$/cancelRequest`, progress notifications | 250 |
| Tests | 300 to 350 |

The size of the binary is an estimate. It was not measured. The own layer has fewer features than CurlyRpc (no batches, streaming, middleware, logging bridge or metrics). Thus it should add less than the 714,040 bytes of CurlyRpc. The estimate is 100 to 300 KB over the baseline.

Prior art for the own layer: the CurlyRpc envelope parser (`JsonDocument` navigation, raw `RawJsonValue` payloads), the [LanguageServer.Framework](https://github.com/CppCXY/LanguageServer.Framework) JSON-RPC layer (about 540 lines), and the message converter in the [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

## Consequences for step 1

- Do not add CurlyRpc or StreamJsonRpc to `Directory.Packages.props`.
- Model `RequestId` as a closed union: a `u64` number or a string. Parse it with `Utf8JsonReader.TryGetUInt64`. Keep the parsed value, and write the same token in the reply.
- Reply with an error for a bad message. Do not close the connection. Send `-32600` for a bad id (negative, fraction, too long) and `-32700` with a `null` id for bad JSON.
- Bind the complete params object to one DTO through its `JsonTypeInfo<T>`. Do not unwrap a params object that has one member.
- Use one dispatcher for stdio and WASM. The stdio connection reads a frame, calls `DispatchAsync`, and writes the reply frame. The WASM guest calls `DispatchAsync` directly.
- Use only source-generated `JsonTypeInfo<T>` for payloads. The MEP layer must publish with zero IL warnings and no suppressions.
- Set a frame size limit on the framer. The limit follows `crates/morphir-host-native/src/process/frame.rs` in `finos/morphir-rust`.
- Reply `-32800` (`RequestCancelled`) when `$/cancelRequest` cancels a request.
- Turn the raw-wire checks of this spike into unit tests of `Morphir.Mep`: string id, `u64` maximum id, `long` maximum + 1 id, notification with no reply, `$/cancelRequest` with each id kind, one-member params object and unknown method.
- Update the "JSON-RPC research" evidence in the PRD with a link to this page.
