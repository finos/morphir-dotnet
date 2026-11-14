---
title: Extension installation status
description: What the extension installer currently implements and what remains
---

# Extension installation status

`extension install` installs through source providers. The `github` provider installs
a package from a GitHub release (`github:<owner>/<repo>[@<version-or-tag>]`). The local
provider installs an explicit local-developer release declaration and archive from a
registered repository name. Both need `--no-probe` consent and support a mutation-free
`--dry-run`. The installer writes and reads only the extension family
`0.1.0-preview.2`; it does not read or convert preview.1 state. The obsolete
positional URL/version form is a usage error. Selection and removal commands remain
stubs. The [management reference](management.md#extension-install) gives the command
syntax and the GitHub release convention. A digest match is not publisher
authentication.

## Historical implementation evidence

The following snapshots retain their original source identities and limitations;
they do not describe the current command surface.

The current library increment on `ad24ce68` can install a release-bound local ZIP
into its canonical package tree, register it through the published catalog/lock
transaction, and capture immutable selected records with an owned package lease.
Local no-probe installation is explicitly named `InstallLocalUncheckedAsync` and
stays labelled local-developer/unchecked. Supported interrupted journals can be
recovered with explicit consent; old formats are not converted.

Unpublished snapshots remain expected-current inventory. They cannot authorize a
new acquisition after current metadata changes. Retained-generation acquisition
requires a supported published binding, which is not implemented by this increment.
No new CLI command or generation metadata format is provided here.

The review correction refuses a foreign process-platform activation before it
returns ownership or registers local metadata. Windows x64 emulation remains
supported. Malformed compressed ZIP content returns a typed archive refusal.
Contended state/repository operations return asynchronously and cancellation waits
for native lock ownership to settle rather than abandoning a waiter.

Fresh Windows installer assembly runs before and after formatting each pass 285
executed tests, with seven Unix controls ignored and no failures or errors. These
include transaction interruptions, journal replacement, empty ZIP directories,
exact current-export refusal, native cancellation and real cross-process leases.
The existing C# consumer also passes its 27-line JIT run; it does not cover the new
package APIs or establish AOT support. Tar packages, repair, full corpus checks,
commands and the actual process-provider journey remain unfinished. Independent
security/interop re-review, package C# consumer coverage, Unix runtime and Native
AOT qualification are still gates. Parent composition contracts are merged at
`d9f309a1`; their runtime binding remains unimplemented here.
The earlier 194-test snapshot and evidence below keep their original source identities.
Independent review requested security corrections to installation I/O. The fixes
now pass 131 executed Windows tests; three Unix-only interleavings were explicitly
ignored. Independent security/interop re-review of `b6936ba5` found no concrete
remaining finding under the approved cooperating Home boundary. Unix runtime
evidence and Native AOT remain qualification gates. These results do not qualify
a released installer.

Subsequent macOS CI at `4c908047` ran 180 tests, with 177 passing, two failing and
one error in file permissions/reopening and temporary-directory disposal. A
Darwin ARM64 native-call correction and permission regressions are now under
review. Focused Windows verification passes Store 15/15 and RunCopies 5/5, with
three Unix-only tests ignored and Unix permission assertions unexecuted. The
correction subsequently passed the installer assembly on hosted macOS and Ubuntu
at exact head `749dd2fe`: 182/182 each, with zero ignored, failed or errored. That
closes the ABI platform-evidence gap. Native AOT, acquired artifacts and the full
installation lifecycle remain unqualified.

Private streaming stages are the next test-first library draft. After separate
missing-API and setup diagnostics, a fresh build succeeded and 16 tests ran.
All 16 errored at the declaration stub's intentional refusal; zero passed or were
ignored. No staging mechanism executed, and no behavior or public API approval
is established by that earlier stub run. The Begin/Write/Seal implementation now
passes 25/25 focused Windows tests that execute byte ownership, integrity,
interrupted-write and concurrency controls, including cancellation while waiting
for creation admission. Affected Store 15/15 and RunCopies
5/5 pass, with three Unix-only cases ignored. The public C# JIT probe also passes
its original 15 output cases plus nine staging cases, including ownership,
nullable cleanup, refusal and cancellation controls. Hosted macOS and Ubuntu at
`98df4035` then each reported 207 executed, 182 passed and all 25 staging tests
errored, with zero ignored or failed. Begin incorrectly declared a raw Unix file
handle asynchronous. A source correction preserves the open file and Windows
OVERLAPPED behavior while using the Unix handle's actual mode. Focused Windows
verification passes 25 staging, 15 Store and five RunCopies tests before and after
formatting, with three Unix-only tests ignored each time. These Windows results
leave the hosted Unix failure open; Unix staging GREEN and AOT evidence remain
pending. The reviewed hashing dependency was merged normally;
publication, host mapping, manager integration and later safe orphan collection
remain unfinished. This adds no working installation command.

The next increment drafts tests for publishing sealed private bytes through the
existing single-file store. Declaration-only refusal stubs now compile; a focused
Windows run executed 17 cases, all errored, with four Unix-only cases ignored.
No publication mechanism ran in that stub check. A source implementation now
connects sealed ownership to the same store publisher/verifier as the path API.
Two fresh focused Windows builds succeed, each with four existing dependency
warnings and zero errors. Before and after pinned formatting, the permitted
lists each pass 17 sealed-publication, 25 staging, 15 Store and five RunCopies
tests. Seven Unix-only cases are ignored in each run. No working command,
current Unix qualification or AOT qualification follows from these checks.
Independent review found no finding within this sealed-publication scope. The
public C# JIT consumer subsequently passes with 27 output lines, preserving its
original 24 and adding checks for published bytes/digest/length, nullable cleanup
and one-time sealed ownership. Its fresh build has four existing dependency
warnings and zero errors; the run has empty stderr. This remains Windows
development evidence.
Required final verification
includes the actual published bytes and Unix mode, including a macOS clone.
Orphan collection, host mapping and acquisition lifecycle integration remain
unfinished. Prior Windows results do not qualify the newly merged source or
replace hosted Unix evidence. The coordinator separately verified the earlier
`56665f6c` installer suite on hosted macOS and Ubuntu, 207/207 each with zero
ignored, failed or errored. That evidence belongs to that exact earlier source.

The installation library can verify and publish a staged single file to a
content-addressed store, validate ZIP and tar-gzip archives without extracting
them, and clean stale temporary directories owned by this host. It checks a
single file against its recorded SHA-256 while copying and rehashes a stored file
before reuse. A mismatched stored object is refused and left untouched. Archive
inspection checks names, entry types and size, count and expansion limits before
anything can be published.

Catalog and lock integration, package extraction, install-time capability probes,
activation and installation commands remain unfinished. Shared package formats
will use the parent contract and corpus published in finos/morphir#1156 at
`0439c4c0`. Portable pins and generation-overlay document formats still wait on the
separate parent toolchain contract.

The first internal shared-reader increment passes 19 focused Windows tests using
data pinned to that parent commit, including six release/schema cases and two
I-JSON cases. It is not connected to installation commands. The remaining parent
case operations and full claim/package validation are still being implemented.

The internal claims reader additionally passes 27 focused Windows tests for
current-schema claims, recursive metadata/comparison and host requirements. It
shares embedded parsing with the descriptor reader and uses the existing version
comparator library. This reads claims supplied in metadata; install-time probing
and publisher authentication remain separate, unfinished integrations.

The maintainer approved project/workspace pins resolved from local inventory and
`MORPHIR_EXTENSION_OVERLAY` above project pins. Explicit frontend `extension_id`
and `extensions` table `id` fields will select installed identities without
changing existing path-string meaning. CMD-1 keeps `remove` as an `uninstall`
alias and `--platform` only on `extension list`. These commands and selection
integrations are not implemented by this approval.

Checksums establish integrity, not who published an extension. A capability check
is a separate fact too. Naming a local source or explicitly installing staged
files is developer consent, and the result stays labelled `local-developer`.
Automatic unsigned staged installation requires an explicitly configured `auto`
setting; inheriting historical default `auto` from an omitted setting is not
consent. The schema default remains unchanged, and staging integration/tests are
still pending. No library operation in this increment authenticates a publisher.

Morphir Home is user-owned. Supported writers must honor stable locks and detected
tampering must be refused. This model does not isolate an arbitrary malicious
process running as the same user; a separate broker or sandbox is not promised.

Ordinary compilation and generation must not acquire extensions from the network.
Staged-file policy will remain explicit. Later commands that change installation
state will support `--dry-run` before writing anything.

The [technical library notes](https://github.com/finos/morphir-dotnet/blob/main/src/Morphir.Extensions.Installation/README.md)
describe the current APIs, store layout, archive bounds and platform verification
limits. Native AOT qualification and Linux/macOS filesystem runs remain pending.
