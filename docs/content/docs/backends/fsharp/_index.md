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
  uses it when no backend extension is selected. It reads JSON and Ion models. The managed host
  `dotnet-morphir` formats the generated source with Fantomas. The Native AOT
  `morphir` cannot run Fantomas, so by default it writes the same declarations
  unformatted, says so on stderr, and its files differ from the extension's in
  formatting. With `--option format=false` every host writes the same unformatted
  bytes; see [Unformatted output](#unformatted-output).
- **MEP extension.** `morphir-fsharp-backend` is a Morphir Extension Protocol
  (MEP) `0.2.0-draft.1` guest with its own extension id,
  `org.finos.morphir.fsharp-backend`. It is for hosts that do not have the
  built-in backend, for example a MoonBit host. See
  [Run the generator as a MEP extension](#run-the-generator-as-a-mep-extension).

`morphir generate` runs the extension only when you select it, with
`--extension` or `[codegen.fsharp].extension`; installing it changes nothing
else. Every entry point (`morphir`, the managed `morphir` and `dotnet-morphir`)
has the option:

```sh
morphir generate --input model.json --target fsharp --output gen --extension morphir-fsharp-backend
```

The host starts the guest, agrees the session, sends the model's JSON IR, and
checks and publishes the files that the guest returns. The report names the
extension by its own id, with `"backendTransport": "mep"` and its version. See
[Backend extensions]({{< relref "/docs/cli/generate#backend-extensions" >}})
for the configuration key, how the value is read, and the failure codes.

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
- an option is not the Boolean `optimize` or `format`, which the built-in
  backend also accepts and which are true when absent;
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

By default the text is formatted by Fantomas, as in `dotnet-morphir`, so its line
endings are those of the platform the guest runs on: CRLF on Windows, LF
elsewhere. With the option `format` false, the guest writes the generator's own
text, byte-identical to the built-in backend with `format=false`.

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

The host side, `MepGenerationBackend` in `Morphir.Tooling`, is checked against
the real guest with the same comparison, and against scripted guests for each
failure code. `morphir generate --extension` and `[codegen.fsharp].extension`
are checked through the command line. Because the files come from the guest,
the Native AOT `morphir` with `--extension` publishes the guest's formatted
files, not the unformatted files of its built-in backend.

## Unformatted output

`--option format=false` turns Fantomas formatting off, for the built-in backend
and for the extension alike. The files are the generator's own text:

- LF line endings only, and exactly one final LF;
- UTF-8 without a byte order mark;
- the same bytes on every operating system, under JIT and Native AOT, and in
  every current culture.

For the built-in backend the generate report says `"formatting": "off"`; the report of
the extension has no `formatting` member, because the host cannot observe what
the guest did. The default formats when the
host can run Fantomas (`"applied"`); the Native AOT `morphir` cannot, so under
the default it writes the same unformatted text, reports `"unavailable"` and
warns on stderr. Formatting the generated files with a tool of your choice
around `generate` is planned separately.

The Morphir compatibility check compares this unformatted set. For the
finos/morphir case `fsharp-compile-generate`, the built-in backend (JIT host,
Native AOT host and `dotnet-morphir`) and the extension (driven by the JIT and
by the Native AOT host) give the same single file, `Pricing.fs`, 112 bytes.
The test project pins those bytes, and its compile step (the case's F# source to
its expected IR) runs through the compatibility kit.

The case's expected generated file is frozen at finos/morphir `9e1f853b7`: its
`case.json` has `"status": "frozen"` and `"formatting": "off"`, and names
`expected/generated/Pricing.fs` with those 112 bytes. This repository's pin moves
to that commit separately; until then the test compares the size and SHA-256 of
the frozen file, and once the pin has the file, its bytes. The guest is not
qualified with the shared compatibility runner of `finos/morphir` yet, because the
runner cannot observe generated files yet. See
[deviations](https://github.com/finos/morphir-dotnet/blob/main/conformance/deviations.md).
