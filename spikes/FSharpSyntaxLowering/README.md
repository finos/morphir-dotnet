# Syntax-led F# match reconstruction proof

Run from the repository root:

```console
dotnet run --project spikes/FSharpSyntaxLowering -- --sequenced
```

Expected result: 34 passing Expecto tests. The project stays outside the solution
and normal test target. It validates the replacement for the deliberately failing
`FSharpMatchJoin` probe. It is not production frontend code.

Original syntax provides matches, patterns, clause order and source body ranges.
A second FCS check of an analysis-only copy supplies subject and result types
through fresh bindings. Union fields are instantiated with checked generic
arguments. No typed decision tree or target-range join is required.

The project overrides FCS to 43.12.403 and Core to its exact supported dependency,
10.1.403. It does not change central dependency versions. It targets .NET 10 and
uses the hosting runtime's trusted platform assemblies. Production dependency
alignment remains a separate task.

The reflection syntax walker, single-file sandbox capture and exceptions are
spike conveniences. Production needs explicit visitors, the hermetic multi-file
edge, typed diagnostics and full binding/constraint/reference preservation checks.
See [the findings](../../openspec/changes/archive/2026-11-09-add-fsharp-compile/spikes/syntax-led-reconstruction.md)
for the validation boundary and the revised [FSF-2 decision](../../openspec/changes/archive/2026-11-09-add-fsharp-compile/design.html#fsf-2).
