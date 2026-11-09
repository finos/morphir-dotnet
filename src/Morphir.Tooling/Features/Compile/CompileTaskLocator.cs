// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;

namespace Morphir.Tooling.Features.Compile;

/// <summary>Where the compile task of one project writes its output: the out root, the member path and the Morphir home.</summary>
/// <param name="OutRoot">The out root of the workspace.</param>
/// <param name="Member">The member path of the project below the out root.</param>
/// <param name="MorphirHome">The Morphir home, which holds the <c>-o</c> install locks, or null.</param>
public sealed record CompileTaskPlace(OutRoot OutRoot, MemberPath Member, string? MorphirHome)
{
    /// <summary>The compile task store of this place.</summary>
    public CompileTaskStore Store(TaskStoreOptions options) =>
        new(OutRoot, Member, options with { MorphirHome = options.MorphirHome ?? MorphirHome });
}

/// <summary>
/// Finds the compile task of a project (REC-1). <c>morphir compile</c> and <c>morphir generate</c> without
/// <c>--input</c> both use it, so both find the same record. The out root is, in this order, <c>--out-dir</c>,
/// <c>MORPHIR_OUT_DIR</c>, the workspace <c>workspace.out_dir</c> and <c>&lt;workspace root&gt;/.morphir/out</c>.
/// The member path is the project directory relative to the workspace root. Without a workspace, the project
/// root (else the working directory) is the workspace root.
/// </summary>
public static class CompileTaskLocator
{
    /// <summary>Finds the compile task of the project that <paramref name="loaded"/> describes.</summary>
    public static Outcome<CompileTaskPlace> Locate(LoadedCompileConfiguration loaded, CompileContext context, string? outDirFlag)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(context);
        var working = CompilePaths.RealPath(context.WorkingDirectory);
        var projectRoot = loaded.ProjectRoot ?? working;
        var workspaceRoot = loaded.WorkspaceRoot ?? projectRoot;
        var outRoot = OutRoot.Resolve(new OutRootRequest(working, workspaceRoot)
        {
            OutDirFlag = outDirFlag,
            Environment = context.Environment,
            ConfiguredOutDir = WorkspaceOutDir(loaded, workspaceRoot) ?? ConfiguredOutDir(loaded),
        });
        if (outRoot is Outcome<OutRoot>.Failure outFailure) return new Outcome<CompileTaskPlace>.Failure(outFailure.Error);
        var member = MemberPath.FromDirectories(workspaceRoot, projectRoot);
        if (member is Outcome<MemberPath>.Failure memberFailure) return new Outcome<CompileTaskPlace>.Failure(memberFailure.Error);
        return new Outcome<CompileTaskPlace>.Success(new CompileTaskPlace(
            ((Outcome<OutRoot>.Success)outRoot).Value, ((Outcome<MemberPath>.Success)member).Value, loaded.MorphirHome));
    }

    /// <summary>
    /// The owning workspace's out directory, relative to the workspace root. Discovery gives it as an absolute
    /// path; <see cref="OutRoot.Resolve"/> refuses a value that leaves the workspace.
    /// </summary>
    private static string? WorkspaceOutDir(LoadedCompileConfiguration loaded, string workspaceRoot) =>
        loaded.WorkspaceOutDirectory is { } directory
            ? Path.GetRelativePath(Path.GetFullPath(workspaceRoot), Path.GetFullPath(directory, workspaceRoot)).Replace('\\', '/')
            : null;

    /// <summary>The project's own <c>workspace.out_dir</c>, when a configuration file sets it.</summary>
    private static string? ConfiguredOutDir(LoadedCompileConfiguration loaded)
    {
        var key = ListModule.OfSeq(new[] { "workspace", "out_dir" });
        if (ConfigTree.tryFind(key, loaded.Effective) is not { Value: ConfigValue.String text }) return null;
        var origin = MapModule.TryFind(key, loaded.Origins);
        return origin is null || origin.Value.Source.Role == SourceRole.Defaults ? null : text.Item;
    }
}
