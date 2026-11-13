---
title: "Discover a workspace from a host library"
linkTitle: "Discovery API"
description: "Find the workspace and member projects from .NET code with ProjectSystem.Discover and ProjectSystem.Load"
---

This page uses the workspace of [Configure a workspace]({{< relref "/docs/getting-started/configuration" >}}).

For a native repository, supply an absolute boundary and start path:

```csharp
using Morphir.Configuration.IO;

var tree = new FileSystemProjectTree("/repo");
var result = ProjectSystem.Discover(tree, "/repo/packages/orders/src");
// Discovery.Location: /repo
// Selection.Projects: /repo/packages/orders
```

A mounted host can supply the same contents with `InMemoryProjectTree`. Both adapters use the same pure discovery and selection policy. The result retains the complete member inventory separately from the selected project. Pass `"packages/orders"` as the optional selector to choose that canonical member path, even if another member has the same display name.

The boundary includes `/repo` and excludes its parents. An input file starts discovery in its containing directory. Only the nearest declared workspace participates; discovery does not merge configuration from every ancestor. If no workspace is found, the nearest primary location is a standalone project. With no primary, the start directory remains the location. Neither `.git` nor an empty `.morphir` directory declares a workspace through this API.

Keep missing and invalid members visible in diagnostics. A nonexistent literal member produces a warning; a literal directory with no configuration or an invalid document requires attention. Excluded members are absent from the selected membership. Read source reports to distinguish an optional miss from a read failure.

Resolve that member independently through the shared loader:

```csharp
var selected = result.Selection.Projects.Single();
var effective = ProjectSystem.Load(tree, result.Discovery, selected,
    new ProjectLoadOptions { Isolated = true });
```

Isolation retains declared primaries and supplied values while omitting machine and personal files. For active profiles, supply the desired profile selection through `ProjectLoadOptions.Input`. To load the root instead, omit the `ProjectInfo` argument. The owning workspace context carries an absolute output directory separately from the member settings; the member does not inherit the root's `project` or `workspace` document subtrees.

Machine files require both explicit `MachineLocationInput` and a separately authorized `MachineTree`. No ambient machine access is implied by this example. A relative `SelectedPath` resolves from the invocation directory, while the project selector resolves from the effective workspace root.

The `dist`, `tool` and `extension` commands are not implemented yet ([#490](https://github.com/finos/morphir-dotnet/issues/490), [#491](https://github.com/finos/morphir-dotnet/issues/491), [#492](https://github.com/finos/morphir-dotnet/issues/492)); when they are, they will use this same discovery.
