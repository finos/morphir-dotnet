---
title: "Microsoft configuration integration"
linkTitle: "Microsoft configuration"
weight: 40
description: "Opt into standard .NET configuration while retaining typed Morphir values and metadata"
---

# Microsoft configuration integration

`Morphir.Configuration.Microsoft` adds a read-only provider to an
`IConfigurationBuilder`. It projects one already resolved Morphir result.
It has no CLI dependency and does not alter logging or remove existing host
configuration providers.

The canonical `DocumentResolution` remains authoritative. Its typed values,
origins, source reports, active profiles, workspace context and diagnostics stay
available through `GetMorphirConfiguration()`. Do not reconstruct that document
from flattened strings.

## Register an explicit result

```csharp
using Microsoft.Extensions.Configuration;
using Morphir.Configuration.IO;
using Morphir.Configuration.Microsoft;

var tree = new FileSystemProjectTree("/repo");
var discovered = ProjectSystem.Discover(tree, "/repo/app");
var project = discovered.Selection.Projects.Single();
var canonical = ProjectSystem.Load(tree, discovered.Discovery, project,
    new ProjectLoadOptions { Isolated = true });

var builder = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["host:unrelated"] = "retained"
    });
builder.AddMorphirConfiguration(canonical);
var configuration = builder.Build();
var target = configuration["codegen:targets:0"];
var typed = configuration.GetMorphirConfiguration();
```

Supply an absolute authorized boundary and start directory. Check discovery and
selection diagnostics before choosing a project. Omit the project argument when
loading the workspace root. See the [discovery guide]({{< relref "/guides/configuration" >}})
for member selection and explicit machine-file authorization.

An overload performs the same single resolution before registration:

```csharp
builder.AddMorphirConfiguration(tree, discovered.Discovery, project,
    new ProjectLoadOptions { Isolated = true });
```

The overload uses the supplied bounded tree and loading options. It does not
infer ambient machine access. Neither overload registers raw JSON, YAML or TOML
providers. Morphir resolves ownership, profiles, complete array replacement and
shape replacement before flattening. A shorter higher-layer array therefore has
no stale indexes from lower Morphir files.

## Bind options without reflection

For source-generated binding, enable the generator in the consuming C# project
and reference `Microsoft.Extensions.Configuration.Binder` 10.0.14:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
</PropertyGroup>
```

Use a concrete options type at the binding call site:

```csharp
var options = configuration.GetSection("codegen").Get<CodegenOptions>();

public sealed class CodegenOptions
{
    public string[]? Targets { get; set; }
    public string[]? Missing { get; set; }
}
```

With `targets = []`, `Targets` is present and empty. An absent `missing` setting
leaves `Missing` null. The provider emits the empty-string marker supported by
.NET 10's generated array binder. Nonempty arrays use zero-based indexed keys.
Bind fresh options with nullable array properties as above. The adapter does not
promise replacement behavior for arbitrary existing option instances or
reflection-based binding of F# unions.

Explicit binding can instead read canonical values directly. This retains type
and presence distinctions independently of the standard configuration view:

```csharp
using Microsoft.FSharp.Collections;
using Morphir.Configuration;

var value = ConfigTree.tryFind(ListModule.OfSeq(new[] { "codegen", "targets" }),
    typed.Effective);
var explicitOptions = new CodegenOptions
{
    Targets = value?.Value is ConfigValue.Array array
        ? array.Item.Select(item => ((ConfigValue.String)item).Item).ToArray()
        : null
};
```

The application owns its options type, validates required fields, and can
register explicitly constructed options in its own dependency container. The
adapter performs no reflection-based discovery of consumer types.

## Projection and failure contracts

| API | Result |
| --- | --- |
| `MorphirConfigurationProjection.Create(DocumentResolution)` | Canonical result, immutable projected `Values`, typed `Diagnostics`, and `IsSuccess` |
| `AddMorphirConfiguration(DocumentResolution)` | Adds one provider on success; throws `MorphirConfigurationException` on failure before changing the builder |
| `GetMorphirConfiguration()` | Returns the canonical result of the most recently registered Morphir provider; fails if none was registered |

`MorphirProjectionDiagnostic` contains `Code`, `Message`, and `Paths`, with
canonical quoted key paths. Failed projections have an empty `Values` map while
retaining the original `Canonical` result. The exception also exposes
`Canonical` and `Diagnostics`.

- `config.microsoft.collision` rejects case-distinct paths such as `custom.Foo`
  and `custom.foo`, including collisions at parent objects. Literal colon
  segments such as `custom."a:b"` are rejected even without a competing path,
  since standard .NET readers would reinterpret them as hierarchy. Empty key
  segments are rejected because they alias .NET section boundaries.
- `config.microsoft.invalid_resolution` refuses a result with error diagnostics.
- `config.microsoft.invalid_root` refuses a non-object document root.

The flattened view formats scalar values with invariant culture. For example,
integer `4` and string `"4"` both project to `"4"`; their canonical types remain
distinct. Arrays and nested objects flatten after Morphir resolution. Empty
objects and null array elements retain their canonical meaning even where a
standard string consumer cannot infer it.

The standard projection uses the shared inspection redaction policy. Sensitive
values and opaque secret references become `[REDACTED]`. Canonical access is
unredacted host data and must be handled accordingly. Registration never
resolves an environment/file/keyring reference or executes a secret command.
The provider rejects `Set`; it does not implement mutation, reload or watching.
Unrelated providers remain in the builder. Canonical arrays, scalar values and
empty objects own their replacement subtrees: earlier host providers cannot
revive discarded array indexes, old descendants or redacted container values.
Standard .NET precedence still lets a provider registered later override the
Morphir view.

## Published non-CLI example

The runnable [smoke host](../../../../tests/Morphir.Configuration.Microsoft.Smoke/README.md)
uses both binding paths, typed metadata, native and mounted trees, replacement
and empty arrays, collision diagnostics, shared redaction, and generated JSON
serialization with reflection disabled. Managed, trimmed and Native AOT runs each
pass 43 checks, including earlier-host-provider array and redacted-value boundaries. Its README gives managed, trimmed and
Native AOT commands and records dependency-warning limitations. This qualifies
the documented concrete binding path, not arbitrary consumer types.
