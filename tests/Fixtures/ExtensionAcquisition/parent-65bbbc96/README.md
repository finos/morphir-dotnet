# Published extension-family test data

These are byte-exact Git blobs from [finos/morphir](https://github.com/finos/morphir/tree/65bbbc961d9eb962ad941a859c9d09fba058f5a0), commit `65bbbc961d9eb962ad941a859c9d09fba058f5a0` (finos/morphir#1176).
`manifest.json` records the source repository, path, full commit and SHA-256 of every copied file. Tests verify these hashes offline from their output directory. They never read another checkout, fetch data or use a network fallback.

The parent owns the schemas, all 158 identity and 72 package cases, and the two NuGet package fixtures under `fixtures/` that the `nupkg-packed` and `nupkg-repository-signed` package cases read. These copies are inputs, not .NET-specific expectations. The shared-reader tests execute only their named supported operations and report those cases separately. The presence or hash verification of a case file is not execution of every case. Full host-adapter/MCK execution remains unfinished.

The schemas are the exact extension family `0.1.0-preview.3` from finos/morphir#1176: the `nupkg` package format, the `nuget-package` provenance and the `nuget-package-entry` source. The normative member tables and the "NuGet package convention" remain in the parent's `docs/design/draft/extensions/distribution-and-acquisition.md` at the same commit; the runtime selection rules are in `docs/spec/extensions/packages.md`. They also require semantic checks beyond JSON Schema shape. No version conversion is used: `0.1.0-preview.2` documents are refused.

To refresh deliberately, capture each listed `git show <full-commit>:<path>` as bytes, verify it against that Git blob, and update provenance and tests together. Do not edit frozen case inputs or expected outcomes here.
