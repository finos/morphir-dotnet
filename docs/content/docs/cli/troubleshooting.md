---
title: "Troubleshooting"
linkTitle: "Troubleshooting"
weight: 100
description: "Solutions to common issues with Morphir .NET CLI"
---

# Troubleshooting Guide

## C# generation

Use exact target `csharp`, numeric `language-version=14` or `15`, and an optional
literal C# namespace. These invocations fail with exit 2 before publication:

```powershell
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v4.json --target csharp --output artifacts/generated-csharp --option language-version=preview --json
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v4.json --target csharp --output artifacts/generated-csharp --option namespace=true --json
```

The first requires a numeric profile. The second supplies a JSON Boolean where
a namespace string is required. Use `--option language-version=14` and
`--option namespace=Pricing.Generated`. Source syntax, empty segments and
unescaped reserved words are refused with a located namespace diagnostic.
Use `@` for reserved words, for example `namespace=@class.Models`.

In JSON mode, parse the single stdout report and inspect `failureKind` and
`diagnostics`. Structured diagnostics are report data; JSON failures need not
duplicate their text on stderr. Without `--json`, human diagnostics use stderr.
Exit 1 identifies unsupported or invalid model semantics; exit 2 identifies
invocation or operational failures; cancellation before publication returns 130.
No successful partial sources are published for these failures.

An unsupported expression, SDK operation, distribution or required semantic has
a stable rule and available model location. Read the
[C# API/diagnostic guide]({{< relref "/docs/backends/csharp/diagnostics" >}}), then correct the input
or restrict it to the admitted model contract. Do not edit emitted sources to
hide a refusal. For a compiler error, verify the separately selected compiler's
numeric language version and the `Morphir.SDK.CSharp` package reference using
the [fresh-consumer walkthrough]({{< relref "/docs/getting-started/morphir-to-csharp" >}}).

Keep input and output separate and retain `.morphir-generated.json`. A failed
generation or rejected overlap preserves earlier owned sources and unowned
files. Shared publication does not promise rollback after a filesystem failure
during final commit; inspect the report and rerun after fixing that failure.

This guide covers common issues you may encounter when using the Morphir .NET CLI and how to resolve them.

## Installation Issues

### Tool Not Found After Installation

**Problem:** After running `dotnet tool install`, the `morphir` command is not found.

**Solution:**

1. **Check if tools are in PATH:**
   ```bash
   echo $PATH | grep .dotnet/tools
   ```

2. **Add to PATH if missing** (Linux/macOS):
   ```bash
   export PATH="$PATH:$HOME/.dotnet/tools"
   ```

3. **Add to PATH if missing** (Windows):
   ```powershell
   $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
   ```

4. **Restart your terminal** after modifying PATH

### Installation Fails with "Unable to find package"

**Problem:** `dotnet tool install Morphir.CLI` fails to find the package.

**Solution:**

1. **Check NuGet sources:**
   ```bash
   dotnet nuget list source
   ```

2. **Add nuget.org if missing:**
   ```bash
   dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
   ```

3. **Verify package name** - ensure you're using the correct package name

## Validation Issues

### "File does not exist" Error

**Problem:**
```
Error: File does not exist: morphir-ir.json
```

**Solutions:**

1. **Check file path:**
   ```bash
   ls -l morphir-ir.json
   ```

2. **Use absolute path:**
   ```bash
   morphir ir verify /full/path/to/morphir-ir.json
   ```

3. **Check current directory:**
   ```bash
   pwd
   cd /path/to/ir/files
   morphir ir verify morphir-ir.json
   ```

### Malformed JSON Errors

**Problem:**
```
Message: Malformed JSON: 'i' is an invalid start of a value. LineNumber: 6
```

**Solutions:**

1. **Validate JSON syntax** with a JSON validator:
   ```bash
   cat morphir-ir.json | jq .
   ```

2. **Check for common issues:**
   - Missing commas between array/object elements
   - Trailing commas (not allowed in JSON)
   - Unquoted keys or values
   - Invalid escape sequences

3. **Use a JSON formatter:**
   ```bash
   cat morphir-ir.json | jq . > morphir-ir-formatted.json
   ```

### Schema Version Detection Issues

**Problem:** Auto-detection selects the wrong schema version.

**Solutions:**

1. **Explicitly specify version:**
   ```bash
   morphir ir verify --schema-version 3 morphir-ir.json
   ```

2. **Check formatVersion field** (for v2/v3):
   ```bash
   cat morphir-ir.json | jq '.formatVersion'
   ```

3. **Verify IR structure:**
   - **v1**: No `formatVersion` field
   - **v2**: `"formatVersion": 2`
   - **v3**: `"formatVersion": 3`

### Validation Fails but IR Appears Correct

**Problem:** Validation reports errors, but you believe the IR is valid.

**Investigation Steps:**

1. **Review error messages carefully** - they include Expected vs. Found values
2. **Check schema documentation** - [Schema Specifications](/docs/spec/schemas/)
3. **Validate against different versions:**
   ```bash
   morphir ir verify --schema-version 1 morphir-ir.json
   morphir ir verify --schema-version 2 morphir-ir.json
   morphir ir verify --schema-version 3 morphir-ir.json
   ```

4. **Compare with known-good IR:**
   ```bash
   # Validate a reference file
   morphir ir verify reference-morphir-ir.json
   ```

5. **Check for common issues:**
   - Incorrect tag capitalization (e.g., `"Public"` vs `"public"`)
   - Missing required fields
   - Incorrect array structure
   - Type mismatches

## Configuration

The shared libraries and `morphir config get/list/show/path/explain` provide multi-format resolution, bounded discovery and inspection. Configuration editing, classic project migration and bounded workspace migration use shared plan/apply services. Existing artifact/cache tooling retains TOML cache settings and Git-first discovery. See the [guide]({{< relref "/guides/configuration" >}}) for the library APIs and the [command reference]({{< relref "/docs/cli/config" >}}) for read/write options and recovery semantics.

### Wrong workspace or missing project

For a library host, inspect `ProjectDiscoveryResult.Discovery`, `Selection`, source reports, and diagnostics. Check the discovery boundary, the nearest declared `workspace`, member globs/exclusions, and the selected project. Use `morphir config path --json` to inspect this evidence. A project file does not hide a parent workspace. A Git root or an empty `.morphir` directory does not declare a workspace through the new API. If artifact-management commands use a different root, check their legacy Git-first policy before changing the declaration.

### Ambiguous configuration files

Read every path named in the diagnostic. Keep at most one ordinary primary per format group across the root, hidden, and dot-config layouts. For example, `morphir.yaml` and `morphir.yml` conflict, as do `morphir.toml` and `.morphir/morphir.toml`. JSON, YAML, and TOML may coexist across groups. Supplemental `morphir-workspace.json` may coexist with the ordinary JSON primary.

### A member is missing or cannot be selected

Inspect `workspace.root`, `members`, and `exclude` in the declared workspace. A missing literal member warns; an existing directory without configuration and an invalid member document remain identifiable as errors. Duplicate display names do not merge projects. Select the canonical member path to disambiguate. An explicit unknown name/path or an invalid `default_member` is an error, rather than a request to choose an arbitrary project.

### A candidate was not read

A report of `Missing` means the optional path was absent. `Failed` retains a parse/access diagnostic and must not be treated as a miss. Discovery never follows discovered links, including a linked configuration directory. Check the host boundary and mount permissions. Inactive profiles, personal files under isolation, and machine sources under isolation are intentionally not read.

### A machine file is ambiguous

Global candidates include the platform configuration directory and the alternate Morphir home. Two files in the same format group conflict even when they are in different directories. `MORPHIR_HOME` replaces only the home alternative. A relative `XDG_CONFIG_HOME` is ignored. Requesting non-isolated machine loading without `MachineTree` produces `discovery.machine_host_required`; pass a separately authorized tree together with the explicit machine locations. Neither API implies ambient machine-file access. Remove the unintended duplicate or use different format groups when deliberate layering is required.

### A member output setting has no effect

`workspace.out_dir` belongs to the owning workspace. Member output overrides produce diagnostics and cannot relocate it. Read the separate owning workspace context rather than expecting the root's `workspace` subtree to be inherited into the member document.

### A setting still has its old value after an edit

For current hosts, inspect the resolved origins and source reports. Use `morphir config explain <key>` to identify contributing layers and the winning source. A successful scoped write can remain overridden by a member, profile, personal file, environment variable, or explicit CLI value. Check the target reported by the edit and distinguish scoped inspection from effective inspection.

### A value reappears after unset

`unset` removes the entry only from its target TOML file. A lower-format file, inherited scope, or default can supply it again. Use `get <key> --show-origin` to find the newly effective source. Removing an override does not delete the inherited setting.

### A profile did not load

Check the supplied `ProfileSelection`, `MORPHIR_PROFILES`, and base profile controls. The CLI equivalents are `--profiles` and `--no-profiles`. An empty profile environment selector disables profiles, and inactive files are not read. Named profiles are not activated by CI detection alone. Profile stems must be adjacent to an eligible primary layout; a dot-config primary uses `config.profile.<name>.<ext>`. Isolation retains active profiles while omitting machine and personal sources. For the existing loader's separate CI behavior, inspect `CiProfileMode` and `.morphir/morphir.ci.toml`.

### A scoped key is missing or a value is redacted

A scope reads only that source group. A workspace-scoped miss does not inherit a global value. Compare `get <key> --workspace` with `get <key> --show-origin`. Empty strings and arrays are present and exit 0; an absent queried key exits 1. Quote a segment containing a literal dot, for example `'codegen."custom.generator".enabled'`.

`[REDACTED]` is safe presentation for sensitive names and opaque secret references. Inspection never executes a secret command. Parser diagnostic messages are sanitized because they can contain values; use diagnostic codes, positions and source paths to locate the problem. `--json` returns structured diagnostics while stderr retains safe human diagnostic text.

### Microsoft configuration projection is rejected

The optional adapter rejects case-distinct paths, literal `:` separators and empty segments that cannot retain their canonical identity in .NET's hierarchical view. Use the typed canonical result to inspect these names; registration must not silently overwrite them. See the [adapter reference]({{< relref "/docs/configuration/microsoft" >}}) for projection diagnostics, generated array binding and published host qualification. Pinned dependency linker warnings remain visible; successful smoke execution is not a warning-free claim.

### A configuration write fails

Check the reported target, permissions, parse/validation diagnostics, and whether another process changed the file. Explicit YAML/JSON file targets are readable but not writable through these commands; select a scope with a TOML target. For a concurrent-change conflict, reload and review the new contents before retrying. Failed validation or a write conflict must leave the file unchanged.

`--json` changes output only; it does not infer input types. Use `--type bool` for `ui.color`, `--type int` for an integer, or complete JSON arrays/objects with their explicit type. Nulls, duplicate object keys and out-of-range integers cannot be written as these TOML edit values. A missing unset exits 1 even when another source supplies the effective value.

At a workspace root, an automatic edit targets the workspace primary. `--user` follows the selected project, including `default_member`. Use the reported target before applying a preview. A successful root write can remain overridden by a selected member, profile, personal file or environment source; read the nested effective explanation and winning origins.

A stale plan includes more than a changed destination. New alternate primaries, changed lower-format sources, active-profile inputs and relevant directory inventories also invalidate it. Reload and review before retrying. Identical writes do not touch the timestamp.

Native trees do not follow links. On macOS `/etc` is a conventional symlink, so a default system edit can fail before writing. An explicitly authorized physical TOML path avoids that alias; do not relax a discovery boundary or follow an unrelated link to hide the error.

`rolled-back` is a failure with the original target restored and owned temporary data removed. `recovery-required` identifies retained stage, backup, journal or directory artifacts with their transaction owner. Preserve them and inspect the target/journal before restoring a matching backup. A cleanup failure can retain artifacts after the data outcome is already known. Do not assume an I/O failure means unchanged bytes, and do not delete recoverable data or another transaction's artifacts. Native coordination is machine-local; a multi-machine store requires a host-supplied coordination protocol. Per-file atomic replacement does not imply multi-file group atomicity.


### A classic project migration is blocked

Use `morphir project migrate morphir.json --dry-run` to inspect mappings, target conflicts, and verification results. Resolve conflicting TOML values deliberately. Nonempty legacy dependency or decoration structures need supported mappings; migration must not discard them merely because compatibility loading ignores them. An equivalence failure can also indicate a missing legacy default or changed relative-path meaning.

Validation and stale-plan failures leave both files unchanged. Verification excludes the classic source and ambient machine, personal, environment, CLI, and activated-profile overlays, so success demonstrates that the new file stands on its own in the declared base workspace context. Keeping `morphir.json` afterward is intentional for older consumers; it is not a failed migration. See the [project command reference]({{< relref "/docs/cli/project" >}}).

### Workspace migration has a blocked member or needs recovery

Use `morphir workspace migrate <directory> --dry-run` to inspect the bounded inventory and all member plans. Exclusions and nested workspaces constrain what can be adopted. A conflict in one selected project blocks the batch before destination replacement.

If a commit fails after some replacements, inspect the reported restored/unresolved paths and retained recovery journal. Overlapping writes stay blocked until recovery is resolved. Do not delete recovery data to force another run. Successful application and complete rollback remove temporary transaction artifacts. See the [workspace reference]({{< relref "/docs/cli/workspace" >}}).

## Performance Issues

### Validation Takes Too Long

**Problem:** Validation of large IR files (>1MB) takes several seconds.

**Current Limitations:**
- This is expected behavior for large files in Phase 1
- Schema validation is inherently slower for complex, deeply-nested JSON

**Workarounds:**

1. **Use `--quiet` mode** to reduce output overhead:
   ```bash
   morphir ir verify --quiet morphir-ir.json
   ```

2. **Validate in parallel** (for multiple files):
   ```bash
   find . -name "*.json" | xargs -P 4 -I {} morphir ir verify --quiet {}
   ```

3. **Phase 2 improvements** (coming soon):
   - Performance optimizations for batch processing
   - Caching and incremental validation

### High Memory Usage

**Problem:** Validation consumes excessive memory for very large IR files.

**Solutions:**

1. **Check file size:**
   ```bash
   ls -lh morphir-ir.json
   ```

2. **For files >10MB:**
   - Consider splitting into smaller modules
   - Report the issue with file size details

3. **Increase available memory** (if using Docker):
   ```bash
   docker run --memory 2g morphir-cli ir verify morphir-ir.json
   ```

## CI/CD Integration Issues

### CI Pipeline Doesn't Fail on Invalid IR

**Problem:** Pipeline continues even when validation fails.

**Solution:** Check exit codes explicitly:

```yaml
# GitHub Actions
- name: Validate IR
  run: |
    morphir ir verify morphir-ir.json
    if [ $? -ne 0 ]; then
      echo "Validation failed"
      exit 1
    fi
```

Or use `set -e` in bash scripts:

```bash
#!/bin/bash
set -e  # Exit on any error

morphir ir verify morphir-ir.json
echo "Validation succeeded!"
```

### JSON Output Not Parsed Correctly

**Problem:** CI tools can't parse JSON output.

**Solutions:**

1. **Verify JSON format:**
   ```bash
   morphir ir verify --json morphir-ir.json | jq .
   ```

2. **Save to file:**
   ```bash
   morphir ir verify --json morphir-ir.json > validation-result.json
   ```

3. **Check for extra output:**
   - Use `--quiet` with `--json` to suppress non-JSON output
   - Some logging may appear on stderr

### Permission Denied in Docker

**Problem:**
```
Permission denied: /app/morphir-ir.json
```

**Solution:** Fix file permissions or use volume mounts:

```dockerfile
# In Dockerfile
RUN chmod +r /app/*.json

# Or when running
docker run -v $(pwd):/app:ro morphir-cli ir verify /app/morphir-ir.json
```

## Error Message Reference

### Common Validation Errors

#### Missing Required Field

```
Path: $.distribution
Message: Required properties ["formatVersion"] are not present
Expected: required property
Found: undefined (missing)
```

**Fix:** Add the missing field to your JSON:
```json
{
  "formatVersion": 3,
  "distribution": [ ... ]
}
```

#### Type Mismatch

```
Path: $.modules[0].name
Message: Value is "string" but should be "array"
Expected: array
Found: string
```

**Fix:** Change the field type:
```json
// Incorrect
"name": "MyModule"

// Correct
"name": ["my", "module"]
```

#### Invalid Tag Value

```
Path: $.modules[0].accessControl
Message: Value must be one of: ["Public", "Private"]
Expected: "Public" or "Private"
Found: "public"
```

**Fix:** Use correct capitalization:
```json
// Incorrect
"accessControl": "public"

// Correct
"accessControl": "Public"
```

#### Array Structure Error

```
Path: $.distribution[2]
Message: Expected array to have exactly 4 elements
Expected: array of length 4
Found: array of length 3
```

**Fix:** Ensure array has the correct number of elements per schema.

## Reporting Issues

If you encounter an issue not covered here:

1. **Check existing issues:** [GitHub Issues](https://github.com/finos/morphir-dotnet/issues)

2. **Gather information:**
   - Morphir .NET CLI version: `morphir --version`
   - .NET SDK version: `dotnet --version`
   - Operating system and version
   - Complete error message
   - Minimal reproduction steps

3. **Create a new issue** with:
   - Clear title describing the problem
   - Steps to reproduce
   - Expected vs. actual behavior
   - Environment information
   - Sample IR file (if possible) or minimal example

## Management Commands (dist/tool/extension)

### No Active Version Set

**Problem:** Running `morphir dist which` or `morphir tool which <name>` shows "No active version set".

**Solution:**

1. **List installed versions:**
   ```bash
   morphir dist list
   morphir tool list
   ```

2. **Set an active version:**
   ```bash
   # Global
   morphir dist use <version>
   morphir tool use <name> <version>
   
   # Local (project-specific)
   morphir dist use <version> --local
   morphir tool use <name> <version> --local
   ```

### Platform Not Supported

**Problem:** Installation fails with "Platform not supported" or downloaded artifact doesn't work.

**Solution:**

1. **Check current platform:**
   ```bash
   dotnet --info | grep RID
   ```

2. **Manually specify platform:**
   ```bash
   morphir dist install <url> <version> --platform linux-x64
   ```

3. **Verify available platforms:** Check the distribution documentation for supported platforms.

### Local vs Global Confusion

**Problem:** Setting a version locally but it's not being used.

**Solution:**

- **Remember precedence:** Local (`.morphir/`) overrides global (`~/.config/morphir/`)
- **Check both scopes:**
  ```bash
  morphir dist which          # Shows resolved version (local > global)
  morphir dist list           # Shows global installations
  morphir dist list --local   # Shows local installations
  ```
- **Clear local selection:** Remove the active selection file in `.morphir/dist/active` or `.morphir/tools/active-<name>`

### Installation Directory Not Created

**Problem:** Installation fails because parent directories don't exist.

**Solution:**

The CLI should create directories automatically. If this fails:

1. **Manually create directories:**
   ```bash
   # Global
   mkdir -p ~/.config/morphir/{dist,tools,extensions}
   
   # Local
   mkdir -p .morphir/{dist,tools,extensions}
   ```

2. **Check permissions:**
   ```bash
   ls -ld ~/.config/morphir
   ```

### Downloaded Artifact Is Corrupted

**Problem:** Installation completes but artifacts don't work or are corrupted.

**Solution:**

1. **Check manifest for hash:**
   ```bash
   cat ~/.config/morphir/dist/<platform>/<version>/manifest.json
   ```

2. **Verify download integrity** (if SHA256 is in manifest):
   ```bash
   sha256sum ~/.config/morphir/dist/<platform>/<version>/bin/artifact
   ```

3. **Re-download:**
   ```bash
   morphir dist remove <version>
   morphir dist install <url> <version>
   ```

## Compile failures

Use `--json` and read `failureKind` and `diagnostics`, or `--json-lines` and read the last `completed`
record. Exit code 1 is a source or model error; exit code 2 is an invocation, configuration, selection or
I/O failure. See [compile]({{< relref "/docs/cli/compile" >}}).

| Code | Cause | Fix |
| --- | --- | --- |
| `compile.frontend_unavailable` | No registered frontend reads the language. The AOT `morphir` has no F# frontend. | Use `dotnet-morphir`, which has a built-in F# frontend. |
| `compile.extension_unavailable` | A frontend extension is configured, and this build has no extension transport. | Remove `--extension` and `[frontend.<language>].extension`. MEP extensions are the second delivery. |
| `compile.unsupported_ir_version` | No writer declares the requested IR version, for example `4.1.0-draft.1`. | Use a declared version: `1`, `2`, `3`, `4` or `4.0.0`. |
| `compile.ir_version_not_served` | The selected frontend does not produce the requested IR version. | Request a version that the message lists for the frontend. |
| `compile.language_required` | No language is selected, and the sources do not select one. A `[frontend.<language>].project_file` is read only for the selected language. | Use `--language`, or set `frontend.language`. |
| `compile.package_required` | No package name. | Use `--package-name`, or set `project.name`. |
| `compile.unsupported_setting` | An option or setting that this build does not implement, for example `--types-only` or `ir.layout = "document-tree"`. | Remove it. The message names the option or the key and its file. |
| `compile.invalid_arguments` naming `--target` or `--generate-output` | `--target` without `--generate-output`, `--target` more than one time, or `--generate-output` or `--option` without `--target`. | Give one `--target` with `--generate-output <directory>`. `-o` installs the IR; it is not the generation output. |
| `incompatible-model` after a successful compile with `--target` | The backend of the target does not declare the IR version. The IR stays published. | Use an IR version that the message lists for the backend, or run `generate` later with another target. |
| `compile.generation_unavailable` | `--target` in a build that registers no generation backends. | Use `dotnet-morphir` or `morphir`, which both register the F# and C# backends, or run `generate` on the published IR. |
| `sources.conflicting_source_options` | `--project-file` and `--input` together. | Use one of them. |
| `sources.project_file_as_input` | An `--input` file is a project file. | Use `--project-file`. |
| `sources.unknown_project_file` | No project-file provider recognizes the file. | Name a supported project file. See [project-file sources]({{< relref "/docs/configuration/project-file-sources" >}}). |
| `sources.no_documents` | The source directory has no file with an extension that the frontend claims. | Set `project.source_directory`, or use `--input`. |
| `sources.linked`, `sources.invalid_encoding` | A source is a link or a special file, or it is not valid UTF-8. | Name the target file, or save the file as UTF-8. |

If F# reports that a module or value is not defined, check the source order. A directory gives files in
lexical order, and F# compiles files in order. Use `--input` files in dependency order, or a project file.

## Generation failures

`morphir generate` requires explicit input, target and output paths. It does not
read `morphir.toml` or `morphir.yml`. Use exact target `fsharp` and Boolean option
`--option optimize=false` to disable folding. Unknown options and quoted strings
where Booleans are required return exit 2.

Use JSON/Ion bare IR, or an Ion tree rooted at `manifest.ion`. YAML models,
JSON/YAML trees and MEP envelopes have explicit unsupported-format/profile
diagnostics. A codec error identifies the input or tree member and logical
location. A semantic refusal returns exit 1 and preserves previous output.

Keep input and output paths separate and remove links from output paths.
Linked ancestors are also refused. On macOS, system temporary paths under
`/var` can pass through an OS-provided link. Use the physical path or, in CI,
a runner-owned temporary directory for model fixtures and generated output.
Publication owns only paths listed in `.morphir-generated.json`. Staging
failures preserve previous output; commit-time failures can leave mixed files.
Fix the reported filesystem problem and rerun generation. In `--json` mode,
parse stdout and collect logs from stderr separately.

See [morphir generate](../generate/) for the complete command contract.

## Getting Help

- **Documentation:** [Morphir .NET Docs](/)
- **GitHub Issues:** [finos/morphir-dotnet/issues](https://github.com/finos/morphir-dotnet/issues)
- **Community:** [FINOS Morphir](https://github.com/finos/morphir)

## See Also

- [CLI Reference](../cli/) - Complete command documentation
- [Management Commands](./management/) - Dist, tool, and extension management
- [morphir ir verify](./ir-verify/) - Detailed verification command reference
- [Schema Specifications](/docs/spec/schemas/) - IR schema documentation

## Project migration blocks or cannot write

Run `morphir project migrate --dry-run --json` and inspect `ledger`, `conflicts`,
`verification` and `diagnostics`. Unsupported nonempty classic shapes and values
TOML cannot represent must be reconciled explicitly. A conflicting target is
never overwritten, even when an ambient override makes normal loading look equal.
Use a supported same-project primary layout and retain one TOML primary.

A stale plan requires a fresh preview. Source, target, alternate-primary and base
workspace evidence must still match before installation. Exit 2 also covers
write failures. A `rolled-back` outcome restored the original target; a
`recovery-required` outcome names owned journal, stage and backup artifacts.
Preserve them and later user edits until manual recovery is resolved. Migration
retains the original classic source and never edits workspace membership.


## Workspace migration blocks or needs recovery

Run `morphir workspace migrate <directory> --dry-run --json` and inspect
`inventory`, `projects`, `diagnostics`, and `verified`. The selected directory is
the boundary even if it has no workspace. A child invocation does not adopt its
parent or siblings. Excluded candidates, default skipped directories, links,
and nested workspace boundaries appear in the inventory. Include/exclude controls
are root relative and repeatable; existing workspace exclusions stay effective.

One unsupported classic field or conflicting TOML value blocks the entire batch.
Resolve the reported project blocker and create a fresh preview. Migration retains
classic JSON bytes, uses the single-project loss ledger, and excludes ambient
personal/machine/profile/environment settings from equivalence checks. An adoption
effect for shared output roots does not permit source-directory or setting drift.
Generated artifacts remain where they are.

A conflict means input, target, listing, or topology evidence changed after
planning. Replan after reviewing that change. Do not weaken filesystem boundary or
link guards. On macOS use physical `/private/tmp` rather than its linked aliases.

`recovery-required` is unfinished work. Preserve all listed journals, stages,
backups, and user-modified targets. Overlapping configuration edits, project
migration, and workspace migration remain blocked until explicit recovery resolves
the journal. `uncertainDirectories` identifies directories whose creation receipt
was interrupted. Do not delete them even if they are empty. Explicit recovery can
preserve reviewed uncertain directories and reports them in `preservedDirectories`.
Inspect the shared transaction recovery report before choosing a recovery action. It identifies restored and unresolved paths; unresolved user
changes are preserved. See [workspace API and recovery]({{< relref
"/docs/configuration/workspace-migration" >}}) for the supported API. There is no
claim of cross-file crash atomicity, and silently deleting artifacts is not
recovery.

Every JSON result, including parse or host-initialization failure, follows the
[workspace result schema](https://github.com/finos/morphir-dotnet/blob/main/docs/spec/schemas/workspace-migration-result.schema.json).
Exit 0 means applicable preview, success, or valid-workspace no-op. Exit 2 means
blocked, invalid, failed, or recovery required. Diagnostics go to stderr and stdout
contains one JSON document when `--json` is supplied.
