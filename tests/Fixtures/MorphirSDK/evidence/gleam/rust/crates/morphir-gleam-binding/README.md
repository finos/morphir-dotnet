# Morphir Gleam extension

The Gleam extension compiles types to Morphir IR v3 and v4, generates Gleam,
and supports incremental compilation through the Morphir Extension Protocol.
Its extension identifier is `morphir-gleam`. The Rust crate is
`morphir-gleam-binding`.

This crate moved from finos/morphir-rust (`crates/morphir-gleam-binding`) to
finos/morphir-gleam (`rust/crates/morphir-gleam-binding`) with its history. Each
imported commit names its original commit in an `Imported-from:` trailer. The
move is planned in the OpenSpec change `move-gleam-extension`. Since `0.4.0`
the WASM build is a core-Wasm guest for the MoonBit Morphir CLI, not an Extism
plugin (see [Core-Wasm guest](#core-wasm-guest)).

The Rust Morphir CLI in finos/morphir links this crate as a native provider.
That CLI is being sunset. The same implementation supports MEP invocation and a
WASM package.

For a Gleam project, select it explicitly with:

```sh
morphir compile --extension morphir-gleam
morphir generate --target gleam
```

The CLI still accepts `morphir-gleam-binding` as a legacy selector. An installed
extension with that exact old id takes precedence over the alias. Existing
installations retain their recorded identity; newly published bundles use
`morphir-gleam`.

The frontend uses the official [`gleam-core`](https://github.com/gleam-lang/gleam/tree/v1.18.1/compiler-core)
parser, pinned to Gleam 1.18.1 (commit `4a83802ca33a8a96227a1b332768725f232f9779`).
An adapter translates its untyped AST into the extension's lowering model; it
does not run Gleam's type inference or compiler backend. Syntax errors and
incomplete editor-recovery nodes are rejected before IR generation. The Cargo
lockfile retains upstream's `ecow` 0.2.6 because its broad dependency bound also
admits an incompatible newer release.

Version 0.3.0 includes breaking changes to the public Rust API: pretty-printer
entry points now return `Result`, and the AST has additional fields and variants
for unsupported syntax. Rust callers must handle rendering errors and update
AST construction and exhaustive matches when upgrading from 0.2.

## Type compatibility

The frontend resolves aliases, generic custom types, opaque types, labelled
constructors, tuples, function types, module imports, import aliases and
unqualified imported types. It checks type names, visibility, argument counts
and circular aliases. Diagnostics include source locations.

Sum types (discriminated unions) use custom types with multiple constructors.
Each constructor retains its name and its own payload types. Nullary variants,
labelled fields, generic payloads and recursive custom types compile and generate
in both IR versions, for example:

```gleam
pub type Outcome(a) {
  Pending
  Succeeded(value: a)
  Failed(message: String, code: Int)
}
```

Gleam records are ADTs with labelled constructor fields, as described in the
[Gleam language tour](https://tour.gleam.run/data-types/records/). The frontend
preserves their constructors, labels and field types. The backend also represents
a closed Morphir record alias as a single-constructor Gleam ADT. That mapping
preserves fields but changes a structural alias into a nominal custom type;
compiling it back therefore produces a custom type. Open extensible records and
anonymous nested structural records are rejected with a diagnostic.

Built-in type references point to the Morphir SDK. Gleam's `Result(success,
error)` reverses the SDK's error/success parameter order. Imports and module
paths are emitted using Gleam spelling, while IR uses canonical Morphir names.

## IR versions and values

Both `3`/`3.0.0` and `4`/`4.0.0` are accepted. Other releases fail explicitly.
The v3 compiler matches Elm-native's type-only contract. Functions and constants
receive `GLEAM_VALUE_SKIPPED` diagnostics. V4 retains the existing Gleam value
lowering; this work does not establish complete Gleam expression semantics or
native evaluation. Requesting `typesOnly` omits values in either version.
Dependencies may supply v3 or v4 interfaces independently of the output version.

Valid syntax that the adapter cannot lower, such as guarded cases, bit arrays,
external function bindings and record updates, remains available for type-only
compilation and produces an error when compiling values. Labelled function
parameters, labelled calls and imported values also report errors until their
metadata and name resolution can be preserved; labelled type declarations remain
supported. List tails and list patterns preserve their structure, and block
bindings lower to scoped IR destructuring. Operator and value-name resolution
still need work; using the official parser does not establish complete value
semantics. See the [IR coverage inventory](IR_COVERAGE.md) for each value,
pattern and type variant, its tests and remaining limitations.

Constant lowering currently accepts explicitly annotated literals and tuples or
lists of those literals. Function-typed or inferred constants, constant
references, constructor values, list tails and concatenation require further
lowering work and report diagnostics.

## Incremental compilation

The extension is stateless. The caller supplies `CompileBaseline` and receives
per-module results with source and public-interface digests. Unchanged modules
reuse their prior IR. A public-interface change recompiles dependents; changes
to documentation or private declarations do not. A failed module can provide
its last good interface to dependents, while a missing usable interface blocks
them. Independent modules still report their results after a failure. The CLI
persists these results in its workspace cache.

Baselines are scoped to the package, exposure list, output version, semantic
options and dependency interfaces. Malformed entries are discarded with a
warning. The per-module baseline payload is wrapped v4 module IR, including for
a v3 distribution, and is private to this extension's versioned cache context.
Changes to the parser or value-lowering semantics invalidate older baselines.

## Core-Wasm guest

The `wasm32-unknown-unknown` build is one core WebAssembly module for the
execution profile `morphir/mep-bytes-v1/v1-draft`, the MoonBit CLI's
`core-wasm-process` route.

- **Imports:** only `morphir_bytes_v1.input_len`, `input_byte` and
  `output_byte`. The module cannot read files, the environment, the clock or
  random numbers.
- **Exports:** `handle` and one unshared memory with a maximum of 4096 pages
  (256 MiB), set in `rust/.cargo/config.toml`.
- **`handle` result:** the exact response length; `0` after `morphir.exit`; `-2`
  after another notification. A body that is not UTF-8 JSON gets a JSON-RPC parse
  error response.
- **Protocol:** MEP `0.2.0-draft.1` with the model tuple `morphir-ir` /
  `morphir-json` / envelope `0.1.0-draft.1` / IR `4.0.0` for
  `morphir.frontend.compileModel` and `morphir.backend.generateModel`. MEP `0.1`
  (`morphir.frontend.compile`, `morphir.backend.generate`) stays available. Ion
  `morphir-model` is not supported yet; an Ion-only offer gets an empty
  agreement.
- **Model envelope:** the payload is the IR v4 document that
  `morphir.frontend.compile` returns. `modelId` is the package name and
  `revision` is `sha256:<payload digest>`.
- **Document trees:** several documents need relative paths, or absolute URIs
  under `sources.root`. Several absolute URIs without a root are refused.

`src/guest.rs` serves one exchange and has native unit tests. `src/core_wasm.rs`
only moves bytes across the imports. The claims that `morphir.extension.describe`
returns are committed in `claims/expected.json`; a release copies them into
`release.json`.

## Verification and packaging

From the repository root, with the Rust tools of the `rust` mise environment:

```sh
MISE_ENV=rust mise install
MISE_ENV=rust mise run rust:fmt     # add --fix to format
MISE_ENV=rust mise run rust:lint
MISE_ENV=rust mise run rust:test
MISE_ENV=rust mise run rust:claims  # add --update after a capability change
MISE_ENV=rust mise run rust:wasm    # build, validate and run the host tests
```

`rust:wasm` builds the release module and runs `tests/core_wasm.rs` on it. That
test is a host for the execution profile: it gives the module only the byte
channel, checks the imports and the memory limit, applies the 10-second call
limit, and compares guest results with native results. Set `MORPHIR_GLEAM_GUEST`
to test another module, for example a downloaded release.

`ring`, a dependency of `gleam-core`, compiles C for `wasm32-unknown-unknown`.
Apple clang cannot do this. On macOS, install LLVM (`brew install llvm`); the
tasks use it automatically. Linux clang works as it is.

## Release

A tag `extension/gleam/v<version>` on `main` starts
`.github/workflows/gleam-extension-release.yml`. The tag version must equal the
crate version. The workflow:

1. checks the tag, the version and that the commit is on `main`;
2. builds the module once on Linux;
3. runs the host tests on that module on Linux, macOS and Windows, and the
   native suite on Linux;
4. assembles the bundle from the tested module with `rust:bundle`, attests the
   module and `release.json` with `actions/attest@v4`, verifies the attestations,
   and creates the GitHub release with `--latest=false`.

A release has exactly these assets:

| Asset | Content |
|---|---|
| `morphir-gleam-<version>.wasm` | the core-Wasm guest |
| `morphir-gleam-<version>.release.json` | descriptor `2.0.0-draft.2`, one artifact with `runtime: "core-wasm"`, the execution profile, and the claims from a live describe |
| `<asset>.sha256` | one `sha256sum` line for each of the two files |

Verify downloaded assets with:

```sh
sha256sum --check ./*.sha256
gh attestation verify morphir-gleam-<version>.wasm --repo finos/morphir-gleam \
  --signer-workflow finos/morphir-gleam/.github/workflows/gleam-extension-release.yml
```

A manual run of the workflow without a tag, or with `publish` off, is a dry run:
it builds, tests and assembles the bundle, and attests and publishes nothing.
Pull requests that touch the Rust half also assemble a dry-run bundle. To build
one locally after `rust:wasm`, run `mise run rust:bundle`.

`extension/gleam/v0.3.0` in finos/morphir-rust is the last Extism release.

When Gleam is installed, verify generated records, aliases and sum types with:

```sh
MORPHIR_TEST_GLEAM="$(command -v gleam)" cargo test -p morphir-gleam-binding \
  --test backend_types generated_records_aliases_and_opaque_types_pass_the_gleam_compiler -- --ignored
MORPHIR_TEST_GLEAM="$(command -v gleam)" cargo test -p morphir-gleam-binding \
  --test sum_types -- --include-ignored
```

The WASM guest uses in-memory output. Native parse-stage filesystem emission
is unavailable in WASM; set `emitParseStage: false` for identical native/guest
results. Explicit fatal parse-stage requests still report failure.
The official parser also runs inside the WASM guest. Parsing does not need
randomness; the guest explicitly returns an unsupported error if an upstream
code path requests entropy.

### Bundle descriptor compatibility

The `0.3.0` bundle from finos/morphir-rust has a version-2 `release.json`.
Publishing it requires Morphir CLI `0.4.0-beta.7` or later. See the
[packaging contract](https://github.com/finos/morphir-rust/blob/main/docs/extensions/capability-claims.md#packaging-wasm-extensions)
for the descriptor shape and provenance rules.
