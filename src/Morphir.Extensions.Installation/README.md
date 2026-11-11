# Extension installation library

This library is the start of extension-model PRD step 3, tracked by
[finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558).
It parses distribution values, checks platform and MEP eligibility, publishes raw
single files, inspects bounded archives and owns temporary run directories.
It does not complete an extension installation yet. The CLI installation commands
remain stubs. See the [user status page](../../docs/content/docs/cli/extension-installation.md).

Shared-family readers are being added against parent `0439c4c0`. The current
internal descriptor reader is an unfinished increment, with no installation or
activation consumer. Its first tests use six named parent release/schema cases,
two I-JSON cases and additional semantic regressions. The copied corpus and seven
schemas are hash-checked offline from test output; their presence does not mean
all 137 identity or 66 package cases have run. Complete recursive metadata,
standalone claims, package semantics and state readers remain open. It does not
call the older MEP/Toolchain claim codecs that convert `claimsVersion` documents.
The first focused Windows `SharedReaders` run passes 19/19 tests, repeated after
pinned formatting. Both I-JSON refusal cases first prove acceptance of a valid
document. This is separate from the earlier 131-test I/O run and does not
establish complete shared-file conformance.

Independent security review of `f6d991bb` requested changes to stage-file identity,
Unix rename-out containment, total gzip expansion/cancellation and run-directory
creation versus sweeping. The earlier Windows 123/123 run and JIT probe are
historical author evidence. Test-first corrections at `b6936ba5` passed independent
security/interop re-review with no concrete remaining finding in the four I/O
sources or their delta under the approved cooperating Home boundary. The
correction's Windows run passed 131 executed tests, with three Unix-only
interleavings explicitly ignored. This is static review plus Windows test
evidence. Linux/macOS native paths and Native AOT remain qualification gates.

Primary API references for the publication and identity mechanisms are
[Windows handle rename](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info),
[Windows file identity](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info),
[Linux descriptor links](https://man7.org/linux/man-pages/man2/link.2.html),
and [Apple descriptor cloning](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/man/man2/clonefile.2).
These define the intended native mechanisms; they are not platform test evidence.

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
file and holds its verified descriptor through publication to
`store/extensions/sha256/<digest>/<filename>` without
replacing an existing name. It enforces the approved 256 MiB single-file limit.
It sets the owner's execute bit on Unix when requested.

Reuse rehashes the existing object. A changed object fails with
`extension.store_object_mismatch` and stays untouched, including when another
writer wins publication. A changed staging input fails with
`extension.digest_mismatch`; its temporary output is removed. The library never
repairs or replaces a corrupt object. It writes no catalog, lock or inventory.

Windows publication renames the open verified handle with replacement disabled.
Directory pins request data/list access and deny deletion while permitting child
writes. Attribute-only access does not engage that sharing protection. Native
calls use internal verbatim paths; returned paths keep their ordinary spelling.
Linux links the kernel's `/proc/self/fd` reference to the verified file, not its
mutable stage name. macOS uses `fclonefileat` from the verified descriptor and
rehashes the clone. Missing procfs or filesystem clone support is an I/O refusal;
there is no vulnerable pathname fallback. Cleanup compares the stage identity
and leaves substituted names untouched in the tested interleaving.

Store and run mutations take a stable private `.morphir-dotnet-io.lock` lease in
Morphir Home. The library never deletes or renames it. Every cooperating .NET
writer must use the same I/O boundary. This is private coordination, distinct
from the future shared state lock and installed-state documents. Unix operations
use descriptor-relative `openat` with `O_NOFOLLOW`. Reopened device/inode ancestry
checks detect renamed-out directories before mutation/publication and check again
after publication.

The maintainer chose a user-owned Morphir Home on 11 November 2026. Supported
writers must honor stable locks and deterministic tampering must be refused.
Advisory locks and identity checks do not atomically stop an arbitrary malicious
same-UID process from renaming a directory between syscalls or replacing a name
after a cleanup check. Such isolation is not promised; no separate broker or
sandbox is required. The caller must provision Morphir Home and its ancestors in
a trusted location. Independent re-review of `b6936ba5` found no concrete remaining
finding under this boundary. Unix runtime verification remains necessary before
task 3.3 is completed.

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
payloads. The bounded gzip stream below `TarReader` charges every decompressed
byte, including headers, metadata, padding and final drains, and checks
cancellation before and after each read. ZIP file content contributes to the same
aggregate budget. Tar file content is already counted and is not charged twice.
Raw PAX/GNU metadata headers contribute to the entry-count limit at the header
boundary, before framework payload allocation. Nested ZIP and tar-gzip
content is inspected with the same bounds.
Inspection output is a read-only summary, not an extraction permission. Later
extraction must validate the captured bytes it actually consumes and finish every
check before publication. There is no extractor or package manifest codec here.

## Run-directory ownership

`RunCopies.CreateAsync` creates `tmp/extensions/morphir-dotnet-run-<random>` and
holds an exclusive `.owner` file. Creation and sweeping share the interprocess
mutation lease until ownership exists. Dispose its `OwnedRunDirectory` only after the
guest process tree has exited. Disposal attempts cleanup. `SweepAsync` takes a
nonwaiting ownership lock before deleting a stale .NET directory. Busy or unsafe
directories are reported as skipped. Disposal checks the original home, directory
and owner identities before cleanup. Other hosts' `morphir-extension-*`
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

Parent group 1 published the seven schemas, normative member tables,
identity/package cases and publication audit in finos/morphir#1156, immutable main
`0439c4c06d8ca6d4a46e41afb226ba0872f9bff8`. That source is the authority for the
next shared-reader tranche. On 11 November root reported HTTP 200 for all seven
production schema URLs with exact `$id` and text matches to parent main. This is
serving evidence, not a claimed raw-byte hash comparison.
Catalog, lock, repository and package readers are still unimplemented here and
will be tested against that corpus. Extension family `0.1.0-preview.1`
matches exactly; it is not renamed or converted when another prerelease label is
valid SemVer.

Multi-file packages follow merged finos/morphir#1151, including VERIFY-B and
LAUNCH-A. Full verification at install and explicit verification, structural
verification at activation, and launch from the shared tree with a process-tree
lease are distinct from a raw single file's verified private run copy. The old
one-executable target-archive recommendation is superseded.

The maintainer approved portable project/workspace pins resolved from local
inventory, the `MORPHIR_EXTENSION_OVERLAY` above project pins, frontend
`extension_id` and `extensions` table `id` fields that preserve path-string
meaning, CMD-1 commands and stable `extension.*` diagnostics on 11 November.
Pin/overlay schemas, member tables, paths and cases remain the parent toolchain
authority's responsibility. This library adopts no unversioned selection format.

Explicit local sources are developer consent and stay labelled `local-developer`.
An explicitly configured staged `auto` setting permits automatic unsigned staged
installation under the narrow WASM-only rule. An absent setting that inherits
historical default `auto` is not consent. Discovery/reporting may retain that
default; effectful staging must preserve whether configuration was explicit. No
schema default or new configuration member is introduced by this decision.
Global file/staged provenance uses a generated one-release local-directory
repository, with true source retained in inventory. These integrations remain
unimplemented. Later mutating command integration needs `--dry-run`. Neither ordinary compilation nor generation may
acquire from the network. Publisher authentication and claim checks remain
separate; a checksum proves integrity only. The manager owns TUF authentication
and repository identity checks; the installer will recheck handed-over digests.

Extraction, probing, inventory, transactions, activation and command reports remain
unfinished. The user status page records that boundary; an installation guide
with working commands still requires the complete pipeline. No WASM runtime is
part of this library work. The full installer and its user and technical
documentation remain the goal.
