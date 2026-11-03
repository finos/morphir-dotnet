# Final status-only shared edit proposal

No lease is assumed. These are the exact proposed shared-path changes for the
next verified checkpoint. Historical evidence and cleanup limitations remain.

## README.md

Replace the paragraph beginning `The in-memory [C# generation foundation]` with:

```markdown
The in-memory [C# generator](docs/content/docs/backends/csharp/_index.md) lowers
typed classic/v4 libraries with explicit numeric C# 14/15 profiles, immutable
values, executable functions/patterns/record operations and public SDK bindings.
Original business fixtures and the bounded modern package-host/trimming checks
are verified under [#474](https://github.com/finos/morphir-dotnet/issues/474).
Final delivery checks are in progress. CLI integration is a separate gated initiative.
```

In the current runtime-delivery paragraph, replace only its final sentence
`Archive follow-up landing and coordinator cleanup remain pending.` with:

```markdown
SDK archive #472 landed; source SDK worktrees and their owned artifacts were removed. Empty shared archive directory cleanup remains pending with the recorded permission/policy limitations.
```

## CHANGELOG.md

Replace the existing Added entry beginning `In-memory C# generation foundation`
with:

```markdown
- Reusable in-memory C# generation for typed classic/v4 libraries with numeric 14/15 profiles, immutable structural values, executable functions/patterns/record operations, public SDK bindings, located whole-model refusals and traversal cancellation; compiled business/SDK observations and bounded modern package-host/trimming verification ([#474](https://github.com/finos/morphir-dotnet/issues/474)).
```

## docs/content/contributing/design/prds/_index.md

Replace only the C# code generation row with:

```markdown
| [C# code generation](./add-csharp-code-generation.md) | In Progress | Final validation | Typed lowering, SDK mapping, cancellation, business matrix and bounded trimming verified; final checks/PR handoff pending, #474 |
```

## docs/content/contributing/design/prds/dotnet-code-generation.md

In the generator initiative row, replace only status
`Implementing #474; early packaged-host foundation verified` with
`Implementing #474; typed lowering, SDK/business matrix and bounded trimming verified`.

In the historical `8dca9b6` paragraph, replace only
`SDK mapping, mid-traversal cancellation and business-fixture acceptance remain pending.`
with `At that checkpoint, SDK mapping, mid-traversal cancellation and business-fixture acceptance remained pending.`

Append:

```markdown
At cancellation checkpoint `a2b67c9`, the packaged .NET 10 host passes 143/143;
active traversal/emission cancellation returns no sources. Subsequent fixture
evidence, recorded in the generator change's validation record, passes 153 focused
tests and 18 actual pinned-compiler executions. These include original classic
car-rental 1/2/3 and daily-rate inputs, direct v4 daily-rate, a labeled synthesized
v4 car input, and a separately labeled compatible books-and-records type slice.
Whole ecosystem originals with missing types or draft bodies still fail with
located diagnostics and no partial output. A fresh typed package consumer passes
self-contained win-arm64 link trimming with identical observations/source hashes
and no IL warnings. Tested package/adapter host remains .NET 10.0.14/SDK
10.0.403/Windows arm64; other IDE hosts and Native AOT remain untested. Final
delivery checks are in progress. CLI integration remains gated and undispatched.
```

These changes describe verified behavior without declaring initiative completion,
landing, general IDE support, full-original ecosystem conformance or archive cleanup.
