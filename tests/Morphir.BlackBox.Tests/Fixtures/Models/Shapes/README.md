# Shapes model

`shapes-v4.json` is an IR v4 library of the package `acme/shapes` with one module, `shapes`:

- `circle` and `rect`: record aliases.
- `shape`: a custom type with the wrapper constructors `circle` and `rect`. The C# backend writes it as a `union`.
- `size`: a custom type with the constructors `small` and `large` and no fields. The C# backend writes it as a `closed record`.

The scenario of `EndToEnd/CSharpBuildGeneration.feature` builds a library from it and matches on its types in an application.

`make-shapes.fsx` wrote the file with the IR v4 JSON writer of `Morphir.Models`. To write it again, build `tests/Morphir.Lang.CSharp.Tests` in Release, then run this command from the root of the repository:

```bash
dotnet fsi tests/Morphir.BlackBox.Tests/Fixtures/Models/Shapes/make-shapes.fsx tests/Morphir.BlackBox.Tests/Fixtures/Models/Shapes/shapes-v4.json
```
