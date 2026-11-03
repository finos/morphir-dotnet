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

This checkpoint is incomplete. Classic normalization, YAML policy qualification,
full diagnostics and corpus tests, final source-loader behavior, formatting and
trimmed/native AOT execution remain before review readiness.
