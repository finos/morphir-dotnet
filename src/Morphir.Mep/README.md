# Morphir.Mep

Shared protocol code for Morphir hosts and guests. Work is tracked by
[#512](https://github.com/finos/morphir-dotnet/issues/512) and
[`add-mep-core`](../../openspec/changes/add-mep-core/proposal.md).

## Current foundation

- `Wire.fs` has parsed protocol versions and the first generated message,
  `DescribeParams`. `MepJson` is generated from this file at build time by
  `Morphir.Myriad.SystemTextJson`. There is no reflection serializer.
- `Framing.fs` reads and writes `Content-Length` frames on a `Stream`. Hosts
  and guests use the same code. Header blocks are bounded at 8192 bytes and
  bodies at 64 MiB, before allocating a body. A framing failure is terminal
  for the connection. Clean EOF between frames is a separate outcome.
- Callers own streams, cancel operations through their tokens, and serialize
  writes to each output stream. Framing never starts a process or logs to stdout.

The JSON-RPC dispatcher, connection, session lifecycle, model admission and
remaining wire types are pending. The generator prerequisite landed in
[#532](https://github.com/finos/morphir-dotnet/pull/532); this project uses the
repository generator build. Session negotiation will decide which offered version
is accepted. The wire codec can read full SemVer offers and the frozen legacy
spelling `0.1` without deciding negotiation.

## Contract inputs

[`tests/Fixtures/Mep`](../../tests/Fixtures/Mep/PROVENANCE.md) pins the upstream
contract model, transcripts and model-transport corpus by commit and digest.
Keep the fixtures unchanged. Update the pin as a separate reviewed change and
run the contract tests when it changes. Wire rules are documented on the types
inside `mep.ir.json`, including rules that differ from ordinary record JSON.

New message types belong in `Wire.fs` or a separately declared generated
context. Add a failing wire test before the type. Check missing and null
members, optional-member omission and the pinned transcript examples, as well
as the happy path. New transport behavior needs stream tests independent of
child processes.

## C# callers

```csharp
using System.Text.Json;
using Morphir.Mep;

var version = ProtocolVersion.Create("0.2.0-draft.1");
var parameters = new DescribeParams([version]);
var json = JsonSerializer.Serialize(parameters, MepJson.Default.DescribeParams);
```

`ProtocolVersion.TryParse` accepts nullable untrusted text and gives C# flow-aware
result and failure outputs. `DescribeParams.ProtocolVersions` is exposed as an
`IReadOnlyList<ProtocolVersion>`. For a stream, call
`ContentLengthFraming.ReadAsync(stream, cancellationToken)` and handle all outcomes
with `FrameRead.Match`. Stop the connection on a refusal; do not try to find a new
frame boundary in the remaining bytes. Callers own and dispose their streams.

## Run the foundation tests

```sh
dotnet run --project tests/Morphir.Mep.Tests -- --summary
```

The suite includes a property test of arbitrary frame payloads and uses only
in-memory streams. The repository's `./build.sh --target Test` includes it.

`ContractTests` reads the describe constructor from the pinned IR and checks wire
names and requiredness. A renamed member in a copy of the pin is rejected. The
remaining wire-type inventory and transcript coverage are added with those types.
The same C# consumer runs in the unit-test host on every CI platform.
`InteropTests` also compiles unsafe C# examples and requires nullable-flow and
read-only-indexer diagnostics. Its child build uses the shared test sandbox.

```sh
./build.sh --target TestMepAot --configuration Release
```

`tests/Morphir.Mep.AotProbe` is a C# consumer with reflection serialization disabled.
It checks generated serialization, parser flow, framing, clean EOF and refusal
through the public C# API. The target publishes its dependency closure with Native
AOT, rejects new IL diagnostics and compares native output with JIT byte for byte.
It uses the existing FSharp.Core summary baseline, as `TestMyriadJsonAot` does.
Both probes run in the existing Linux AOT step of PR CI; the unit tests run on all
three CI platforms. A full session AOT probe remains part of the later session slice.

To collect module coverage, including the generated codec:

```sh
dotnet test tests/Morphir.Mep.Tests -c Release --settings tests/Morphir.Mep.Tests/coverage.runsettings --collect:"Code Coverage"
```
