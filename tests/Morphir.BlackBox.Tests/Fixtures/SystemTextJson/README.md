# System.Text.Json fixtures

- `WireRules.fs` is a link to `tests/Morphir.Myriad.SystemTextJson.Tests/Contexts/WireRules.fs`, which the unit tests
  of the plugin also compile.
- `WireRulesProgram.fs` is the program of the scenario that reads and writes the wire rules from the packed packages.
  It came from the check "wire defaults, extension data and migration work from packed packages" of
  `tests/Morphir.Myriad.SystemTextJson.Tests/package-tests.fsx`, which pull request 4 of the change
  `consolidate-black-box-tests` removed.
