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
ignored. Independent re-review, Unix runtime evidence and Native AOT are still
pending. These results do not constitute security approval of a released installer.

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
