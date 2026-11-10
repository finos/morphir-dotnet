---
title: "F# generation"
linkTitle: "F#"
description: "Generate F# source from a Morphir library model, built in or as a MEP extension."
---

The F# generator (`Morphir.CodeGeneration.FSharp`) turns a Morphir library
model into F# source files, one file for each module. To generate F# from the
command line, see [Generate F# from a Morphir model]({{< relref "/docs/getting-started/generate-fsharp" >}})
and the [generate command]({{< relref "/docs/cli/generate" >}}).

## Status

The generator has two deliveries. Both run the same generator code.

- **Built in.** `morphir.fsharp` is the backend of the `morphir` and
  `dotnet-morphir` hosts. It is the default: `morphir generate --target fsharp`
  always uses it. It reads JSON and Ion models. The managed host
  `dotnet-morphir` formats the generated source with Fantomas. The Native AOT
  `morphir` cannot run Fantomas, so it writes the same declarations
  unformatted: its files differ from the extension's in formatting.
- **MEP extension.** `morphir-fsharp-backend` is a Morphir Extension Protocol
  (MEP) `0.2.0-draft.1` guest with its own extension id,
  `org.finos.morphir.fsharp-backend`. It is for hosts that do not have the
  built-in backend, for example a MoonBit host. See
  [Run the generator as a MEP extension](#run-the-generator-as-a-mep-extension).

The morphir-dotnet hosts do not start the extension yet. `morphir generate`
has no option that selects an external backend, so installing the extension
does not change what `morphir generate` does.

## Run the generator as a MEP extension

Install the guest as a dotnet tool. It is framework dependent, so it needs the
.NET 10 runtime.

```sh
dotnet tool install --global Morphir.CodeGeneration.FSharp.Extension --prerelease
```

The guest speaks only MEP `0.2.0-draft.1`, over stdio with `Content-Length`
framing. A host selects it explicitly: it starts the `morphir-fsharp-backend`
executable, initializes a session, and sends `morphir.backend.generateModel`
with the target `fsharp`. The guest claims:

| Claim | Value |
| --- | --- |
| Claims version | `0.1.0-draft.2` |
| Extension id and type | `org.finos.morphir.fsharp-backend`, `backend` |
| Backend target | `fsharp`, for IR `4.0.0` and `3.0.0` |
| Legacy `morphir.backend.generate` | no |
| Model tuples | `morphir.backend.generateModel`, profile `morphir-ir`, codec `morphir-json`, envelope `0.1.0-draft.1`, IR `4.0.0` and `3.0.0` |
| Guarantees | none |
| Streaming, cancellation, progress | no |

The extension version is the version of its own package, not of a host.

For IR `4.0.0` the model is IR v4 JSON with `formatVersion` 4 or `"4.0.0"`. For
IR `3.0.0` it is classic JSON with `formatVersion` 3. Classic IR versions 1
and 2 and Ion models have no tuple: a host converts them first, or uses the
built-in backend.

The guest refuses a request with `-32602`, and generates nothing, when:

- its input tuple is not a `generateModel` tuple that the session agreed
  (`Model admission refused: unsupported_tuple`);
- the envelope fails admission, for example a digest that does not match
  (`Model admission refused: digest_mismatch`);
- it requires a guarantee (`Model admission refused: unsupported_guarantee`);
- the target is not `fsharp`;
- an option is not the Boolean `optimize`, which the built-in backend also
  accepts and which is true when absent;
- the payload is not JSON IR of the input tuple's IR version.

Without an agreed `generateModel` tuple, the request is `-32013`. A model that
the generator rejects, or a model that is not a Library (such as a Specs
model), gives a result with `success: false`, error diagnostics and no
artifacts. A successful result has one text artifact for each generated file,
in the generator's dependency order, the order in which the built-in backend
lists its files. Each path is relative to the output directory, with `/` as the
separator; the guest checks that it has no empty, `.` or `..` segment and no
`\` or `:`, and returns `-32603` instead of an artifact that fails the check.
The guest never writes files; it returns them to the host.

The text is formatted by Fantomas, as in `dotnet-morphir`, so its line endings
are those of the platform the guest runs on: CRLF on Windows, LF elsewhere.

Generation runs beside the session: while a `generateModel` runs, the guest
reads and answers other messages, such as `morphir.ping` and
`morphir.shutdown`. The generator cannot stop part way. The guest checks for a
cancelled session (for example Ctrl+C) before and after it generates, and then
answers `-32800`. It does not claim `cancellation`, so it does not act on
`$/cancelRequest` and serves a request that a host asks to cancel.

The guest makes no trimming or Native AOT claim. It is not published as a
self-contained executable yet.

## How the extension is checked

The tests in `tests/Morphir.CodeGeneration.FSharp.Extension.Tests` start the
guest as a process behind the `Morphir.Mep` host session. They check the claim
set, the negotiated tuples, and that an unagreed tuple is refused and not
served. They compare the guest's files (paths, order and text) with those of
the managed built-in backend, through `FSharpGenerationBackend`, on the same
machine, for these checked-in models: the daily-rate models at IR 3 and IR 4
with default options, and the two-module constant-folding models at IR 3 and
IR 4 with `optimize` true and with `optimize` false. For the constant-folding
models they also check that the two settings give different text. There is no
comparison with the Native AOT `morphir`, whose files are unformatted. Other
tests check malformed params, a model nested just under the agreed `maxDepth`,
generation beside a ping, and the path check. A further test packs and installs
the tool and reads its claims.

The guest is not qualified with the shared compatibility runner of
`finos/morphir` yet: its frozen expected F# files do not exist yet. See
[deviations](https://github.com/finos/morphir-dotnet/blob/main/conformance/deviations.md).
