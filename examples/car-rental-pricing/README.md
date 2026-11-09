# Car rental pricing

The finished example of the
[F# pricing tutorial](../../docs/content/docs/guides/fsharp-pricing-tutorial.md):
car rental pricing rules in F#, compiled to Morphir IR with `dotnet-morphir compile`.

```bash
dotnet-morphir compile
dotnet-morphir generate --target csharp --output generated/csharp
```

`CompileTutorialTests` in `tests/Morphir.Tooling.Tests` keeps these files equal to
the tutorial and checks that they compile.
