# Configuration adapter runtime qualification

This non-CLI host exercises equivalent formats, extended JSON5 and exact
integers, duplicate/error diagnostics, classic normalization/raw retention,
low-priority defaults, lazy profiles, source order and typed-host origin parity.
It also compares complete native/mounted workspace discovery, member selection
and loading results, checks bounded invocation, isolated active profiles and
shared workspace output ownership. With `--fixtures`, it verifies hashes and runs all 228 official JSON5 syntax
checks and 26 pinned Morphir documents through the published executable.
Explicitly selected workspace/member personal files are checked with isolation
on and off; host parity includes the retained ownership restrictions and diagnostics.

On macOS use a physical temporary directory for the filesystem boundary checks:

```sh
mkdir -p /private/tmp/morphir-configuration-smoke
export TMPDIR=/private/tmp/morphir-configuration-smoke
export TEMP="$TMPDIR" TMP="$TMPDIR"
DOTNET_PROCESSOR_COUNT=2 dotnet publish tests/Morphir.Configuration.IO.Smoke \
  -c Release -r osx-arm64 -m:1 -p:BuildInParallel=false \
  -p:PublishTrimmed=true -p:ILLinkTreatWarningsAsErrors=false \
  -o /tmp/morphir-config-trimmed
/tmp/morphir-config-trimmed/Morphir.Configuration.IO.Smoke \
  --fixtures tests/Fixtures/Configuration

DOTNET_PROCESSOR_COUNT=2 dotnet publish tests/Morphir.Configuration.IO.Smoke \
  -c Release -r osx-arm64 -m:1 -p:BuildInParallel=false \
  -p:PublishAot=true -p:PublishTrimmed=true \
  -p:ILLinkTreatWarningsAsErrors=false -p:TrimmerSingleWarn=false \
  -o /tmp/morphir-config-native
/tmp/morphir-config-native/Morphir.Configuration.IO.Smoke \
  --fixtures tests/Fixtures/Configuration
```

## Dependency diagnostics remain visible

The project keeps a strict linker-as-errors default. That strict publish fails
with the repository's pinned CsToml 1.8.6 and FSharp.Core 11.0.100. Runtime
qualification explicitly uses the normal warning severity above and retains
the diagnostics. No linker warnings are suppressed in code or descriptor files.
Successful execution does not claim warning-free dependency analysis.

CsToml's generic fallback resolver emits IL2055/IL2067 and native analysis can
emit IL3050. This adapter always deserializes the closed `TomlDocument` type and
walks nodes directly; it never asks for dynamic dictionary/object binding.
The [pinned serializer source](https://github.com/prozolic/CsToml/blob/6e3b51a9ac9b4bf8f51c9175ffa6f74cd73940ea/src/CsToml/CsTomlSerializer.cs)
has a separate `TomlDocument` branch before generic formatter resolution.

FSharp.Core emits reflection/structured-formatting diagnostics and IL2040 for
resource-removal entries referring to absent package resources. These warnings
remain an upstream qualification limitation. Hosts should consume typed values
and explicit diagnostics; this campaign does not qualify arbitrary reflection
serialization or generic F# pretty-printing under AOT.

The delivery journal records exact commands, results, warning limitations and
checkpoint SHAs. A future dependency update must rerun this host and fixture
suite before claiming equivalent runtime evidence.
