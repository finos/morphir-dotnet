# Morphir.Mep

One implementation of the Morphir Extension Protocol (MEP) `0.2.0-draft.1` for
morphir-dotnet hosts and guests: JSON-RPC 2.0 messages, `Content-Length` framing,
the session lifecycle and the model transport. Work is tracked by
[#512](https://github.com/finos/morphir-dotnet/issues/512) and
[`add-mep-core`](../../openspec/changes/add-mep-core/proposal.md). The roadmap is the
[Morphir extension model PRD](../../docs/content/contributing/design/prds/morphir-extension-model.md), step 1.

## Layers

| Layer | File | What it does |
| --- | --- | --- |
| Wire types | `Wire.fs` | Records and unions of the pinned contract, with parsed values (`ProtocolVersion`, `SemanticVersion`, `Sha256Digest`, `RequestId`). `MepJson` is generated from this file at build time by `Morphir.Myriad.SystemTextJson`. There is no reflection serializer. |
| Model transport | `ModelTransport.fs` | Tuple selection, the host check of a selection, envelope creation and admission. Pure functions. |
| Message codec | `Rpc.fs` | Classifies a JSON-RPC message by its members (`JsonRpcMessage`), keeps params raw, and writes messages. This is the only hand-written JSON. |
| Dispatcher | `Dispatcher.fs` | `DispatchAsync(bytes, ct)` returns the reply bytes, or none for a notification. A method table maps method names to typed handlers; a gate refuses a method before its handler runs. |
| Framing | `Framing.fs` | `Content-Length` frames on a `Stream`: header blocks up to 8192 bytes, bodies up to 64 MiB, checked before the body is read. A refused frame ends the connection. |
| Connection | `Connection.fs` | Reads frames, dispatches inbound requests concurrently, matches inbound responses to the pending-request table, and sends and receives `$/cancelRequest`. |
| Session | `Session.fs` | The pure lifecycle rules (`Lifecycle.admit`) and version negotiation, shared by hosts and guests. |
| Peers | `Peers.fs` | `Guest` (lifecycle methods, gate, extension methods) and `HostSession` (refuses to send what the guest must refuse). |

The stream path (`Connection`) and the single-message path (`Dispatcher.DispatchAsync`)
use the same dispatcher, so they give the same bytes. Starting a guest process is not
in this project: a process host connects a `HostSession` to the child's streams.

## A guest and a host in memory

This example is compiled by `tests/Morphir.Mep.Tests/ReadmeTests.fs`. Keep the two equal.

```fsharp
let runSession (claims: CapabilityClaimSet) (capabilities: ExtensionCapabilities) =
    task {
        let guest =
            Guest(
                { Claims = claims
                  Capabilities = capabilities
                  Methods = readOnlyDict [] }
            )

        let toGuest = Pipe()
        let toHost = Pipe()
        let guestRun = guest.RunAsync(toGuest.Reader.AsStream(), toHost.Writer.AsStream(), CancellationToken.None)

        let host =
            HostSession(
                toHost.Reader.AsStream(),
                toGuest.Writer.AsStream(),
                { Kind = PeerKind.Cli; Name = "morphir"; Version = "0.4.0" },
                null
            )

        let hostRun = host.RunAsync CancellationToken.None
        let! session = host.InitializeAsync CancellationToken.None
        let! ping = host.PingAsync CancellationToken.None
        let! _ = host.ShutdownAsync CancellationToken.None
        do! host.ExitAsync()
        let! guestEnd = guestRun
        toHost.Writer.Complete()
        let! _ = hostRun
        return session, ping, guestEnd
    }
```

## Add a method

1. Check that the pinned contract has the method and its params and result types. Do not
   add a method, member or error code that the pin does not have; raise an upstream issue.
2. Write a failing wire test, then add the types to `Wire.fs` with the attributes that the
   contract's wire rules need. Add the type to the contract test (`ContractTests.fs`):
   a complete sample, and the members that the contract text lets a reader default.
3. Add the method to `Method` if it is new, and its capability to `Lifecycle.capability`
   if the contract makes it depend on one (the guest replies `-32013` without it).
4. Register a handler in the guest's `Methods` with `Handlers.request` (params bind as one
   object through their codec; absent or mismatched params give `-32602`), and add a typed
   call to `HostSession` if hosts send it.
5. Add the method to the AOT probe (`tests/Morphir.Mep.AotProbe`) when it changes what
   C# calls.

## The pin

[`tests/Fixtures/Mep`](../../tests/Fixtures/Mep/PROVENANCE.md) pins the upstream
contract model (`mep.ir.json`), the transcripts and the model-transport corpus by commit
and digest. Do not edit the fixtures. To update the pin:

1. Copy the new files from `finos/morphir` at one commit, and record the commit and the
   SHA-256 digests in `PROVENANCE.md`.
2. Run the tests. `ContractTests` fails for each renamed or added member, and the
   transcript and corpus tests fail for each changed behavior.
3. Change the types and rules in the same pull request. A new MEP version moves every MEP
   part in morphir-dotnet together; drafts match only exactly.

Where a transcript and the contract text disagree, the tests follow the contract text.
The legacy transcripts initialize with `0.1`; the replays use `0.2.0-draft.1` instead.

## C# callers

The project is trimmable and AOT-compatible, with F# nullness metadata. Parsed values have
`TryParse` and `Create`; `FrameRead` has `Match`. `tests/Morphir.Mep.AotProbe` is a C#
consumer that runs the full session; `InteropTests` compiles C# examples and checks the
nullable-flow diagnostics.

## Tests

```sh
dotnet run --project tests/Morphir.Mep.Tests -- --summary
./build.sh --target TestMepAot --configuration Release
```

The first command runs the wire, contract, JSON-RPC, connection, session and
model-transport tests on in-memory streams. The second publishes the probe with Native
AOT, refuses IL diagnostics other than the FSharp.Core summary baseline, and compares
the native output with JIT byte for byte. Both run in PR CI.
