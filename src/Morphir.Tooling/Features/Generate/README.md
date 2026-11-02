# Generation application contracts

The generation command is being implemented under
[add-generate-command](../../../../openspec/changes/add-generate-command/proposal.md).
These contracts separate settings, model loading, backend selection, generation
and publication. The CLI exposes them through `morphir generate`; see its
[command reference](../../../../docs/content/docs/cli/generate.md).

`CliGenerationSettingsResolver` accepts explicit input, target and output values.
It resolves relative paths against `InvocationContext.WorkingDirectory`; it does
not open project files or discover configuration. Repeated `KEY=VALUE` options
parse JSON first, fall back to strings and use the last occurrence of a key.
`BackendOptions` clones the values so they outlive their parse documents.

```csharp
IGenerationSettingsResolver resolver = new CliGenerationSettingsResolver();
var settings = await resolver.ResolveAsync(
    new GenerateOverrides("models/pricing.ion", "fsharp", "generated",
        Options: ["optimize=false"]),
    new InvocationContext(Environment.CurrentDirectory), cancellationToken);
```

The equivalent F# construction is covered by the consumer test:

```fsharp
open Morphir.Tooling.Features.Generate
let target = TargetId("fsharp")
let context = InvocationContext(System.IO.Directory.GetCurrentDirectory())
let input = ModelInput(System.IO.Path.Combine(context.WorkingDirectory, "model.ion"), InputFormat.Ion)
let settings = GenerationSettings(input, target, BackendOptions.Empty, PublicationPolicy("generated"), ModelReadLimits.Default)
```

`VersionedModel` lives in Morphir.Models. Its `DistributionModel` holds the
existing classic or v4 distribution. `SourceFormatVersion.Text` retains the
declaration separately from `Family` and `Kind`. No Ion reference or version
migration is required to consume the semantic model.

`LoadedModel` pairs that semantic view with a `ModelDescriptor`, a physical
representation and `RetainedInput`. Retained entries own their bytes; callers
can open read-only streams or request independent copies. A semantic view is
not a claim that all source annotations or representation facts are exposed.
Loaders must enforce `ModelReadLimits` before constructing these snapshots.

`RegisteredBackendResolver` takes statically registered `IGenerationBackend`
instances. Resolution matches the exact `TargetId`, family, source version and
distribution kind. Unknown targets and ambiguous compatible providers are
invocation failures. An incompatible model is a semantic failure. Resolution
does not invoke any provider. Each provider validates its own options and
returns `GenerationArtifacts`; `BackendGenerationRequest` has no output path.
The host owns publication through the existing tree publisher.

`ModelDescriptor.RequiredSemantics` carries explicit capability requirements from
callers or future admission adapters. Providers advertise
`BackendDescriptor.SupportedSemantics`; unmet requirements fail before provider
invocation. Bare IR loaders leave requirements empty and retain opaque extension
data. No IR extension key is reserved or interpreted as a required marker.

The C# provider example and F# settings example compile and run in
`GenerationContractsTests`. Resolver tests include a second target that needs
only registration, without changes to the command.

`Outcome<T>` distinguishes success from a typed `GenerationFailure` with
semantic, invocation, operational or cancellation classification. Diagnostics
carry stable codes and available path/location context. They do not carry raw
exceptions or retained payloads.

TOML/YAML configuration discovery and MEP host/guest adapters are future work.
They should produce the same resolved settings and backend requests. No config
syntax or protocol envelope is admitted by these contracts automatically.

## Public model loader

```csharp
IModelLoader loader = new GenerationModelLoader(new BoundedModelCapture());
var outcome = await loader.LoadAsync(
    new ModelInput("models/pricing.ion", InputFormat.Auto),
    ModelReadLimits.Default, cancellationToken);
if (outcome is Outcome<LoadedModel>.Success loaded)
{
    var semanticModel = loaded.Value.Model;
    var exactDeclaredVersion = semanticModel.SourceVersion.Text;
    var retainedInput = loaded.Value.Original;
}
```

These delivered combinations load through the public adapter:

| Input | Tested source versions | Codec |
| --- | --- | --- |
| JSON file | 1, 2, 3, 3.0.0, 3.1.0 | Shared classic JSON reader |
| JSON file | 4, 4.0.0 | Shared v4 JSON reader |
| Ion text or binary file | 3.0.0, 3.1.0, 4.0.0 | `IonIr.read` |
| Ion tree rooted at `manifest.ion` | 3.0.0, 3.1.0, 4.0.0 | `IonIrTree.readDetailed` |

Loading preserves Library, Specs and Application identity. It does not select
a generator or turn specifications into implementations. Backend compatibility
is a separate check; see the F# generator's exact supported version list.

Automatic detection checks the Ion binary marker before textual names or hints.
Ion suffixes and header annotations select Ion; other supported text selects
JSON. An explicit format constrains parser selection. A malformed recognized
Ion document keeps its Ion rule, and tree errors name the relative member and
logical location. There is no parser fallback and no JSON write/reparse bridge.

YAML models and JSON/YAML trees return `unsupported-format`. MEP JSON envelopes
and text/binary `morphir_model` wrappers return `unsupported-profile` because
admission is unavailable. YAML configuration discovery is also future work.

## Capture limits and retention

Defaults are 64 MiB aggregate bytes, depth 128, 1,000,000 decoded nodes,
10,000 tree entries and 4,000 characters per absolute input path. All budgets
must be positive. Tree entry counts include directories and ignored files.
Byte limits cover every captured file. Tree depth counts path components below
the root; codec depth counts the root node as one. JSON token and Ion reader
budgets run before typed decoding. Links, linked ancestors and device entries
are refused. Read failures identify the affected entry and return no model.

Capture owns file bytes before decoding and never reopens the original input
to generate code. Changing the source afterward cannot change the retained
snapshot or model. Tree entries keep relative paths and exact bytes, including
representation facts the typed view does not expose. Returned byte arrays are
copies; streams are read-only. Semantic normalization is not a promise of
lossless serialization. Cancellation returns a typed cancelled failure.

`GenerationModelLoaderTests`, `GenerationIonLoaderTests`, `GenerationProfileTests`
and the common wire conformance matrix exercise these combinations and boundaries
through this public API. The matrix uses the same daily-rate/scalar consumers,
compiler and refusal assertions as typed generation.
