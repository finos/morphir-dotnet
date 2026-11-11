---
title: Extension installation status
description: What the extension installer currently implements and what remains
---

# Extension installation status

External extension installation is under implementation in
[#558](https://github.com/finos/morphir-dotnet/issues/558). The `extension install`,
selection and removal commands remain stubs. The
[management reference](management.md#extension-commands) describes the current
command status. These library changes are not a released installer.

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
passes 24/24 focused Windows tests that execute byte ownership, integrity,
interrupted-write and concurrency controls. Affected Store 15/15 and RunCopies
5/5 pass, with three Unix-only cases ignored. The public C# JIT probe also passes
its original 15 output cases plus nine staging cases, including ownership,
nullable cleanup, refusal and cancellation controls. New staging Unix and AOT
evidence remain pending. The reviewed hashing dependency was merged normally;
publication, host mapping, manager integration and later safe orphan collection
remain unfinished. This adds no working installation command.

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
