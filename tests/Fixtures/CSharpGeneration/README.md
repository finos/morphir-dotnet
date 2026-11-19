# C# generation fixtures

Each folder has a model of the C# frontend and the C# that the C# backend generates for it. The black-box scenario
"The generated C# of the Acme.Trading model is the reviewed C#" of
`tests/Morphir.BlackBox.Tests/Integration/CSharpGenerationCommand.feature` generates C# from `model.json` and compares
each file with the file in `generated/`, byte for byte. A pull request runs the scenario on the development build (JIT).
The release and manual workflows run it on each published target, also on the Native AOT executable. Thus the Native
AOT output and the JIT output are the same C#.

## acme-trading

`Domain.cs` is the example of the section "Modules" of `docs/content/docs/frontends/csharp/_index.md`. To make the
fixture again, run these commands in this folder with the development build, and then copy the files:

```bash
morphir compile --language csharp --input Domain.cs --package-name acme/trading --ir-version 4
cp .morphir/out/compile.dest/morphir-ir.json model.json
morphir generate --input model.json --target csharp --output generated
```

Do not keep `.morphir/` or `generated/.morphir-generated.json`. Review each change of a generated file: the change is a
change of the output of the backend.
