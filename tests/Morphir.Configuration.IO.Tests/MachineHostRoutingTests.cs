using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO.Tests;

public sealed class MachineHostRoutingTests
{
    [Test]
    [Arguments("C:/Users/user/AppData/Roaming", "C:/")]
    [Arguments("//server/profiles/user", "//server/profiles")]
    public void MachineLocationsUseTheirOwnAuthorizedVolumes(string roaming, string roamingRoot)
    {
        var project = new InMemoryProjectTree("D:/repo", new Dictionary<string, string>
        {
            ["D:/repo/morphir.toml"] = "[project]\nname='App'"
        });
        var locations = new MachineLocationInput(MachinePlatform.Windows, "C:/Users/user",
            FSharpOption<string>.Some(roaming), FSharpOption<string>.Some("C:/ProgramData"), MapModule.Empty<string, string>());
        var files = new Dictionary<string, string>
        {
            ["C:/ProgramData/morphir/morphir.toml"] = "[custom]\nsystem=true",
            [roaming + "/morphir/morphir.yaml"] = "custom: {roaming: true}",
            ["C:/Users/user/.morphir/morphir.json"] = "{custom:{home:true}}"
        };
        var roots = ProjectSystem.MachineBoundaries(locations, "D:/repo");
        roots.Should().BeEquivalentTo(new[] { "C:/", roamingRoot }.Distinct());
        var hosts = roots.Select(root => new InMemoryProjectTree(root,
            files.Where(p => ProjectPath.contains(root, p.Key)).ToDictionary())).ToArray();
        var result = ProjectSystem.Load(project, ProjectSystem.Discover(project, "D:/repo").Discovery,
            options: new ProjectLoadOptions { Machine = locations, MachineTrees = hosts });

        result.Diagnostics.ToArray().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        foreach (var key in new[] { "system", "roaming", "home" })
            ProjectLoadingTests.Value(result, "custom", key).Should().Be(ConfigValue.NewBoolean(true));
        project.ObservedPaths.Should().OnlyContain(path => ProjectPath.contains("D:/repo", path));
        foreach (var host in hosts)
            host.ObservedPaths.Should().OnlyContain(path => ProjectPath.contains(host.Boundary, path));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void UnixMachineLocationsShareTheFilesystemRoot(bool macOS)
    {
        var locations = new MachineLocationInput(macOS ? MachinePlatform.MacOS : MachinePlatform.Linux,
            "/home/user", null, null, MapModule.Empty<string, string>());
        ProjectSystem.MachineBoundaries(locations, "/repo").Should().Equal("/");
    }

    [Test]
    public void InvalidMachineLocationsCannotCreateRelativeAuthorization()
    {
        var locations = new MachineLocationInput(MachinePlatform.Windows, "relative-home", null, null, MapModule.Empty<string, string>());
        FluentActions.Invoking(() => ProjectSystem.MachineBoundaries(locations, "D:/repo"))
            .Should().Throw<ArgumentException>();
    }

    [Test]
    public void RelativeMorphirHomeUsesTheInvocationVolume()
    {
        var locations = new MachineLocationInput(MachinePlatform.Windows, "C:/Users/user", null, null,
            MapModule.OfSeq(new[] { Tuple.Create("MORPHIR_HOME", "settings") }));
        ProjectSystem.MachineBoundaries(locations, "D:/repo").Should().BeEquivalentTo("C:/", "D:/");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void ExplicitMachineAuthorizationIsNeverWidened(bool isolated)
    {
        var project = new InMemoryProjectTree("D:/repo", new Dictionary<string, string>());
        var host = new InMemoryProjectTree("D:/", new Dictionary<string, string>());
        var locations = new MachineLocationInput(MachinePlatform.Windows, "C:/Users/user", null, null, MapModule.Empty<string, string>());
        var result = ProjectSystem.Load(project, ProjectSystem.Discover(project, "D:/repo").Discovery,
            options: new ProjectLoadOptions { Machine = locations, MachineTrees = [host], Isolated = isolated });
        result.Diagnostics.Any(d => d.Code == "discovery.read_error").Should().Be(!isolated);
        host.ObservedPaths.Should().BeEmpty();
    }
}
