# Inspection serializer runtime qualification

This small non-CLI host compiles the actual `ConfigurationReport.cs` source used
by the CLI and its generated System.Text.Json context. Reflection serialization
is disabled. It exercises NaN, positive and negative infinity, finite numeric
types, temporal text/kind, JSON Pointer escaping, nested arrays/null, ordinary
tag-shaped objects, redaction before encoding, and entry/contribution metadata.
It also checks that classic compatibility defaults remain effective while file
queries and explanations report their absence from the authored document.

Nonfinite values appear as `{"$number":"NaN"}`,
`{"$number":"Infinity"}` or `{"$number":"-Infinity"}`. The report's
`valueTypes` array identifies the precise JSON Pointer with `kind: "number"`
and `encoding: "nonfinite"`. A user object with the same keys has no such
discriminator. Temporal values retain text with `kind: "temporal"` and their
canonical kind in `encoding`. Finite values use normal JSON types.

```sh
export DOTNET_PROCESSOR_COUNT=2
export TMPDIR=/private/tmp/morphir-config-inspection
export TEMP="$TMPDIR" TMP="$TMPDIR"
dotnet build tests/Morphir.Configuration.Inspection.Smoke -m:1 -p:BuildInParallel=false
dotnet tests/Morphir.Configuration.Inspection.Smoke/bin/Debug/net10.0/Morphir.Configuration.Inspection.Smoke.dll
dotnet publish tests/Morphir.Configuration.Inspection.Smoke \
  -c Release -r osx-arm64 -m:1 -p:BuildInParallel=false \
  -p:PublishTrimmed=true -p:PublishAot=false -p:TrimmerSingleWarn=false \
  -p:ILLinkTreatWarningsAsErrors=false \
  -o /private/tmp/morphir-config-inspection/serializer-trimmed
/private/tmp/morphir-config-inspection/serializer-trimmed/Morphir.Configuration.Inspection.Smoke
dotnet publish tests/Morphir.Configuration.Inspection.Smoke \
  -c Release -r osx-arm64 -m:1 -p:BuildInParallel=false \
  -p:PublishTrimmed=true -p:PublishAot=true -p:TrimmerSingleWarn=false \
  -p:ILLinkTreatWarningsAsErrors=false \
  -o /private/tmp/morphir-config-inspection/serializer-native
/private/tmp/morphir-config-inspection/serializer-native/Morphir.Configuration.Inspection.Smoke
```

The project retains a strict linker-as-errors default. Runtime qualification
keeps pinned FSharp.Core dependency warnings visible by explicitly selecting
`ILLinkTreatWarningsAsErrors=false`. No suppression or warning-free dependency
claim is made. Unlike the Microsoft adapter host, this serializer host does not
load CsToml or any CLI services. Restore incidental RID/ILCompiler lockfile churn
after publishing. The delivery journal records actual commands and results.
