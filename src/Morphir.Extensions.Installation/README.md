# Extension installation library

This library is the start of extension-model PRD step 3, tracked by
[finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558).
It parses distribution values, checks platform and MEP eligibility, publishes raw
single files, inspects bounded archives and owns temporary run directories.
It does not complete an extension installation yet. The CLI installation commands
remain stubs. See the [user status page](../../docs/content/docs/cli/extension-installation.md).

## Source providers

`Providers.fs` is the seam for every source of extension releases. An
`IExtensionSourceProvider` has a `Scheme`, a `ResolveAsync` that reads the release
descriptor and selects the artifact for the host platform, and a `FetchAsync` that
brings the artifact bytes to a private local file checked against the descriptor
SHA-256. `ExtensionSources.InstallAsync` then calls the one installer
(`InstalledExtensions.InstallPreparedAsync`): preflight, package tree, catalog, lock
and journal. No provider has its own install path.

| Provider | Request | Provenance and source it records |
| --- | --- | --- |
| `LocalRepositoryProvider` | `ExtensionSourceRequest.Local(...)` | `local-directory`, `local-file` |
| `GitHubReleaseProvider` | `github:<owner>/<repo>[@<version-or-tag>]` | `github-release`, `github-release-asset` |

The GitHub provider streams the asset into `PrivateStaging`, which hashes with the
managed BouncyCastle digest, so no OpenSSL hash is used. It reads GitHub JSON with
the reflection-free `Morphir.Toolchain` reader. To add a source, add a request case,
a provider, and the provenance and source variants of the extension family.

## Package tree increment on ad24ce68

The current working-tree increment adds `PackagePlan.Read`, canonical
`PackageManifest.Read`, and tree-only `PackageInstaller` operations. `InstallAsync`
hashes a descriptor-bound ZIP, extracts into an owned private stage outside the
state lock, checks the canonical tree digest, publishes without replacement and
fully verifies the actual destination. Reuse rehashes every file and refuses a
corrupt winner; repair is not implemented. `VerifyAsync` explicitly rehashes an
installed tree. `AcquireAsync` performs VERIFY-B structure/manifest/size/mode
checks and returns an owned shared `PackageActivation` lease. Its consumer must
retain that lifetime until its whole child tree and output pumps settle. This
library does not launch the guest.

The tree and archive digests stay distinct. Trees use
`store/extensions/packages/sha256/<treeSha>/manifest` and `tree/`, with the stable
sibling `<treeSha>.lease`. Shared readers coexist; an exclusive collector cannot
acquire their lease. Publication uses the platform's no-replace directory rename.
The new native branches have Windows test evidence only.

The next working-tree increment adds semantic catalog/lock/index/repositories and
journal readers through `StateDocuments`. They enforce recursive closed-lock
members, open-catalog critical paths, identity/claim/argument/package/provenance
bindings, sorted unique ids and I-JSON integer constraints. Registration writes
one supported journal, then its exact saved lock and catalog bytes, and removes
the journal last. `InstalledState.RecoverAsync` replays a fully validated current
journal only with explicit consent. Old state and unsupported saved documents
refuse before replay; this does not implement old-home backup/doctor repair.
Interrupted supported transactions roll forward through their journal rather
than inventing an incompatible rollback format.

`InstalledExtensions.InstallLocalUncheckedAsync` explicitly skips the guest
probe. It authors the published one-release directory-repository format at the
caller's explicit root, under `.publish.lock`, and records canonical local
provenance plus the exact history digest. It records `claimCheck: unchecked` and
no publisher authentication or readiness. A fully verified package shared lease
is held before the journal commit through capture and consumer ownership.
`CaptureAsync` returns a `CapturedExtension`: immutable `InstalledActivationRecord`
data together with its already-acquired shared package lifetime. The snapshots
preserve unknown optional selected catalog values, exact lock bytes and args,
claims/provenance, archive/tree identities and canonical manifest modes.

An unpublished capture is expected-current inventory, not retained-generation
authority. `CaptureExpectedAsync` and `AcquireCurrentAsync` compare exact current
exports under the state lock, acquire the package lifetime atomically and refuse
changed exports. An already-held A lease stays owned if global B replaces A;
unpublished A cannot acquire a new lifetime afterward. A supported published
generation's full binding is required for retained acquisition independent of
global selection. Parent composition contracts are now merged at
`d9f309a1062b0aeb744dc7f20d51ceec448ec4e6`; their runtime binding is not implemented
here. No generation schema, provisional receipt, token or manager field was added.

The review correction adds process-platform admission before activation ownership
and local registration. It uses process architecture, admitting Windows x64
emulation on ARM64 without changing `Toolchain.Platform.current`. Activation
positives use actual host-platform fixtures; a foreign-platform package may be
stored and explicitly verified but cannot supply an activation lifetime. Malformed
Deflate raises the same typed `extension.unsafe_archive` refusal as archive
inspection after the delivered archive digest has been verified.

Public state/package operations now cross an asynchronous task boundary before
work starts. Waiting state, repositories and local-publication locks use cancellable
async acquisition; production callers do not synchronously wait on nested tasks.
Windows uses an OVERLAPPED `LockFileEx` request, targeted `CancelIoEx`, and awaited
kernel completion before disposing cancellation registration, handle or request
memory. Wait setup precedes the native request. Unix retries nonblocking `flock`
with cancellable awaited delays, leaving no pending kernel waiter. Neither path
promises a hard deadline for an arbitrary filesystem syscall. See Microsoft's
[pending-I/O cancellation rules](https://learn.microsoft.com/en-us/windows/win32/fileio/canceling-pending-i-o-operations)
and [LockFileEx contract](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-lockfileex).

Fresh full installer assembly runs before and after pinned Fantomas 5.2.4 pass
285 executed Windows tests, with seven Unix controls ignored and no failures or
errors. They retain the journal-identity and empty-ZIP-directory repairs, five
actually consumed frozen parent catalog cases, real cross-process leases, and
six named-lock controls. The native cancellation control observes pending 997
then cancelled 995 before task settlement. The unchanged public C# consumer also
builds and passes its existing 27-line JIT run against the same installer DLL.
It covers the earlier single-file/staging API, not the new package surface.
The final builds report four existing dependency-support warnings and zero errors.

This is not full corpus or lifecycle qualification. Tar.gz package extraction,
repair, full fixed archive/rejection-order coverage, remaining platform/path/ADS
controls, complete inventory/commands and published-generation retained authority
remain unfinished. New package C# consumer coverage, independent security/interop
re-review, Unix runtime and Native AOT checks remain gates. No full task checkbox
is completed. The earlier 134- and 194-test reports keep their own source identities.

## Earlier implementation evidence

The notes below preserve earlier slices and their then-open work. Current
implementation limits and evidence are recorded above; earlier results do not
qualify later inputs.
Shared-family readers are being added against parent `0439c4c0`. The current
internal descriptor reader is an unfinished increment, now consumed by the
partial package tree APIs above. Its first tests use six named parent release/schema cases,
two I-JSON cases and additional semantic regressions. The copied corpus and seven
schemas are hash-checked offline from test output; their presence does not mean
all 137 identity or 66 package cases have run. Complete package semantics, full state-corpus execution,
public claim-reader boundaries and probe integration remain open. It does not
call the older MEP/Toolchain claim codecs that convert `claimsVersion` documents.
The first focused Windows `SharedReaders` run passes 19/19 tests, repeated after
pinned formatting. Both I-JSON refusal cases first prove acceptance of a valid
document. This is separate from the earlier 131-test I/O run and does not
establish complete shared-file conformance.

The internal `Claims` reader now handles standalone claims at the exact family
schema URL and embedded claims without a schema/version member. It retains the
supplied JSON beside existing MEP identity/protocol values and a projection of
understood members. Comparisons ignore object member order and unknown optional
members recursively, preserve array order, and equate absent `critical` with an
empty array. Known optional metadata is type-checked even when its capability
kind is not declared. Host requirements reuse `Toolchain.Comparator.tryParse`
and `Comparator.allMet`, backed by the established Semver library. None of these
facts establishes publisher authentication or that an artifact was probed.

Claims runtime RED recorded 27 failures. The focused Windows GREEN passes 27/27;
the affected SharedReaders 19/19 also passes, with both repeated after formatting.
These include positive and negative equivalence/host controls. Public MEP and
Toolchain codecs are unchanged. Install-time probing and full parent-corpus
execution remain unfinished.

Independent security review of `f6d991bb` requested changes to stage-file identity,
Unix rename-out containment, total gzip expansion/cancellation and run-directory
creation versus sweeping. The earlier Windows 123/123 run and JIT probe are
historical author evidence. Test-first corrections at `b6936ba5` passed independent
security/interop re-review with no concrete remaining finding in the four I/O
sources or their delta under the approved cooperating Home boundary. The
correction's Windows run passed 131 executed tests, with three Unix-only
interleavings explicitly ignored. This is static review plus Windows test
evidence. Linux/macOS native paths and Native AOT remain qualification gates.

Hosted macOS CI at `4c908047` subsequently ran all 180 installer tests: 177 passed,
two failed and one errored. Disposal, coordination-file reopening and tampered
object rewriting exposed the Darwin ARM64 variadic `openat` binding. Both native
`open` and `openat` now dispatch to stack-mode bindings on Darwin ARM64; Darwin
x64 and Linux x64/ARM64 retain their fixed-position conventions. Unsupported ABIs
refuse explicitly. No-follow/exclusive flags and descriptor identity are unchanged.
The correction adds private-mode and repeated reopen/disposal regressions.
Focused Windows Store 15/15 and RunCopies 5/5 passed before and after formatting,
with three existing Unix-only tests ignored. The new Unix permission assertions
did not execute on Windows. Subsequent hosted Development run `43029092889` at
exact head `749dd2fee3b6573a2ad874635c832fab1f4e2389` executed the installer
assembly on macOS job `130551180282` and Ubuntu job `130551179939`: each passed
182/182 with zero ignored, failed or errored. Those runs close the ABI platform
evidence gap, including the three Unix interleavings. They do not qualify minimum
OS versions, acquired artifacts, Native AOT or the full installer lifecycle.

The ABI correction follows Apple's
[ARM64 calling convention](https://developer.apple.com/documentation/xcode/writing-arm64-code-for-apple-platforms)
and [variadic open wrappers](https://github.com/apple-oss-distributions/xnu/blob/main/libsyscall/wrappers/open-base.c),
corroborated by the first-party
[.NET macOS binding](https://source.dot.net/Aspire.Tray.Tests/src/Aspire.Tray/Mac/MacTrayLog.cs.html).

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

The sealed-publication increment now has focused Windows development evidence.
A separate `SealedPublication` list in the existing Store test
module contains 21 cases, including four Unix-only controls. Existing Store,
PrivateStaging, RunCopies and public C# consumer cases are preserved. A bounded
RED burst first captured 25 compile diagnostics: the absent overload/seam and
test inference errors. Declaration-only refusal stubs and explicit test-local
types then compiled, four existing dependency TFM warnings and zero errors.
The exact list ran once: 17 executed, zero passed/failed, four ignored and 17
errored. Sixteen errors are NotImplementedException; the concurrency control's
ten-second wait timed out because the stub never invokes its pause hook. The
admitted task was observed during awaited test cleanup. This is missing-behavior
evidence only, not commit, tamper, ownership or cleanup mechanism coverage.
The proposed overload takes a sealed owner,
parsed filename, executable policy and cancellation token; captured Home, digest
and exact length come from the owner. Its implementation must share today's
SingleFileStore verifier and SafeDirectory.Publish, with serialized ownership
transfer and actual commit-state observation. No second store or checker is
proposed.

Draft controls cover verified final bytes/mode, path/sealed reuse, no-clobber
races, changed/truncated/appended open bytes, identity replacement, cancellation
before/after actual commit, duplicate/concurrent publication, disposal joining
and observable closed-orphan cleanup deferral. The Darwin control mutates the
actual clone result before final verification. A visible object that fails
required verification is proposed to remain visible with a refusal identifying
its path, without a receipt, repair or successful publication claim. This test
contract still needs mechanism review. Those earlier declarations always returned
faulted NotImplemented tasks without I/O, hook invocation or ownership transfer.
The current source replaces them with one shared publisher/verifier for the
path-copy and sealed-owner APIs. Its first and formatted Windows runs each pass
17/17 SealedPublication tests, with four Unix-only cases ignored. This does not
establish final API approval or Unix qualification.

The draft admits one sealed publisher in the existing phase machine. Duplicate
or overlapping admission refuses; disposal joins admitted publication, while the
old writable wrapper cannot close its transferred owner. Publication validates
captured Home/source identity, takes the existing private lease and rehashes the
same open source after the awaited before-commit hook. Reuse, racing winners and
new objects share final digest/length/identity/launch-mode verification. There is
no second hash, store or native implementation. The extension cap is applied
only by SingleFileStore; generic staging retains its caller-supplied bound.

A minimal SafeDirectory callback records successful native visibility before
postcommit location validation can throw. The after-commit hook runs only there.
After a successful commit, final verification observes the actual named object
with a noncancelled token, including the new Darwin clone inode. A failure returns
a typed refusal identifying the committed path; it leaves the visible object
without returning a StoredArtifact, repairing it or claiming rollback. Late
cancellation cannot fabricate a failed commit. Windows source handles retain
DELETE access through rename, then close before the final readonly open, whose
identity must still match the captured source identity. This ordering follows
[Windows sharing rules](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew),
not a pathname fallback for publication.

The publishing lease ends before private cleanup. All retained ownership closes
before the existing cooperative deletion attempt, with CleanupReport exposing
deferred physical cleanup. Primary precommit cancellation propagates after
awaited release. No native operation is detached and no hard kernel deadline is
promised. All 21 test cases and existing guards remain unchanged from the recorded
RED source apart from pinned formatting. Two fresh Release no-restore builds each
succeeded with four existing dependency TFM warnings and zero errors. Before and
after Fantomas 5.2.4 formatting, the exact lists each pass SealedPublication 17/17,
PrivateStaging 25/25, Store 15/15 and RunCopies 5/5. Seven Unix-only cases are
ignored in each run. No compile or behavior correction was needed. All 90 locks
and protected test/project/C# sources remain unchanged. Safe orphan collection
and host mapping remain unimplemented.

Independent review `6e79` found no security, correctness or interop finding in
this sealed-publication source. It checked the frozen source hashes and Windows
62/62 results twice, with seven Unix-only cases ignored each time. A subsequent
public C# consumer appends three cases to the original 24-case source prefix.
Its fresh Release no-restore build succeeds with four existing dependency
warnings and zero errors. One JIT run emits exactly 27 lines with empty stderr;
the original 24 lines are unchanged. The new cases check actual published
bytes/digest/length, nullable cleanup observations and consumed-owner refusal
through the public sealed `PublishAsync` overload. This adds Windows C# consumer
evidence, not current Unix, Darwin clone or Native AOT qualification.

The cohesive intermediate checkpoint uses the maintainer's sequencing exception.
Local full-suite, coverage, full-format, changelog and repository Precommit
checks are omitted. The actual Git hooks directory has no installed hooks, so
ordinary commit/push runs without a bypass. Final exact-head CI, security,
platform and review gates remain required. No full task is completed by this
checkpoint.

The checkout normally merged reviewed main `b2b24b81d7f622b118275881e1868520bebec66b`
as `af7ac67f7372d4321c94ec25a4a74c713d3d8526`, retaining installer head `56665f6c`
and acquisition ancestry. The merge preserves the security/bootstrap changes and
both changelog histories without conflicts. This merge remains unpushed; its
targeted declaration/stub evidence above does not qualify the full merged source.
Prior Windows evidence belongs to the exact `56665f6c` source. The coordinator
subsequently inspected full hosted logs at that head and reported macOS and
Ubuntu each ran/passed 207 installer tests with zero ignored, failed or errored.
Those raw logs are retained separately by the coordinator; they do not qualify
this later merge or sealed-publication draft. Safe orphan sweep,
host publication mapping, acquisition composition and the full lifecycle remain
unfinished.

Private asynchronous byte staging now has an implementation draft of
Begin/Write/Seal. Earlier missing-API/setup logs are retained separately from the
first runtime-stub RED: 16 executed, all 16 errored at intentional Begin refusal,
zero passed, ignored or failed. No staging mechanism executed in that run. The
initial implementation passed the focused Windows PrivateStaging list: 24
executed, 24 passed, zero ignored/failed/errored. These tests execute real owned
stream writes, flushed partial-prefix cancellation/error, one-time ownership
transfer, same-object tamper checks, disposal joins, interprocess lease access
while writes pause, and observable closed-orphan deferral. Affected Store passes
15/15 with one Unix-only test ignored, and RunCopies passes 5/5 with two Unix-only
tests ignored. No new staging Unix branch or AOT run is established. The subsequent
public C# JIT probe preserves its original 15 output cases and appends nine staging
cases, for 24 deterministic lines. It verifies sealed length/digest and ownership,
nullable cleanup/Failure consumption, null Home/digest and caller-bound refusals,
short/wrong-digest seals and pre-cancelled Begin preserving its token. Two
deliberately unsafe accesses produced CS8602 for CleanupReport and Failure;
the guarded consumer then built and ran. Explicit disposal uses IAsyncDisposable.
This was two focused no-restore probe builds and one successful fresh assembly
run, with no production API or mechanism change, native publish or AOT inference.

The lease wait now uses one iterative task instead of recursive task expressions,
so prolonged contention does not retain a parent task per retry. A new control
first passed against the initial implementation: cancelled Begin while the lease
is held creates no stage and preserves the caller token; releasing the lease
permits a verified retry. After the refactor and pinned formatting, PrivateStaging
passes 25/25, Store 15/15 and RunCopies 5/5, with the same three Unix-only cases
ignored. The freshly rebuilt public C# probe still emits all 24 lines. This is
functional regression evidence, not a measured heap-growth reproduction or new
Unix/AOT qualification. Independent review remains required before integration.

Hosted Unix execution at exact `98df40356676d4facdd358e43effa9fad795af43`
subsequently found a real staging failure. Both full Run tests logs report 207
executed, 182 passed, zero ignored/failed and 25 errored. All 25 PrivateStaging
cases errored while Begin tried to construct a FileStream with an async flag on
a raw Unix handle whose `IsAsync` metadata is false. The existing 182 tests passed;
that does not qualify staging. A source correction now retains the Windows
OVERLAPPED flag and describes Unix handles truthfully. All 25 staging cases remain;
the successful-write control now crosses the 64 KiB buffer, and the ownership
control checks platform handle metadata. Two focused Windows no-restore builds
now pass, each with four existing dependency TFM warnings and zero errors. Before
and after pinned two-file formatting, PrivateStaging passes 25/25, Store 15/15
with one Unix-only ignored and RunCopies 5/5 with two Unix-only ignored. The
format check passes. Exact-head Unix GREEN remains required; these Windows
results cannot close this failure. No AOT or full-lifecycle qualification follows.

This burst used three focused no-restore builds. The first two recorded tuple
alias/await syntax diagnostics separately; the third succeeded after pinned
Fantomas formatting, with four existing dependency TFM warnings and zero errors.
Nullable observations use the established Option.toObj boundary so the pinned
formatter accepts the syntax. The final four-file format check passed. The three
lists ran once; the build cap left no second rebuild/list repeat. All 90 lock
files and the two owned project registrations were unchanged by verification.
Exact source SHA-256 and full logs are retained in the ignored installer evidence
folder. This is local Windows development evidence, not full qualification.

Reviewed PR #594 head `8f28d5ca7881adeafc5443f1e9069ee1710378b0` was normally
merged as `bb1aaef1900155f15ef4b397f4535ba8b75eec7a`, with every unstaged draft
byte preserved. This supplies `Morphir.Toolchain.Digest.Incremental`; no hash or
native filesystem implementation was copied and there is no installer dependency
on Toolchain.IO. This dependency merge does not activate TUF or durable trust.

The source uses one phase/owner state machine. Overlapping writes refuse while
the admitted write finishes. Failed or cancelled writes and failed seals prohibit
later sealing. Disposal joins active writes/sealing, including failed operations,
before releasing handles. Each awaited write consumes caller memory using bounded
storage and hashes the chunks actually admitted. Begin validates caller length
and limit without selecting an extension cap or destination. Streaming holds no
global mutation lease. Seal flushes, checks captured Home/directory/file identity,
and rehashes that same open object against exact count and digest before ownership
transfers once to the sealed wrapper. Disposing the writable wrapper after that
transfer cannot close the sealed owner's stream.

The FileSafety opening path requests Windows OVERLAPPED only for staging. Raw
Unix openat handles retain synchronous handle metadata. The correction
passes that actual mode to the same-handle FileStream constructor. The .NET 10
[constructor validation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileStream.cs)
requires the flag to match `SafeFileHandle.IsAsync`. Unix uses the same
[stream strategy](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/FileStreamHelpers.Unix.cs)
for either mode; its
[async writes](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/RandomAccess.Unix.cs)
queue awaited thread-pool operations on the original descriptor. No pathname is
reopened or handle metadata overridden. Buffered WriteAsync/FlushAsync, same-open
rehashing, awaited durability and cancellation/disposal ownership stay in place.
Cancellation cannot interrupt an arbitrary kernel syscall already running.
Existing store/run callers keep their prior handle strategy.
Private lease waits retry only native sharing/flock contention, with cancellation;
permission/identity failures refuse. The .NET 10
[async Windows stream strategy](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/AsyncWindowsFileStreamStrategy.cs)
and [stream strategy selection](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/FileStreamHelpers.cs)
were read as API references, not runtime evidence for this new code.

Awaited disposal exposes `CleanupReport` with completed or deferred physical
cleanup. It closes ownership before attempting identity-checked deletion under
the cooperative lease. A busy lease or foreign substitution leaves closed private
bytes and an observable diagnostic, with no detached task or shared journal.
Failed Begin retains a cleanup report in its exception Data under
`Morphir.PrivateStaging.CleanupReport`; typed refusal messages also disclose
physical deferral. A later process's safe orphan collector remains unimplemented
and needs its own substitution/ownership controls. No immediate physical deletion
under failed storage or a busy lease is promised. Native durability/close calls
are awaited to completion; cancellation is not a hard kernel deadline.

Private temporary names and empty owner files are installer internals, not a
shared metadata contract. Public classes have internal constructors, nullable
cleanup observations and Task/ValueTask/IAsyncDisposable ownership; no F# options
or unions cross that boundary. The API has focused C# JIT/nullable evidence;
independent review and native/AOT consumer qualification remain outstanding.

Later Unix verification must execute the sealed-publication draft's Home/name
and Darwin clone controls. Focused Windows runs execute its cancellation, actual
commit and ownership controls alongside the existing store/staging/run-copy
guards. Store.fs remained unchanged in the first two
Begin/Write/Seal slices; the current source increment shares its publisher with
sealed ownership. Read-only reuse, safe orphan sweep, host publication mapping
and manager composition remain unimplemented. Generic staging chooses no
extension layout or 256 MiB policy. Current Unix, independent security/interop
review and Native AOT qualification remain pending.

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

## Package declaration review follow-up

Acquisition and explicit verification now apply the same complete executable
declaration checks as extraction. Every additional executable must exist in the
canonical manifest, and a Windows entry point must end in `.exe`, including when
an otherwise identical manifest permits several executable files. Archive suffix
matching is ASCII case insensitive for ZIP, TAR.GZ and TGZ. Reading a tar.gz plan
does not imply that tar.gz extraction is implemented.

Seven new regressions first reproduced the declaration and suffix gaps. The
corrected full Windows suite passes 292 tests with seven Unix-only tests ignored,
before and after pinned formatting. Package observations also have member XML
documentation and explicit diagnostic `ToString` implementations for C# callers.
An independent security/correctness and interop recheck cleared this repair.
A subsequent fresh C# JIT consumer passes its original 27 output controls and
seven new package/state controls. It uses public package publication, full
verification, owned activation, defensive record exports, expected-current
capture, structured cancellation refusal and replacement admission. Its fixture
is never launched and local registration remains explicitly unchecked.
This source checkpoint establishes no Unix/AOT, installed command,
published-generation, readiness or portable-lock qualification. The maintained
[checkpoint evidence](../../openspec/changes/add-extension-installation/evidence/2026-11-12-package-state-current-activation.md)
records exact source scope and limitations.
