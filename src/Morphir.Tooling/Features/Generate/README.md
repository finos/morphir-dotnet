# Generation application contracts

The generation command is being implemented under
[add-generate-command](../../../../openspec/changes/add-generate-command/proposal.md).
These contracts separate settings, model loading, backend selection, generation
and publication. The command itself is not available at this stage.

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
