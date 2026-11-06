// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;

namespace Morphir.Configuration.IO.Tests;

/// <summary>Spec: morphir-project-system, "The global Morphir home is not a project location".</summary>
public sealed class MorphirHomeDiscoveryTests
{
    [Test]
    public void ProjectBelowTheHomeDirectory_DoesNotAdoptTheGlobalFile()
    {
        var tree = new InMemoryProjectTree("/", new Dictionary<string, string>
        {
            ["/home/ada/.morphir/morphir.toml"] = "[project]\nname='Global'\n[custom]\nglobal=true",
        }, ["/home/ada/work/app"]);

        var result = ProjectSystem.Discover(tree, "/home/ada/work/app", morphirHome: "/home/ada/.morphir");

        result.Discovery.Root.Path.Should().Be("/home/ada/work/app", "with no primary the host stays at its start location");
        result.Discovery.Root.Sources.ToArray().Should().BeEmpty();
        result.Discovery.Workspace.Should().BeNull();
        result.Selection.Projects.ToArray().Should().BeEmpty();
        result.Discovery.Sources.Where(report => report.Source.Path == "/home/ada/.morphir/morphir.toml").ToArray()
            .Should().OnlyContain(report => report.Status.IsMissing, "the global file is never read as a primary");
    }

    [Test]
    public void WithoutAMorphirHome_TheLayoutIsStillDiscovered()
    {
        // Callers that pass no home keep the existing behaviour.
        var tree = new InMemoryProjectTree("/", new Dictionary<string, string>
        {
            ["/home/ada/.morphir/morphir.toml"] = "[project]\nname='Global'",
        }, ["/home/ada/work/app"]);

        var result = ProjectSystem.Discover(tree, "/home/ada/work/app");

        result.Discovery.Root.Path.Should().Be("/home/ada");
    }

    [Test]
    public void RelocatedMorphirHome_IsNotAProjectLocation()
    {
        var tree = new InMemoryProjectTree("/", new Dictionary<string, string>
        {
            ["/opt/morphir-home/morphir.toml"] = "[workspace]\nmembers=['extensions/*']",
        }, ["/opt/morphir-home/extensions/tool"]);

        var result = ProjectSystem.Discover(tree, "/opt/morphir-home/extensions/tool", morphirHome: "/opt/morphir-home");

        result.Discovery.Root.Path.Should().Be("/opt/morphir-home/extensions/tool");
        result.Discovery.Workspace.Should().BeNull("the relocated home's file is global configuration, not a workspace");
        result.Discovery.Sources.Where(report => report.Source.Path == "/opt/morphir-home/morphir.toml").ToArray()
            .Should().OnlyContain(report => report.Status.IsMissing, "nothing in the home is read as a primary");
        result.Discovery.Root.Sources.ToArray().Should().BeEmpty();
    }

    [Test]
    public void RealProjectDotMorphirLayout_StillWorks()
    {
        var tree = new InMemoryProjectTree("/", new Dictionary<string, string>
        {
            ["/home/ada/.morphir/morphir.toml"] = "[custom]\nglobal=true",
            ["/home/ada/repo/app/.morphir/morphir.toml"] = "[project]\nname='App'",
        }, ["/home/ada/repo/app/src"]);

        var result = ProjectSystem.Discover(tree, "/home/ada/repo/app/src", morphirHome: "/home/ada/.morphir");

        result.Discovery.Root.Path.Should().Be("/home/ada/repo/app");
        result.Selection.Projects.ToArray().Should().ContainSingle();
    }

    [Test]
    public void WindowsHomeWithNativeSeparators_IsRecognized()
    {
        var tree = new InMemoryProjectTree("C:/", new Dictionary<string, string>
        {
            ["C:/Users/ada/.morphir/morphir.toml"] = "[project]\nname='Global'",
        }, ["C:/Users/ada/work/app"]);

        var result = ProjectSystem.Discover(tree, "C:/Users/ada/work/app", morphirHome: @"C:\Users\ada\.morphir");

        result.Discovery.Root.Path.Should().Be("C:/Users/ada/work/app");
    }
}
