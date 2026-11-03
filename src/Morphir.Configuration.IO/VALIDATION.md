# Reader validation checkpoints

The implementation uses executable TUnit and Reqnroll tests. Heavy jobs share a
single build token with the core/profile workers.

| Checkpoint | Validation state |
| --- | --- |
| `ade0c0e` | Reader API/BDD contracts, explicit initial compile red |
| `92c7253` | Parser foundation, JSON5 tests green, classic normalization red |
| `b93edae` | Pinned official grammar fixtures and hashes, execution pending at commit |
| `542f129` | Reader/normalization/YAML Default implementation, 58 tests green |
| `72386d7` | Non-CLI smoke scaffold, execution pending at commit |

- Initial red, before reader production code: `dotnet build tests/Morphir.Configuration.IO.Tests -m:1 -p:BuildInParallel=false`
  failed with CS0103 for the absent `ConfigurationReader` API.
- Reader build: `dotnet build tests/Morphir.Configuration.IO.Tests -m:1 -p:BuildInParallel=false --nologo --no-restore`
  succeeded with zero warnings/errors.
- Initial parser slice: `dotnet tests/Morphir.Configuration.IO.Tests/bin/Debug/net10.0/Morphir.Configuration.IO.Tests.dll --treenode-filter '/*/*/ReaderTests/*' --maximum-parallel-tests 1`
  ran three tests. Typed JSON5 and the extended JSON5 lexical grammar passed.
  Classic normalization failed because `project` was absent, establishing the
  next red behavior against the deliberately empty normalization implementation.

## Passing implementation slices

- Classic normalization passed its original regression. Four later observed
  failures covered equal legacy/canonical conflicts, YAML one-based positions,
  YAML integers above Int64 and typed TOML dates. After fixes, the focused
  `ReaderTests` run passed 8/8 with a zero-warning build.
- The full executable then passed 36/36. This includes Reqnroll scenarios,
  all 26 pinned Morphir corpus entries with file/text/typed-host parity, snapshot
  hashes, malformed/duplicate syntax, and both official JSON5 conformance runs.
  The latter verify 124 upstream file hashes and execute 114 syntax cases under
  each reader extension, or 228 parse checks.
- The next red slice ran 58 tests, with 43 passing and 15 failing. The new failures
  established YAML Default profile behavior, required/type-safe classic fields,
  exact opaque secret-reference normalization, and rejected explicit profile
  controls preventing reader callbacks.
- After implementing that slice, `dotnet build tests/Morphir.Configuration.IO.Tests -m:1 -p:BuildInParallel=false --no-restore --nologo`
  succeeded with zero warnings/errors. `dotnet tests/Morphir.Configuration.IO.Tests/bin/Debug/net10.0/Morphir.Configuration.IO.Tests.dll --maximum-parallel-tests 1`
  passed 58/58, including all five Reqnroll scenarios and the pinned grammar/corpus checks.

- The next run added decoded YAML duplicate and resource-bound coverage plus
  negative-zero semantic checks. It passed 61/63, failing both JSON5 `-0` and
  `-0x0` cases because the parser returned integer zero. The fix retains an IEEE
  negative zero. The parent's fresh capped build reported zero warnings/errors
  and the complete executable passed 63/63 with one test worker.
- All 124 official JSON5 fixture files were independently read from committed
  Git blobs and checked against their recorded SHA-256 hashes. There were zero
  mismatches, including CR/CRLF/LF cases.
- The parent built and ran the managed smoke host with zero warnings. It emitted
  `success=true`, `formats=3`, and successful JSON5, normalization, profile-loading
  and host-parity checks.

The parent owns final formatting, coverage, tooling compatibility, and actual
trimmed/native AOT smoke publishing and execution. Those validations remain
pending at this module handback. The implementation handback does not claim the
campaign has landed or that its owned checkout/branches have been cleaned up.
