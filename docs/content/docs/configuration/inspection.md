# Inspecting a resolved configuration

`Morphir.Configuration` exposes pure `Inspection.get`, `list`, `show`, `path`
and `explain` functions. They take a `QueryScope` and `ResolutionDetails`.
`get` and `explain` also take a segmented key path. No operation reads files,
consults the environment, constructs the CLI, executes scripts or resolves
secret references.

Use `Resolver.resolveDetailed(input)` for supplied sources, or
`ProjectSystem.LoadDetailed(tree, discovery, project, options)` from
`Morphir.Configuration.IO`. Both resolve once and retain loaded canonical layers
alongside their ownership-filtered contributions. Their `Resolution` property
is the existing `DocumentResolution`; `Resolver.resolve` and `ProjectSystem.Load`
keep their original signatures and results.

```fsharp
open Morphir.Configuration

let details = Resolver.resolveDetailed suppliedInput
let key = KeyPath.parse "codegen.\"custom.generator\".enabled"
match key with
| Ok segments -> Inspection.get QueryScope.Effective segments details
| Error message -> failwith message // map to a usage diagnostic at the host edge
```

```csharp
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Configuration.IO;

var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
{
    ["/repo/morphir.toml"] = "[project]\nname='Example'\n[custom]\nitems=[]\n"
});
var discovery = ProjectSystem.Discover(tree, "/repo");
var details = ProjectSystem.LoadDetailed(tree, discovery.Discovery);
var key = ListModule.OfSeq(new[] { "custom", "items" });
var item = Inspection.get(QueryScope.Project, key, details);
// Found is true; Value contains ConfigValue.Array with no items.
var history = Inspection.explain(QueryScope.Effective, key, details);
var sources = Inspection.path(QueryScope.Effective, details);
```

## Key grammar

Paths are ordinal and case-sensitive. Plain segments contain ASCII letters,
digits, `_` and `-`; dots separate segments. Double-quoted segments are JSON
strings, so `codegen."custom.generator".enabled` addresses one literal dot in a
name. Quotes, backslashes, control characters, colons, non-ASCII characters and
empty names use quoted segments. `KeyPath.format` produces this grammar and
`KeyPath.parse` returns a typed `Result`. Leading/trailing dots, empty plain
segments, whitespace outside quotes and bracket indexing are invalid.
Arrays and secret references are whole values, not traversable paths.

## Scopes and results

`QueryScope.Effective` uses the resolved document. `Workspace`, `Project`,
`User`, `Global`, `System` and `File(path)` select source groups before query
merging. A scope never fills a missing key from defaults, environment or another
scope. Workspace/project mean primary files, not their profile/personal layers.
User means the override adjacent to the inspected target. File identities are
canonical absolute paths supplied by the host. Scoped inspection reads each
source's own normalized values; effective queries retain ownership filters.

File scope excludes synthetic compatibility defaults even when they share the
manifest's path. For a classic manifest with no authored `pipeline.backend`,
the file-scoped query reports missing while the effective query can still return
the compatibility value `checkpoint`.

`InspectionResult` has `Operation`, `Scope`, optional `Key`, `Found`, optional
`Value`, `Entries`, `Sources`, `Contributions`, and `Diagnostics`.

- `get` returns one typed value and an entry with its origins. `Found=false`
  differs from a present empty string, array, object, or null array element.
- `list` returns ordered leaf entries; arrays, empty objects and secret
  references are indivisible leaves. Each entry carries a formatted `Key`,
  segmented `Path`, typed `Value` and ordered `Origins`.
- `show` returns the nested document plus entries and origins.
- `path` returns consulted source identities, load statuses, contribution flags
  and precedence order. Missing optional, skipped and failed sources stay visible.
- `explain` returns relevant ordered layer contributions and marks surviving
  origins as winners. A composite object can retain winners from several layers.
  An ancestor shape replacement can block a formerly present descendant.

Check `Diagnostics` for errors before consuming a result. CLI exit codes are
0 for success, 1 for a missing queried key, and 2 for usage/loading errors.
The [CLI reference](../cli/config.md) documents the generated JSON report,
including source and diagnostic schemas.

## Redaction

Every inspection operation calls the same recursive presentation redactor.
Secret references become the string `[REDACTED]`. A path containing a sensitive
name also becomes `[REDACTED]`, including descendants in objects and arrays.
Matching ignores punctuation and case and recognizes password, passphrase,
secret, token, credential, private key, API key, access key, connection string
and authorization names. This deliberately favors redaction of ambiguous names.

Diagnostic messages can contain parser input, so inspection replaces their
free text with safe text and retains codes, severity, key/source identity and
positions. Key names and source paths are metadata and remain visible. Hosts
must keep secrets out of those identities. `ResolutionDetails.Resolution` and
its layers remain the original canonical data for trusted application consumers;
inspection redaction does not mutate or sanitize that canonical storage.

See [Microsoft configuration integration](microsoft.md) for the optional
standard .NET view and its explicit/source-generated binding path. Existing
IR and cache commands retain their earlier configuration behavior.
