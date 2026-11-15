# C# language support probes

These probes are the evidence for the [C# language support PRD](../../docs/content/contributing/design/prds/csharp-language-support.md). The report with the reproduction steps and the results is [C# language support spike, 2026-11-15](../../docs/content/contributing/design/spikes/csharp-language-support.md).

The probes are not product code. No project of `Morphir.slnx` references them.

| Folder | Question |
| --- | --- |
| `roslyn-codegen` | Does C# output with `SyntaxFactory` run under Native AOT, with and without a bind of the output? |
| `roslyn-analyzer` | Does a `DiagnosticAnalyzer` run in process under Native AOT? |
| `roslyn-loop` | Can data-flow and control-flow analysis classify a loop as a traversal or as recursion, under Native AOT? |
| `cs15` | Do `closed record` and `union` compile and match exhaustively, and what does Roslyn have for them? |
| `union` | How does Roslyn show a `union` to a frontend, and how do plain unions and records compare? |
| `cross-assembly` | Does a `switch` stay exhaustive across two assemblies that each have their own polyfill types? |
| `lower-tfm` | Do `union` and `closed record` compile for `net8.0` and `netstandard2.0`? |
| `source-generator` | Does a source generator with an F# core load in the compilers of three .NET SDKs? |
| `host-size` | How much does Roslyn add to the Native AOT `morphir`? |

`Directory.Build.props` stops the walk to the settings of the repository, so each probe also builds in a copy outside the repository. Two probes need the repository: `source-generator` reads the sources in `src/`, and `host-size` publishes `src/Morphir`.

All probes need .NET SDK 11.0.100.
