# Development compatibility runner

`morphir-mck` is an experimental development executable. Build it from this checkout with the SDK pinned in `global.json`:

```powershell
dotnet build src/Morphir.Mck/Morphir.Mck.csproj -c Release
& ./src/Morphir.Mck/bin/Release/net10.0/morphir-mck.exe wasm-host run --help
```

On other platforms, use the `morphir-mck` apphost without `.exe`. Keep its complete build output together, including dependencies, runtime configuration and content. This project is not packable and does not add an official product offering or install a tool.

## Crafted worker cases

The development adapter invokes the existing `Morphir.Conformance.WasmHost.WasmHostCommands` runner and report checker. The tested product is the explicit `--host` executable:

```powershell
& ./src/Morphir.Mck/bin/Release/net10.0/morphir-mck.exe wasm-host run `
  --kit conformance/suites/wasm-host --host C:/tools/morphir/morphir.exe `
  --offering native-aot --platform windows-x64 --engine 'wasmtime 50.0.1' `
  --report ./out/wasm-host.json

& ./src/Morphir.Mck/bin/Release/net10.0/morphir-mck.exe wasm-host report-check `
  --kit conformance/suites/wasm-host --report ./out/wasm-host.json `
  --expect-offering native-aot --expect-platform windows-x64
```

Supply the actual offering, executable platform and engine identity. A build on one platform does not qualify another platform, acquired package, minimum OS or release. Existing qualification and citation guards remain enforced by the shared checker. A generic citation remains refused until the authoritative generic checker is integrated.

Commands reserve stdout for results, including one JSON usage document for invalid `wasm-host` arguments. Diagnostics go to stderr. Exit codes are 0 for accepted results, 1 for failed checks and 2 for invalid inputs or operational errors. Use `--help` for the complete current argument set.

## Product forwarding

Both product hosts forward every `mck` argument unchanged to a separate executable before parsing product commands. To select this development build explicitly:

```powershell
$env:MORPHIR_MCK = (Resolve-Path ./src/Morphir.Mck/bin/Release/net10.0/morphir-mck.exe).Path
& ./src/Morphir/bin/Release/net10.0/morphir.exe mck wasm-host run --help
```

The development executable accepts both `wasm-host ...` and the forwarded `mck wasm-host ...` form. An absent configured runner returns product exit code 127. The product does not reference or embed Conformance through this adapter.

## Generic profile work in progress

The shared GuestKit runner now executes the adopted one-session plan in controlled tests: 21 entries for both IR versions, or 16 for one selected version. It consumes the tested MEP operation observations directly, retaining the original received bodies and correlation facts. Controlled worker transcripts verify the outer process/session path. These execution results are not a generic report carrier.

Neither required notification-silence case can pass from a write to a worker or an adapter returning no response. Withheld or unestablished guest delivery remains `NotRun` and prevents qualification. Generic CLI commands, report serialization/checking, four crafted E2E scenario migrations, offering-script runner inputs and real Python evidence remain open in the [implementation tasks](../../../../openspec/changes/adopt-generic-guest-kit/tasks.md).
