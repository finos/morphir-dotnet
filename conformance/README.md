# Morphir Compatibility Kit seed

The F# frontend suite is the first seed of the portable Morphir Compatibility
Kit. Cases and expectations are product data, independent of the implementation
under test. Do not record or regenerate expectations from compiler output.

- [Vocabulary 1.0](steps/steps-1.md) defines every supported step.
- [F# refusal seed](suites/frontend-fsharp/mutable.feature) covers four IR
  versions and three source input kinds.
- [Deviations](deviations.md) records the upstream compatibility decisions.
- [Runner source](../src/Morphir.Conformance/) implements the official Gherkin
  reader, linter, matrix checks, comparison and deterministic JSON reports.

Run the fast reader/runner tests:

```console
dotnet run --project tests/Morphir.Frontend.FSharp.Tests -- --sequenced
```

The `library` adapter accepts an implementation identity and a function from
compile input to an observation or operational failure. It receives no expected
outcome. Frontend binding is added when the production compiler is available;
these runner tests do not qualify an F# frontend.

Check the AOT boundary on macOS ARM64:

```console
dotnet publish tests/Morphir.Conformance.Smoke -c Release -r osx-arm64 -p:PublishAot=true -p:TrimmerSingleWarn=false -o /tmp/morphir-conformance-aot
/tmp/morphir-conformance-aot/Morphir.Conformance.Smoke conformance
```

The smoke program deliberately supplies an incorrect implementation. It exits
zero only when the implementation fails qualification and every planned variant
has a verdict. It writes the unqualified report to stdout. It validates the
runner, not the frontend.

The compiler and Core pins remain unchanged. The smoke's expanded FSharp.Core
warnings are compared with the existing AOT CLI baseline in the feature PRD;
no new warning is suppressed. Other RIDs can run the same smoke using their own
runtime identifier.

Kit manifests, profiles, claims planning, additional adapters, provenance review
records, report checking and literate documentation belong to the full
compatibility-kit change. The seed keeps those additions on the same reader,
content model and runner.
