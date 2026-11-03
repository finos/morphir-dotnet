# Reader validation checkpoints

The implementation uses executable TUnit and Reqnroll tests. Heavy jobs share a
single build token with the core/profile workers.

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

Formatting, coverage, further loader/resource-bound regressions and actual
trimmed/native AOT smoke execution remain before review readiness. The smoke host
is authored in `tests/Morphir.Configuration.IO.Smoke` but has not yet been built
or executed at this checkpoint.
