# Extension installation library

This library is the start of extension-model PRD step 3, tracked by
[finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558).
It parses distribution values, checks platform and MEP eligibility, publishes raw
single files, inspects bounded archives and owns temporary run directories.
It does not complete an extension installation yet. The CLI installation commands
remain stubs. See the [user status page](../../docs/content/docs/cli/extension-installation.md).

`DistributionIdentifiers` reuses `Morphir.Extensions.ExtensionId`,
`Morphir.Toolchain.TargetPath` and `Morphir.Mep.Sha256Digest`. Id validation checks
grammar, length, then the first segment's Windows device name. Built-in ids such as
`morphir.fsharp` stay valid in the registry. External installation parsing refuses
the `morphir.` prefix; it grants no trust to other prefixes.

Declared paths use the existing toolchain portability rules: relative paths with
`/` separators, NFC, no traversal or Windows device components, at most 512 UTF-8
bytes and 240 UTF-16 units. Artifact filenames additionally require one segment.
This is a lexical check. The store also walks opened directories inside Morphir
Home and refuses symlinks and reparse points.

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

## Single-file store

`SingleFileStore.PublishAsync` takes an existing private staging file inside the
supplied Morphir Home, a parsed filename, the recorded digest and an executable
flag. It hashes the same chunks it copies, using the existing toolchain SHA-256
helper, and compares the result with the recorded digest. It flushes a temporary
file before publishing to `store/extensions/sha256/<digest>/<filename>` without
replacing an existing name. It enforces the approved 256 MiB single-file limit.
It sets the owner's execute bit on Unix when requested.

Reuse rehashes the existing object. A changed object fails with
`extension.store_object_mismatch` and stays untouched, including when another
writer wins publication. A changed staging input fails with
`extension.digest_mismatch`; its temporary output is removed. The library never
repairs or replaces a corrupt object. It writes no catalog, lock or inventory.

Windows operations open reparse points themselves and pin parent directories
against write/delete sharing. Native calls use internal verbatim paths for long
paths; returned paths keep their ordinary spelling. Unix operations use directory
descriptors, `openat` with `O_NOFOLLOW`, and no-clobber `linkat`. Morphir Home itself
and its ancestors must be provisioned by the caller in a trusted location. These
operations do not establish the trust of an arbitrary ancestor outside that home.

## Generic archive inspection

`ArchiveReader.InspectAsync` detects ZIP and tar-gzip from bytes, opens the source
without following its final link and validates every entry without extracting or
publishing anything. It checks effective PAX/GNU names, ZIP local/central name
agreement, ASCII portable paths, case aliases, file/directory collisions and entry
types. Only regular files and directories are accepted. Archive modes and owners
are never applied. Gzip holding something other than tar, plain tar and bare WASM
are outside this reader.

The limits come from the approved
[archive spec](../../openspec/changes/add-extension-installation/specs/extension-package-archives/spec.md#requirement-archive-reading-has-recorded-limits),
not from a package layout assumption:

| Bound | Limit |
| --- | --- |
| Archive input and total expanded content, including nested content | 1 GiB each |
| Entries per archive | 1024 |
| Single file or archive entry | 256 MiB |
| PAX/GNU extension header payload | 1 MiB |
| Expansion after the first 1 MiB | 200:1 |

PAX/GNU declared sizes are checked before the framework reader allocates their
payloads. Nested ZIP and tar-gzip content is inspected with the same bounds.
Inspection output is a read-only summary, not an extraction permission. Later
extraction must validate the captured bytes it actually consumes and finish every
check before publication. There is no extractor or package manifest codec here.

## Run-directory ownership

`RunCopies.CreateAsync` creates `tmp/extensions/morphir-dotnet-run-<random>` and
holds an exclusive `.owner` file. Dispose its `OwnedRunDirectory` only after the
guest process tree has exited. Disposal attempts cleanup. `SweepAsync` takes a
nonwaiting ownership lock before deleting a stale .NET directory. Busy or unsafe
directories are reported as skipped. Other hosts' `morphir-extension-*`
directories are untouched. The empty owner file is private lifecycle state, not
installed inventory or shared package metadata. Launch and process-tree lifetime
integration remain unfinished.

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
Windows tests cover actual reparse/symlink refusal and publication races. Linux
and macOS native filesystem paths, including Unix execute bits, require their own
runs. Passing conditional tests on Windows does not exercise those branches.

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

Extraction, probing, inventory, transactions, activation and command reports remain
unfinished. The user status page records that boundary; an installation guide
with working commands still requires the complete pipeline. No WASM runtime is
part of this library work. The full installer and its user and technical
documentation remain the goal.
