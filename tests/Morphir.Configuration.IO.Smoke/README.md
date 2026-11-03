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
Portable Windows inputs also exercise machine sources on another drive and a
redirected UNC share through separate authorized hosts.

The host also exercises configuration editing outside the CLI. It patches comments,
BOM/CRLF text, inline fields and typed complete composites, preserves signed zero,
and proves identical edits and unrepresentable nulls do not change bytes. A
read-only in-memory transaction host throws if preview attempts any write or
writer coordination. Native checks cover YAML-to-TOML promotion, inherited-value
origins after unset, no-op timestamps, explicit YAML refusal, source and absent
alternate-primary preconditions, staging/install/rollback faults and external
writer conflicts.

Incomplete recovery must return owned backup/journal metadata and preserve the
uncertain installed bytes. The host inspects the durable journal and verifies the candidate and backup before
calling the explicit shared recovery API. It checks restored bytes and owned-artifact cleanup. If a check
fails while recovery remains pending, the host retains that temporary directory
and prints its location to stderr. These checks qualify per-file installation;
they make no multi-file group-atomicity claim.

On macOS use a physical campaign temporary directory. Run the following publishes
and executions sequentially. The reflection serialization switch stays disabled
in all three modes:

```sh
mkdir -p /private/tmp/morphir-workspace-migration/publish/tmp
export TMPDIR=/private/tmp/morphir-workspace-migration/publish/tmp
export TEMP="$TMPDIR" TMP="$TMPDIR"

DOTNET_PROCESSOR_COUNT=2 dotnet publish tests/Morphir.Configuration.IO.Smoke \
  -c Release -r osx-arm64 -m:1 -p:BuildInParallel=false \
  -p:PublishTrimmed=false -p:PublishAot=false \
  -p:JsonSerializerIsReflectionEnabledByDefault=false \
  -o /private/tmp/morphir-workspace-migration/publish/managed
/private/tmp/morphir-workspace-migration/publish/managed/Morphir.Configuration.IO.Smoke \
  --fixtures tests/Fixtures/Configuration

DOTNET_PROCESSOR_COUNT=2 dotnet publish tests/Morphir.Configuration.IO.Smoke \
  -c Release -r osx-arm64 -m:1 -p:BuildInParallel=false \
  -p:PublishTrimmed=true -p:ILLinkTreatWarningsAsErrors=false \
  -p:JsonSerializerIsReflectionEnabledByDefault=false \
  -o /private/tmp/morphir-workspace-migration/publish/trimmed
/private/tmp/morphir-workspace-migration/publish/trimmed/Morphir.Configuration.IO.Smoke \
  --fixtures tests/Fixtures/Configuration

DOTNET_PROCESSOR_COUNT=2 dotnet publish tests/Morphir.Configuration.IO.Smoke \
  -c Release -r osx-arm64 -m:1 -p:BuildInParallel=false \
  -p:PublishAot=true -p:PublishTrimmed=true \
  -p:ILLinkTreatWarningsAsErrors=false -p:TrimmerSingleWarn=false \
  -p:JsonSerializerIsReflectionEnabledByDefault=false \
  -o /private/tmp/morphir-workspace-migration/publish/native
/private/tmp/morphir-workspace-migration/publish/native/Morphir.Configuration.IO.Smoke \
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

## Editing managed checkpoint

The local development build passes with zero warnings/errors. Its executable
passes all 44 new editing checks: 9 editor, 22 shared planning/application and
13 transaction checks. The same invocation retains all 228 grammar checks and
26 pinned Morphir documents. Evidence is under
`/private/tmp/morphir-workspace-migration/publish/managed-build.log` and
`managed-run.log`. This checkpoint is a managed build/run; published managed,
trimmed and native results belong in the campaign delivery journal after running
the commands above.

Personal target qualification includes mixed primary layouts and explicit personal
files in another declared member, with retained workspace ownership under isolation.

## Classic migration qualification

The host also runs single-project migration outside the CLI in every published
mode. It checks all three TOML layouts through a read-only tree, safe structured
plans, exclusion of personal sources, member base context, source retention,
verified application, no-op timestamps, workspace drift, raw loss and IR-input
rejection. The JSON receipt includes `migration_checks`. It also links the actual CLI report
DTO sources and exercises their generated serializer with reflection disabled,
checking that canonical secrets never enter the report.

For campaign 5 use `/private/tmp/morphir-project-migration/` as the temporary and
publish root in the commands above. Keep publishes serial, use two logical
processors and retain the existing explicit dependency-warning qualification.
The campaign delivery journal records actual commands and results. Preserve any
reported recovery directory until its artifacts are resolved.

## Workspace migration qualification

The host adds batch planning, application, repeated-run byte/timestamp checks and
explicit durable inspection/recovery outside the CLI. It uses the same bounded
inventory and project verification services as the CLI, including a hidden primary
anchor and common proposed base workspace. It verifies raw secret redaction using
the actual linked-source generated workspace report with reflection serialization
disabled. The JSON receipt adds `workspace_migration_checks`.

A deliberate interrupted transaction must block a subsequent workspace preview.
The host explicitly recovers its journal, proves originals restored and verifies
that no pending state or owned artifacts remain. If recovery cannot be proved,
it retains the temporary tree and reports its physical path on stderr. The
workspace delivery journal records the managed, trimmed and native receipts.
