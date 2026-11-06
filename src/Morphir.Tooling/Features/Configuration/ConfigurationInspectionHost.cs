// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Tooling.Features.Configuration;

/// <summary>Validated invocation controls passed to the shared loading boundary.</summary>
public sealed record ConfigurationInvocation(QueryScope Scope, string? Project, string? SelectedFile, string? Boundary,
    ProfileOptions Profiles, bool Isolated, string[] SuppressWarnings, IReadOnlyDictionary<string, string> Environment);

/// <summary>Deferred loading edge; command construction and help never invoke this service.</summary>
public interface IConfigurationInspectionHost
{
    ResolutionDetails Load(ConfigurationInvocation invocation);
}

/// <summary>Supplies native filesystem and process inputs to shared discovery and resolution.</summary>
public sealed class NativeConfigurationInspectionHost : IConfigurationInspectionHost
{
    public ResolutionDetails Load(ConfigurationInvocation invocation)
    {
        var start = ProjectPath.normalize(System.Environment.CurrentDirectory);
        var boundary = ProjectPath.normalize(Path.GetFullPath(invocation.Boundary ?? Path.GetPathRoot(start)!));
        // The Morphir home is excluded from discovery even when isolated.
        var tree = new FileSystemProjectTree(boundary,
            ProjectSystem.MorphirHomeDirectory(ProjectSystem.CaptureMachineLocations(invocation.Environment), start));
        var discovered = ProjectSystem.Discover(tree, start, invocation.Project);
        var project = invocation.Scope.IsWorkspace ? null : discovered.Selection.Projects.Count() == 1 ? discovered.Selection.Projects.Head : null;
        var environment = MapModule.OfSeq(invocation.Environment.Select(p => Tuple.Create(p.Key, p.Value)));
        var input = new ResolutionInput(FSharpList<SourceInput>.Empty, environment, DocumentDefaults.emptyValue,
            invocation.Profiles, ProjectScope.Root, null, ListModule.OfSeq(invocation.SuppressWarnings));
        var machine = invocation.Isolated ? null : ProjectSystem.CaptureMachineLocations(invocation.Environment);
        var options = new ProjectLoadOptions
        {
            Input = input,
            Isolated = invocation.Isolated,
            SelectedPath = invocation.SelectedFile,
            Machine = machine,
            MachineTrees = machine is null ? [] : ProjectSystem.MachineBoundaries(machine, start)
                .Select(root => (IProjectTree)new FileSystemProjectTree(root)).ToArray()
        };
        var detailed = ProjectSystem.LoadDetailed(tree, discovered.Discovery, project, options);
        var usesProjectSelection = invocation.Scope.IsEffective || invocation.Scope.IsProject || invocation.Scope.IsUser;
        var selectionErrors = usesProjectSelection ? discovered.Selection.Diagnostics : FSharpList<ConfigDiagnostic>.Empty;
        if (selectionErrors.IsEmpty) return detailed;
        var result = detailed.Resolution;
        return new ResolutionDetails(new DocumentResolution(result.Effective, result.Origins, result.Sources, result.ActiveProfiles,
            result.OwningWorkspace, ListModule.OfSeq(result.Diagnostics.Concat(selectionErrors))), detailed.Layers, detailed.Scope);
    }
}
