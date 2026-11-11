# Extension installation library

This library is the start of extension-model PRD step 3, tracked by
[finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558).
It currently parses distribution values and checks platform and MEP eligibility.
It does not install an extension yet. The CLI installation commands remain stubs.

`DistributionIdentifiers` reuses `Morphir.Extensions.ExtensionId`,
`Morphir.Toolchain.TargetPath` and `Morphir.Mep.Sha256Digest`. Id validation checks
grammar, length, then the first segment's Windows device name. Built-in ids such as
`morphir.fsharp` stay valid in the registry. External installation parsing refuses
the `morphir.` prefix; it grants no trust to other prefixes.

Declared paths use the existing toolchain portability rules: relative paths with
`/` separators, NFC, no traversal or Windows device components, at most 512 UTF-8
bytes and 240 UTF-16 units. Artifact filenames additionally require one segment.
This is a lexical check. Later I/O code must also check containment through opened
handles and refuse links that escape the repository or Morphir Home.

`DistributionSelection` holds one channel or one exact version. It uses the
toolchain channel parser and bounded canonical `SemVer` reader, backed by the
centrally pinned Semver 3.0.0 library. Null means an absent option; two absent
options select `stable`. Empty text is invalid, and version metadata remains part
of identity. It does not resolve repository histories yet.

`InstallationPlatform` maps the eight existing toolchain platforms to descriptor
triples and v1 index spellings. The six glibc/macOS/Windows rows map both ways.
Musl has a descriptor triple but no v1 index platform; that lookup refuses with
`extension.no_matching_artifact` naming its RID. No operating-system or architecture
alias is guessed.

`MepSupport` evaluates `>=0.2.0-0 <0.3.0` using
`SemVersionRange.ParseNpm` with its default prerelease rule. It reads the exact
implemented set from `Morphir.Mep.Lifecycle.protocolVersions`, currently
`0.2.0-draft.1`. A later draft on the line is "not implemented"; `0.1` or another
line is "not supported". A runnable entry must contain an exact implemented
identity. Supporting the line does not make different drafts interchangeable.

F# callers use `Parse`/`Result`. C# callers use the static `TryParse` methods with
flow-aware outputs, read-only collections and `Match` delegates. C# code must not
inspect F# union tags or consume F# options. The C# probe exercises this boundary
with nullable warnings treated as errors.

## Validation

Build one project at a time, with the coordinated machine slot:

```powershell
$env:DOTNET_PROCESSOR_COUNT = '2'
dotnet build tests/Morphir.Extensions.Installation.Tests/Morphir.Extensions.Installation.Tests.fsproj -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet exec tests/Morphir.Extensions.Installation.Tests/bin/Release/net10.0/Morphir.Extensions.Installation.Tests.dll --sequenced
```

The C# consumer is
`tests/Morphir.Extensions.Installation.AotProbe/Morphir.Extensions.Installation.AotProbe.csproj`.
Its JIT run is development evidence. The manual Nuke target
`TestExtensionInstallationAot` publishes the probe and compares native and JIT
output through the existing helper. No new PR qualification job is added. Native
AOT qualification is outstanding; do not infer it from a JIT build.

## Remaining integration

Parent group 1 in finos/morphir owns the seven schemas, normative member tables,
identity/package cases and publication audit. Catalog, lock, repository and package
readers wait for those published inputs. Extension family `0.1.0-preview.1`
matches exactly; it is not renamed or converted when another prerelease label is
valid SemVer.

Multi-file packages follow merged finos/morphir#1151, including VERIFY-B and
LAUNCH-A. Full verification at install and explicit verification, structural
verification at activation, and launch from the shared tree with a process-tree
lease are distinct from a raw single file's verified private run copy. The old
one-executable target-archive recommendation is superseded.

Level pins, generation overlays, configuration references, commands and developer
policy remain gated on the maintainer's remaining choices. Later mutating command
integration needs `--dry-run`. Neither ordinary compilation nor generation may
acquire from the network. Publisher authentication and claim checks remain
separate; a checksum proves integrity only. The manager owns TUF authentication
and repository identity checks; the installer will recheck handed-over digests.

Store, extraction, probing, transactions, activation, command reports, and the user
installation guide remain unfinished. No WASM runtime is part of this library
work. The full installer and its user and technical documentation remain the goal.
