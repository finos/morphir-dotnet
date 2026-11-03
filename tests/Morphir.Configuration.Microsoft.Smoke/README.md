# Microsoft configuration host qualification

This non-CLI executable demonstrates `AddMorphirConfiguration` over one resolved
result and over explicit loading options. It verifies complete/empty array
replacement, missing-array presence, scalar/nested values, canonical metadata,
case/separator/prefix/empty-segment collision diagnostics, retained unrelated host values,
replacement boundaries against earlier host providers,
redacted opaque references, and bounded native-tree loading.

`EnableConfigurationBindingGenerator=true` compiles concrete
`Get<CodegenOptions>()` calls through .NET 10's configuration binding generator.
The host also constructs options explicitly from canonical values. JSON output
uses a generated context with reflection serialization disabled. The host
returns 0 and one JSON result when every check passes; diagnostics use stderr.

Use a physical macOS temp directory and keep publish jobs serialized:

```sh
mkdir -p /private/tmp/morphir-config-inspection
export TMPDIR=/private/tmp/morphir-config-inspection
export TEMP="$TMPDIR" TMP="$TMPDIR" DOTNET_PROCESSOR_COUNT=2

dotnet build tests/Morphir.Configuration.Microsoft.Smoke \
  -p:BuildInParallel=false -m:1
dotnet tests/Morphir.Configuration.Microsoft.Smoke/bin/Debug/net10.0/Morphir.Configuration.Microsoft.Smoke.dll

dotnet publish tests/Morphir.Configuration.Microsoft.Smoke \
  -c Release -r osx-arm64 -m:1 -p:BuildInParallel=false \
  -p:PublishAot=false -p:PublishTrimmed=true \
  -p:ILLinkTreatWarningsAsErrors=false -p:TrimmerSingleWarn=false \
  -o /private/tmp/morphir-config-inspection/microsoft-trimmed
/private/tmp/morphir-config-inspection/microsoft-trimmed/Morphir.Configuration.Microsoft.Smoke

dotnet publish tests/Morphir.Configuration.Microsoft.Smoke \
  -c Release -r osx-arm64 -m:1 -p:BuildInParallel=false \
  -p:PublishAot=true -p:PublishTrimmed=true \
  -p:ILLinkTreatWarningsAsErrors=false -p:TrimmerSingleWarn=false \
  -o /private/tmp/morphir-config-inspection/microsoft-native
/private/tmp/morphir-config-inspection/microsoft-native/Morphir.Configuration.Microsoft.Smoke
```

The strict project default keeps linker warnings as errors. Runtime
qualification explicitly retains normal warning severity because pinned
CsToml 1.8.6 and FSharp.Core 11.0.100 emit documented linker/native-analysis
warnings. No diagnostic is suppressed. Existing upstream warnings do not qualify
new adapter, binding or serialization warnings. The campaign delivery journal
records exact commands, counts and publish diagnostics.

Publishing can add incidental RID/ILCompiler entries to dependency lockfiles.
Restore those incidental changes after publishing while retaining the new
adapter/test/smoke dependency additions.

## Qualification checkpoint, 2026-11-03

The adapter was implemented after two recorded red checkpoints:

- Before production behavior, the missing public API failed compilation. Minimal
  API stubs then compiled without warnings; all 13 initial executable
  TUnit/Reqnroll host cases failed.
- The next 22-case run passed 17 and failed five new regressions. Earlier host
  providers revived discarded array indexes and descendants, while empty key
  segments were accepted. Replacement boundaries and explicit empty-segment
  diagnostics made all 22 pass.

After formatting the owned C# files, the adapter test project built with zero
warnings/errors. Its configured executable passed 22/22 with one worker and
produced `/private/tmp/morphir-config-inspection/microsoft-coverage.cobertura.xml`.
The authored adapter source union covers 118/119 lines, 99.16%.

The managed, trimmed and Native AOT executables each return:

```json
{"success":true,"checks":43,"generatedBinding":true,"explicitBinding":true,"nativeTree":true}
```

Each published host has empty stderr. The warning-qualified trimmed publish
retains 44 dependency warning lines; Native AOT retains 47. All warning origins
are pinned CsToml or FSharp.Core, including F# reflection/structured formatting.
No new adapter, configuration-binder or JSON-serialization warning was emitted.
Trimming reports IL2040, IL2055, IL2060, IL2067, IL2070, IL2072, IL2075 and IL2080;
native analysis additionally reports IL3050. No warning suppression was added.
This evidence does not claim a warning-free or strict-linker-errors publish.

Logs and result JSON are under `/private/tmp/morphir-config-inspection/`:
`microsoft-{compile-red,runtime-red,host-edge-red,final-build,final-tests}.log`,
`microsoft-publish-{trimmed,native}.log`, and
`microsoft-{final-managed,trimmed,native}.json`. The prepublish lockfile snapshot
is `microsoft-prepublish-lockfiles.json`. All four RID/ILCompiler-modified
lockfiles were restored to their prepublish bytes, retaining intentional new
adapter dependencies. These temporary files are reproducible qualification
artifacts; repository source remains in the checkout.
