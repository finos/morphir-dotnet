---
title: Generate F# from a Morphir model
description: Generate F# source from JSON or Ion libraries
---

# Generate F# from a Morphir model

From a repository checkout, generate the shared daily-rate example:

```powershell
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v4.json --target fsharp --output artifacts/generated-fsharp --json
```

The report lists `Inventory.fs`. The output contains that file and the
`.morphir-generated.json` ownership manifest. Generate the same input again to
receive an `unchanged` receipt without rewriting the file.

Classic JSON and Ion use the same command. For an Ion tree, pass its directory:

```powershell
morphir generate --input tests/Fixtures/MorphirIon/trees/v3-daily-rate --target fsharp --output artifacts/generated-fsharp --json
```

Use the report's artifact order when adding files to an F# project. Generation
does not create the project or compile it. Keep handwritten files separate
from owned artifact paths. See the [command reference](../../cli/generate/) for
the supported profile, options, exit codes and publication guarantees.

`morphir generate --target fsharp` uses the built-in backend `morphir.fsharp`.
The same generator is also published as the MEP extension
`morphir-fsharp-backend` for hosts without that built-in; see
[F# generation](../../backends/fsharp/).
