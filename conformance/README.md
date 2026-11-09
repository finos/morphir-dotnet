# Morphir Compatibility Kit seed

The F# frontend suite is the first seed of the portable Morphir Compatibility
Kit. Cases and expectations are product data, independent of the implementation
under test. Do not record or regenerate expectations from compiler output.

- [Vocabulary 1.1](steps/steps-1.md) defines every supported step and the
  value encoding of call observations.
- [F# frontend suite](suites/frontend-fsharp/) covers four IR versions and three
  source input kinds.
- [Deviations](deviations.md) records the upstream compatibility decisions.
- [Runner source](../src/Morphir.Conformance/) implements the official Gherkin
  reader, linter, matrix checks, comparison and deterministic JSON reports.

Run the fast reader/runner tests:

```console
dotnet run --project tests/Morphir.Frontend.FSharp.Tests -- --sequenced
```

The `library` adapter accepts an implementation identity and a function from
compile input to an observation or operational failure. It receives no expected
outcome. `FrontendAdapter.create` binds the adapter to any in-process frontend of
the compile contract (`IFrontend`) and writes a successful model with the writer
of the case's IR version. A directory row gives the documents in ordinal path
order. The adapter has no project-file provider, so a project-file row keeps the
document order of the case; the `cli` adapter checks real project-file expansion.

The suite has one refusal case for each code of the F# admission catalog, grouped
by construct family, and semantic cases. A semantic case has expected IR written
by hand for each IR version under `suites/frontend-fsharp/expected/`, call
observations, or both. The frontend tests run the whole suite with the in-process
F# frontend and the .NET call checker, which generates F# from each compiled
model with the F# backend and runs the calls in FSI:

```console
dotnet run --project tests/Morphir.Frontend.FSharp.Tests -- --sequenced --filter "F# corpus through the library adapter (MCK-1)"
```

Check the AOT boundary on macOS ARM64:

```console
dotnet publish tests/Morphir.Conformance.Smoke -c Release -r osx-arm64 -p:PublishAot=true -p:TrimmerSingleWarn=false -o /tmp/morphir-conformance-aot
/tmp/morphir-conformance-aot/Morphir.Conformance.Smoke conformance
```

The frontend tests also run the IR → F# → IR round trip, a morphir-dotnet check
that is not a kit verdict. For each successful case, the F# backend generates F#
from the compiled IR, and the frontend compiles that F# again. The two models
must be equal. For IR v4, the source spans are removed before the comparison,
because the generated F# has other lines and columns.

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
