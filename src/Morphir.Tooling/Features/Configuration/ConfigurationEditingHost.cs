// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Tooling.Features.Configuration;

public interface IConfigurationEditingHost
{
    ConfigurationEditReport Execute(ConfigurationInvocation invocation, EditRequest request, bool dryRun);
}

/// <summary>Captures process inputs and delegates editing to the reusable IO boundary.</summary>
public sealed class NativeConfigurationEditingHost : IConfigurationEditingHost
{
    public ConfigurationEditReport Execute(ConfigurationInvocation invocation, EditRequest request, bool dryRun)
    {
        var start = ProjectPath.normalize(System.Environment.CurrentDirectory);
        var boundary = ProjectPath.normalize(Path.GetFullPath(invocation.Boundary ?? Path.GetPathRoot(start)!));
        var machine = ProjectSystem.CaptureMachineLocations(invocation.Environment);
        var input = new ResolutionInput(FSharpList<SourceInput>.Empty,
            MapModule.OfSeq(invocation.Environment.Select(p => Tuple.Create(p.Key, p.Value))), DocumentDefaults.emptyValue,
            invocation.Profiles, ProjectScope.Root, null, ListModule.OfSeq(invocation.SuppressWarnings));
        var options = new ProjectLoadOptions
        {
            Input = input,
            Isolated = invocation.Isolated,
            SelectedPath = invocation.SelectedFile,
            Machine = machine,
            MachineTrees = ProjectSystem.MachineBoundaries(machine, start).Select(root => (IProjectTree)new FileSystemProjectTree(root)).ToArray()
        };
        EditScope[] selectors = invocation.Scope switch
        {
            QueryScope.File file => [EditScope.NewFile(file.Item)],
            _ when invocation.Scope.IsWorkspace => [EditScope.Workspace],
            _ when invocation.Scope.IsProject => [EditScope.Project],
            _ when invocation.Scope.IsUser => [EditScope.User],
            _ when invocation.Scope.IsGlobal => [EditScope.Global],
            _ when invocation.Scope.IsSystem => [EditScope.System],
            _ => []
        };
        var service = new ConfigurationEditService(new FileSystemProjectTree(boundary), new NativeConfigurationTransactionHost());
        var preview = service.Plan(start, request, selectors, invocation.Project, options);
        var operation = request.Operation.IsUnset ? "unset" : "set";
        if (preview.Failure is { } failure) return ConfigurationEditReport.Failure(operation, failure.Code, failure.ExitCode, failure.Diagnostics);
        if (dryRun) return ConfigurationEditReport.From(preview.Plan!, "preview", true);
        var applied = service.Apply(preview);
        var outcome = applied.Status switch
        {
            ConfigurationTransactionStatus.Written => "written",
            ConfigurationTransactionStatus.Unchanged => "unchanged",
            ConfigurationTransactionStatus.Conflict => "conflict",
            ConfigurationTransactionStatus.RolledBack => "rolled-back",
            ConfigurationTransactionStatus.RecoveryRequired => "recovery-required",
            _ => "io-failure"
        };
        var success = applied.Status is ConfigurationTransactionStatus.Written or ConfigurationTransactionStatus.Unchanged;
        return ConfigurationEditReport.From(preview.Plan!, outcome, false, success ? 0 : 2, success ? null : applied.Code,
            applied.Artifacts.Select(a => new ConfigurationEditArtifactDto(a.Path, a.Kind.ToString(), a.TransactionId)).ToArray());
    }
}
