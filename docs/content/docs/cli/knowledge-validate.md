---
title: Validate a knowledge bundle
description: Check an Open Knowledge Format bundle with the Morphir .NET CLI
---

# Validate a knowledge bundle

`morphir knowledge validate` checks a directory of Open Knowledge Format (OKF) documents and assets.
It collects findings across the bundle, so one malformed document can be fixed alongside others.

```bash
morphir knowledge validate kb/bundles/revenue
morphir knowledge validate kb/bundles/revenue --okf-version 0.2
morphir knowledge validate kb/bundles/revenue --policy morphir --json=document
```

The command uses an explicit `--okf-version` first, then the `okf_version` in the root `index.md`,
then OKF 0.3. An unsupported declaration is reported as `unverified` and gets a best-effort 0.3
check. OKF 0.3 accepts ordinary Markdown such as a project README. Under OKF 0.2, a
non-reserved Markdown file requires concept frontmatter with a nonempty `type`.
See the [OKF 0.3 specification](https://github.com/open-knowledge-format/open-knowledge-format/blob/527e81aff55992be21507e97004286789126f88c/SPEC.md)
and [OKF 0.2 specification](https://github.com/GoogleCloudPlatform/open-knowledge-format/blob/8a8ff87bfd3847c34c9ddd4fbfd9296be2aee4a3/SPEC.md).

`--policy morphir` also requires a root index with a version declaration and checks authored index
descriptions, local references, and source citation IDs. Policy findings are separate from OKF
conformance. For example, a broken local link can be an OKF advisory and a Morphir policy error.

Local links and resource paths resolve within the bundle. External URLs are left alone.
The physical directory reader excludes symbolic links and reports encountered exclusions.

## Output and exit status

Text output includes the member path and one-based line and column when available.
The JSON report includes a zero-based, half-open range whose character offsets count UTF-16
code units. Each diagnostic has a stable code, message, severity and origin. `--json=document`
writes the report as one JSON object on stdout. A bare `--json` writes one `completed` record
containing that report. See [machine output](_index.md#machine-output).

| Exit status | Meaning |
| --- | --- |
| 0 | The check completed without errors. |
| 1 | The check produced one or more errors. |
| 2 | The command could not run because of the invocation or bundle directory. |

Use the report's `conformance` field to distinguish verified conformance from a best-effort
check. Library consumers can call the same validator through `Morphir.Knowledge.Okf`.
