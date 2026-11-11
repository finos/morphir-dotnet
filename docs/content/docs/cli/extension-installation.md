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

The installation library can verify and publish a staged single file to a
content-addressed store, validate ZIP and tar-gzip archives without extracting
them, and clean stale temporary directories owned by this host. It checks a
single file against its recorded SHA-256 while copying and rehashes a stored file
before reuse. A mismatched stored object is refused and left untouched. Archive
inspection checks names, entry types and size, count and expansion limits before
anything can be published.

Catalog and lock integration, package extraction, install-time capability probes,
activation and installation commands remain unfinished. Shared package formats
wait on the parent contract's published tables and cases. The remaining selection,
configuration, command and developer-policy choices still need maintainer answers.

Checksums establish integrity, not who published an extension. A capability check
is a separate fact too. No library operation in this increment authenticates a
publisher or grants permission to run an unsigned developer extension.

Ordinary compilation and generation must not acquire extensions from the network.
Staged-file policy will remain explicit. Later commands that change installation
state will support `--dry-run` before writing anything.

The [technical library notes](https://github.com/finos/morphir-dotnet/blob/main/src/Morphir.Extensions.Installation/README.md)
describe the current APIs, store layout, archive bounds and platform verification
limits. Native AOT qualification and Linux/macOS filesystem runs remain pending.
