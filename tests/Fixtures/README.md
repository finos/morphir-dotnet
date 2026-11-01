# Model fixtures

Keep complete Morphir JSON documents in fixture files. The backend, plugin and BDD test projects link these files as embedded resources with names beginning `Morphir.Tests.Fixtures.`. The car rental models are linked directly from `examples/car-rental/models/`, so there is one source for the example and its tests.

F# tests load resources through `ResourceLoader.fs`; the C# BDD steps use their assembly resource stream. The standalone package-check script loads the same shared files from disk.

Use `JsonNode` to change a version, rate, module name or declaration list. This keeps substitutions structural and avoids hand-assembled JSON or escaping mistakes. The focused backend tests also use small object builders to describe individual IR behaviors. A text templating engine can be introduced if future fixtures require more complex text expansion; the current substitutions do not need one.
