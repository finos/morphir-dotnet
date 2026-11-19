// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Configuration;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Infrastructure;

/// <summary>Spec: discovery-containment, boundary and isolation precedence and validation.</summary>
[Category("PlatformSpecific")]
public class ContainmentTests
{
    private static readonly string Root = ProjectPath.normalize(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "morphir-containment")));
    private static readonly string Workspace = Root + "/workspace";
    private static readonly string Project = Workspace + "/app";

    private static Dictionary<string, string> Env(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static Containment Resolve(string? flagBoundary = null, bool? flagIsolated = null,
        Dictionary<string, string>? environment = null, string? currentDirectory = null)
    {
        var resolution = Containment.Resolve(flagBoundary, flagIsolated, environment ?? Env(), currentDirectory ?? Project);
        resolution.Failure.Should().BeNull();
        return resolution.Value!;
    }

    [Test]
    public async Task NoControls_UseTheFilesystemRootAndNoIsolation()
    {
        var containment = Resolve();

        containment.Boundary.Should().Be(ProjectPath.normalize(Path.GetPathRoot(Project)!).ToUpperInvariantDrive());
        containment.BoundarySource.Should().Be(ContainmentSource.Default);
        containment.Isolated.Should().BeFalse();
        containment.IsolationSource.Should().Be(ContainmentSource.Default);
        await Task.CompletedTask;
    }

    [Test]
    public async Task EnvironmentBoundary_Applies()
    {
        var containment = Resolve(environment: Env((Containment.BoundaryVariable, Workspace)));

        containment.Boundary.Should().Be(Workspace);
        containment.BoundarySource.Should().Be(ContainmentSource.Environment);
        await Task.CompletedTask;
    }

    [Test]
    public async Task FlagBoundary_OverridesTheEnvironment()
    {
        var containment = Resolve(flagBoundary: Project, environment: Env((Containment.BoundaryVariable, Workspace)));

        containment.Boundary.Should().Be(Project);
        containment.BoundarySource.Should().Be(ContainmentSource.Flag);
        await Task.CompletedTask;
    }

    [Test]
    public async Task EmptyEnvironmentBoundary_IsUnset()
    {
        var containment = Resolve(environment: Env((Containment.BoundaryVariable, "")));

        containment.BoundarySource.Should().Be(ContainmentSource.Default);
        await Task.CompletedTask;
    }

    [Test]
    public async Task RelativeEnvironmentBoundary_ResolvesAgainstTheCurrentDirectory()
    {
        var containment = Resolve(environment: Env((Containment.BoundaryVariable, "..")), currentDirectory: Project);

        containment.Boundary.Should().Be(Workspace);
        await Task.CompletedTask;
    }

    [Test]
    public async Task RelativeFlagBoundary_ResolvesAgainstTheCurrentDirectory()
    {
        var containment = Resolve(flagBoundary: "..", currentDirectory: Project);

        containment.Boundary.Should().Be(Workspace);
        await Task.CompletedTask;
    }

    [Test]
    public async Task WindowsNativeBoundary_IsAbsolute()
    {
        if (!OperatingSystem.IsWindows()) return;

        var native = Workspace.Replace('/', '\\');
        var containment = Resolve(environment: Env((Containment.BoundaryVariable, native)));

        containment.Boundary.Should().Be(Workspace);
        await Task.CompletedTask;
    }

    [Test]
    public async Task DriveLetterCase_DoesNotChangeTheBoundary()
    {
        if (!OperatingSystem.IsWindows()) return;

        var lower = char.ToLowerInvariant(Workspace[0]) + Workspace[1..];
        var containment = Resolve(environment: Env((Containment.BoundaryVariable, lower)));

        containment.Boundary.Should().Be(Workspace);
        containment.Contains(Project).Should().BeTrue();
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("1", true)]
    [Arguments("true", true)]
    [Arguments("TRUE", true)]
    [Arguments("0", false)]
    [Arguments("false", false)]
    [Arguments("False", false)]
    public async Task EnvironmentIsolation_IsParsed(string value, bool expected)
    {
        var containment = Resolve(environment: Env((Containment.IsolatedVariable, value)));

        containment.Isolated.Should().Be(expected);
        containment.IsolationSource.Should().Be(ContainmentSource.Environment);
        await Task.CompletedTask;
    }

    [Test]
    public async Task BareFlag_IsolatesOverEnvironmentZero()
    {
        var containment = Resolve(flagIsolated: true, environment: Env((Containment.IsolatedVariable, "0")));

        containment.Isolated.Should().BeTrue();
        containment.IsolationSource.Should().Be(ContainmentSource.Flag);
        await Task.CompletedTask;
    }

    [Test]
    public async Task FlagFalse_TurnsEnvironmentIsolationOff()
    {
        var containment = Resolve(flagIsolated: false, environment: Env((Containment.IsolatedVariable, "true")));

        containment.Isolated.Should().BeFalse();
        containment.IsolationSource.Should().Be(ContainmentSource.Flag);
        await Task.CompletedTask;
    }

    [Test]
    public async Task EmptyEnvironmentIsolation_IsUnset()
    {
        var containment = Resolve(environment: Env((Containment.IsolatedVariable, "")));

        containment.Isolated.Should().BeFalse();
        containment.IsolationSource.Should().Be(ContainmentSource.Default);
        await Task.CompletedTask;
    }

    [Test]
    public async Task UnrecognizedIsolationValue_FailsNamingTheVariableAndValues()
    {
        var resolution = Containment.Resolve(null, null, Env((Containment.IsolatedVariable, "yes-please")), Project);

        resolution.Value.Should().BeNull();
        resolution.Failure!.Code.Should().Be(Containment.InvalidValueCode);
        resolution.Failure.Message.Should().Contain("MORPHIR_ISOLATED").And.Contain("yes-please")
            .And.Contain("true").And.Contain("false").And.Contain("1").And.Contain("0");
        await Task.CompletedTask;
    }

    [Test]
    public async Task StartInsideTheBoundary_IsAccepted()
    {
        var containment = Resolve(environment: Env((Containment.BoundaryVariable, Workspace)));

        containment.RequireInside(Project).Should().BeNull();
        containment.RequireInside(Workspace).Should().BeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task StartOutsideTheBoundary_FailsNamingTheBoundaryAndItsSource()
    {
        var containment = Resolve(environment: Env((Containment.BoundaryVariable, Project)), currentDirectory: Workspace);

        var failure = containment.RequireInside(Workspace);

        failure.Should().NotBeNull();
        failure!.Code.Should().Be("discovery.boundary");
        failure.Message.Should().Contain(Project).And.Contain("MORPHIR_DISCOVERY_BOUNDARY").And.Contain(Workspace);
        await Task.CompletedTask;
    }

    [Test]
    public async Task SiblingWithSharedPrefix_IsOutside()
    {
        var containment = Resolve(flagBoundary: Project);

        containment.Contains(Project + "-other").Should().BeFalse();
        containment.RequireInside(Project + "-other")!.Message.Should().Contain("--discovery-boundary");
        await Task.CompletedTask;
    }
}

internal static class DrivePathExtensions
{
    /// <summary>Upper-cases a Windows drive letter, matching the resolver's canonical form.</summary>
    public static string ToUpperInvariantDrive(this string path) =>
        path.Length >= 2 && path[1] == ':' ? char.ToUpperInvariant(path[0]) + path[1..] : path;
}
