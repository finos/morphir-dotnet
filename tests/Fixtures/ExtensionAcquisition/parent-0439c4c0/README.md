# Published extension-family test data

These are byte-exact Git blobs from [finos/morphir](https://github.com/finos/morphir/tree/0439c4c06d8ca6d4a46e41afb226ba0872f9bff8), commit `0439c4c06d8ca6d4a46e41afb226ba0872f9bff8`.
`manifest.json` records the source repository, path, full commit and SHA-256 of every copied file. Tests verify these hashes offline from their output directory. They never read another checkout, fetch data or use a network fallback.

The parent owns the schemas and all 137 identity and 66 package cases. These copies are inputs, not .NET-specific expectations. The shared-reader tests initially execute only their named supported operations and report those cases separately. The presence or hash verification of a case file is not execution of every case. Full host-adapter/MCK execution remains unfinished.

The schemas are the exact published extension family `0.1.0-preview.1`. The normative member tables remain in the parent's `docs/design/draft/extensions/distribution-and-acquisition.md` at the same commit. They also require semantic checks beyond JSON Schema shape. No old `claimsVersion` codec or version conversion is used. Older reference fixtures next to this snapshot are historical evidence and are unchanged.

To refresh deliberately, capture each listed `git show <full-commit>:<path>` as bytes, verify it against that Git blob, and update provenance and tests together. Do not edit frozen case inputs or expected outcomes here.
