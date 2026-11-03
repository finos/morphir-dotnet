---
title: "Shared configuration editing"
linkTitle: "Editing"
weight: 40
description: "Plan one scoped TOML edit, inspect its effective result, and explicitly apply it"
---

# Shared configuration editing

Configuration edits belong to `Morphir.Configuration` and `Morphir.Configuration.IO`. The CLI supplies arguments and renders results. Embedded hosts can supply discovery, typed documents, environment and filesystem evidence, then preview without a writer.

`EditRequest` distinguishes set and unset. Strings are the default. Boolean, integer, number, array and object inputs use explicit typing. Composite inputs are JSON values; JSON output does not imply JSON input. Arrays and objects replace the complete entry in the target. TOML has no null value, so nulls in edit inputs are rejected, including nested array/object nulls. Ordinary configuration readers retain their existing null behavior.

`Editing.selectTarget` selects one destination from captured ownership and inventory. `Editing.plan` produces immutable before/after values, a candidate document, validation, observations and a redacted effective explanation. It performs no I/O. Canonical plan values are for application and must be treated as sensitive data; use the redacted explanation and shared redactor for presentation.

Default edits target the selected project. At a declared workspace root they target the workspace primary, including a virtual workspace. Explicit selectors choose workspace, one project, adjacent personal overrides, global, system or one TOML file. Missing or ambiguous contexts fail. File scopes exclude synthetic default layers.

Personal target selection considers every eligible layout belonging to its owner, not only the directory containing the preferred primary. An existing personal TOML remains the target across mixed primary layouts; multiple personal TOML candidates remain ambiguous. If only a lower-format personal source exists, its corresponding TOML stem is used.

An explicit personal file belonging to another declared member keeps that member's context for validation and effective-value reporting. It remains a member-personal source under isolation, so a workspace output setting in that file cannot relocate the owning workspace.

A scope with only JSON/YAML creates the corresponding TOML primary. Lower-format bytes stay unchanged. Unset removes an authored TOML entry and adds no tombstone. A lower value can reappear with its original origin. A higher environment, personal or profile source can keep winning after a successful file write. The effective explanation makes that visible.

## Presentation and input compatibility

The editor patches TOML tokens and reparses through the existing pinned reader. It verifies the intended typed change and all unrelated values. It preserves unrelated comments, whitespace, ordering, line endings and text. It does not rewrite a document through a serializer. Explicit JSON/YAML targets fail. Syntax acceptance remains the canonical reader's contract; the editor adds no parser package or TOML version upgrade.

## Application and recovery

A preview is read only. Application checks identities of the observed source, destination, candidate inventory and relevant discovery inputs, including absent alternate primaries. A stale observation returns a conflict before replacement. Replan after inspecting the other writer's change. An identical edit does not rewrite the destination.

The transaction service coordinates overlapping cooperating writers, stages on the destination filesystem and installs each file atomically. It does not offer multi-file group atomicity. It retains backups and journal evidence until success or proven rollback. A fully rolled-back fault reports failure and cleans owned temporary data. An incomplete recovery returns a typed recovery-required result with retained artifact paths; preserve those files and reconcile the affected destinations before retrying.

External programs that do not participate in coordination can still race with the final filesystem operation. Preconditions detect observed changes; they are not a filesystem-wide lock on arbitrary editors. Generated-output publication and its manifests are separate and retain their existing guarantees.

## Embedded consumption

The pure API accepts a complete captured `EditTargetContext`, `ResolutionInput` and observation list. It does not access the environment or filesystem:

```fsharp
let request = {
    Operation = EditOperation.Set("false", EditValueType.Boolean)
    Key = "ui.color"
}

let preview =
    Editing.selectTarget [EditScope.Project] targetContext
    |> Result.bind (fun target -> Editing.plan target request resolutionInput observations)

match preview with
| Ok plan -> plan.Effective // redacted effective value, contributions and origins
| Error failure -> failwith failure.Code // typed exit category and safe diagnostics
```

For an IO-backed host, supply authorized trees and storage explicitly:

```csharp
var tree = new FileSystemProjectTree(projectBoundary);
var storage = new NativeConfigurationTransactionHost();
var editor = new ConfigurationEditService(tree, storage);
var request = new EditRequest(
    EditOperation.NewSet("false", EditValueType.Boolean), "ui.color");
var preview = editor.Plan(invocationDirectory, request);

if (preview.Failure is { } failure)
    return failure.ExitCode;

// Display a redacted preview. No writer has been acquired yet.
var explanation = preview.Plan!.Effective;
// Apply only when the host decides to commit this exact preview.
var result = editor.Apply(preview);
```

An in-memory host can implement `IConfigurationTransactionHost` and use `InMemoryProjectTree`. `Observe` must identify the same bytes returned by `ReadFile`; file identities are uppercase SHA-256 hex. Directory identities represent the captured relevant inventory. The host must honor the writer coordination and per-file atomic installation contract. The transaction service copies observations and candidate bytes into immutable collections.

`ConfigurationEditPreview.Failure` carries a code, exit category and safe diagnostics. A successful preview contains both the shared `EditPlan` and immutable `ConfigurationTransactionPlan`. `ConfigurationTransactionResult.Status` distinguishes `Written`, `Unchanged`, `Conflict`, `IoFailure`, `RolledBack` and `RecoveryRequired`. `Artifacts` identifies retained paths, their kind and transaction owner. A retained recovery artifact is not permission to delete it.

Native stages are on the destination filesystem. Native coordination is deliberately coarse, serializing configuration writers through an OS mutex, including writers targeting different scopes. It leaves no lock files in the project. Embedded hosts may provide finer coordination if every overlapping writer participates.
