// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;

namespace Morphir.Tooling.Tests.Features.TaskRecords;

/// <summary>Spec: compile-task-records, "Compile output uses the workspace out root".</summary>
[Category("PlatformSpecific")]
public sealed class TaskPathsTests
{
    private static readonly string Workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "morphir-out-root", "workspace"));
    private static readonly string Elsewhere = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "morphir-out-root", "elsewhere"));

    private static OutRoot Resolve(string? flag = null, string? environment = null, string? configured = null)
    {
        var request = new OutRootRequest(Elsewhere, Workspace)
        {
            OutDirFlag = flag,
            Environment = environment is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { [OutRoot.EnvironmentVariable] = environment },
            ConfiguredOutDir = configured,
        };
        var outcome = OutRoot.Resolve(request);
        outcome.Should().BeOfType<Outcome<OutRoot>.Success>();
        return ((Outcome<OutRoot>.Success)outcome).Value;
    }

    [Test]
    public async Task TheFlagWinsOverTheEnvironmentOverTheWorkspaceOverTheDefault()
    {
        Resolve("flag", "env", "build/out").Path.Should().Be(Path.Combine(Elsewhere, "flag"));
        Resolve("flag", "env", "build/out").Source.Should().Be(OutRootSource.Flag);
        Resolve(null, "env", "build/out").Path.Should().Be(Path.Combine(Elsewhere, "env"));
        Resolve(null, "env", "build/out").Source.Should().Be(OutRootSource.Environment);
        Resolve(null, null, "build/out").Path.Should().Be(Path.Combine(Workspace, "build", "out"));
        Resolve(null, null, "build/out").Source.Should().Be(OutRootSource.Workspace);
        Resolve().Path.Should().Be(Path.Combine(Workspace, ".morphir", "out"));
        Resolve().Source.Should().Be(OutRootSource.Default);
        await Task.CompletedTask;
    }

    [Test]
    public async Task AnEmptyEnvironmentValueIsIgnored()
    {
        Resolve(null, "", "build/out").Path.Should().Be(Path.Combine(Workspace, "build", "out"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task AbsoluteFlagAndEnvironmentValuesAreKept()
    {
        var absolute = Path.Combine(Elsewhere, "abs-out");
        Resolve(absolute).Path.Should().Be(absolute);
        Resolve(null, absolute).Path.Should().Be(absolute);
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("../outside")]
    [Arguments("out/../../outside")]
    [Arguments("out\\win")]
    [Arguments("C:/out")]
    [Arguments("/abs/out")]
    [Arguments("")]
    public async Task AWorkspaceOutDirThatLeavesTheWorkspaceIsRefused(string configured)
    {
        var outcome = OutRoot.Resolve(new OutRootRequest(Elsewhere, Workspace) { ConfiguredOutDir = configured });
        var failure = outcome.Should().BeOfType<Outcome<OutRoot>.Failure>().Subject;
        failure.Error.Kind.Should().Be(GenerationFailureKind.Invocation);
        failure.Error.Diagnostics.Single().Code.Should().Be("out.invalid_out_dir");
        await Task.CompletedTask;
    }

    [Test]
    public async Task AWorkspaceMemberPutsItsTaskUnderItsDeclaredPath()
    {
        var member = MemberPath.FromDirectories(Workspace, Path.Combine(Workspace, "packages", "orders"));
        var path = member.Should().BeOfType<Outcome<MemberPath>.Success>().Subject.Value;
        path.Value.Should().Be("packages/orders");

        var location = Resolve().Task(path, TaskId.Compile);
        var expected = Path.Combine(Workspace, ".morphir", "out", "packages", "orders");
        location.Dest.Should().Be(Path.Combine(expected, "compile.dest"));
        location.Record.Should().Be(Path.Combine(expected, "compile.json"));
        location.Lock.Should().Be(Path.Combine(expected, "compile.lock"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task TheRootModuleHasAnEmptyMemberPath()
    {
        var member = ((Outcome<MemberPath>.Success)MemberPath.FromDirectories(Workspace, Workspace)).Value;
        member.Should().Be(MemberPath.Root);
        member.Value.Should().BeEmpty();
        Resolve().Task(member, TaskId.Compile).Dest.Should().Be(Path.Combine(Workspace, ".morphir", "out", "compile.dest"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task AProjectOutsideTheWorkspaceHasNoMemberPath()
    {
        var outcome = MemberPath.FromDirectories(Workspace, Elsewhere);
        outcome.Should().BeOfType<Outcome<MemberPath>.Failure>()
            .Which.Error.Diagnostics.Single().Code.Should().Be("out.member_outside_workspace");
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("compile", true)]
    [Arguments("generate/scala", true)]
    [Arguments("", false)]
    [Arguments("generate/", false)]
    [Arguments("generate/..", false)]
    [Arguments("generate/a\\b", false)]
    [Arguments("C:/x", false)]
    public async Task TaskIdsAreParsedIntoDirectorySegments(string text, bool valid)
    {
        TaskId.TryParse(text, out var id, out var error).Should().Be(valid);
        if (valid) id!.Value.Should().Be(text);
        else error.Should().NotBeNullOrEmpty();
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("morphir-ir.json", true)]
    [Arguments("morphir-ir/Module.json", true)]
    [Arguments("morphir-ir/", true)]
    [Arguments("", false)]
    [Arguments("/abs", false)]
    [Arguments("../up", false)]
    [Arguments("a/./b", false)]
    [Arguments("a//b", false)]
    [Arguments("a\\b", false)]
    [Arguments("C:x", false)]
    public async Task RecordPathsAreRelativeAndMadeOfOrdinarySegments(string text, bool valid)
    {
        RecordPath.TryParse(text, out var path, out var error).Should().Be(valid);
        if (valid) path!.Value.Should().Be(text);
        else error.Should().NotBeNullOrEmpty();
        await Task.CompletedTask;
    }
}
