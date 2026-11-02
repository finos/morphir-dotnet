---
title: Generate
description: Generate target artifacts from a Morphir model
---

# Generate

## Publication and ownership

Generated files are published through the shared tree publisher. The output
directory contains `.morphir-generated.json`, an ownership manifest listing
artifact paths in declared compilation order. Repeat generation leaves
byte-identical files and the manifest untouched. Reports distinguish written,
unchanged and removed paths.

Files listed by the previous manifest are removed when no longer generated.
Files outside that ownership list remain untouched. Keep the manifest alongside
the output so later runs can identify stale owned files.

Input and output paths must be separate. Rooted artifact paths, traversal, case
collisions and links are refused before publication. Generation errors preserve
previous output. A failure while staging also preserves previous files and the
ownership manifest.

Publication does not provide directory-wide rollback. A failure while committing
staged files can leave a mix of old and new files. Such failures return an
operational error; inspect the output and rerun generation after fixing the
reported filesystem problem.
