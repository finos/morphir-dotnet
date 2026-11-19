using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO.Tests;

public sealed class ProjectLoadingBoundaryTests
{
    [Test]
    [Arguments("toml", "[custom]\nvalue='selected'")]
    [Arguments("yaml", "custom: {value: selected}")]
    [Arguments("json5", "{custom:{value:'selected'}}")]
    public void ExplicitNonprimaryFileRetainsItsRoleAndTypedValue(string extension, string contents)
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/settings." + extension] = contents });
        var result = ProjectSystem.Load(tree, ProjectSystem.Discover(tree, "/repo").Discovery,
            options: new ProjectLoadOptions { SelectedPath = "settings." + extension });
        ProjectLoadingTests.Value(result, "custom", "value").Should().Be(ConfigValue.NewString("selected"));
        result.Sources.ToArray().Should().Contain(s => s.Source.Role == SourceRole.Selected && s.Contributed);
    }

    [Test]
    [Arguments("absent.toml", "config.missing")]
    [Arguments("settings.ini", "config.unsupported_format")]
    [Arguments("../outside.toml", "discovery.read_error")]
    public void ExplicitFailuresAreNotOptionalMisses(string path, string code)
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/settings.ini"] = "custom=value" });
        var result = ProjectSystem.Load(tree, ProjectSystem.Discover(tree, "/repo").Discovery,
            options: new ProjectLoadOptions { SelectedPath = path });
        result.Diagnostics.ToArray().Should().Contain(d => d.Code == code);
        tree.ObservedPaths.Should().OnlyContain(p => p == "/repo" || p.StartsWith("/repo/"));
    }

    private sealed class FaultTree(ProjectEntry entry) : IProjectTree
    {
        private readonly InMemoryProjectTree _tree = new("/repo", new Dictionary<string, string> { ["/repo/morphir.toml"] = "[project]\nname='App'" });
        public string Boundary => "/repo";
        public ProjectEntry Observe(string path) => path == "/repo/morphir.user.toml" ? entry : _tree.Observe(path);
    }

    [Test]
    [Arguments("failure")]
    [Arguments("directory")]
    [Arguments("link")]
    public void OptionalPersonalFilesRetainAccessAndLinkEvidence(string kind)
    {
        var entry = kind == "failure" ? ProjectEntry.NewFailure("Denied") : kind == "directory" ? ProjectEntry.NewDirectory(FSharpList<string>.Empty) : ProjectEntry.Link;
        var tree = new FaultTree(entry);
        var result = ProjectSystem.Load(tree, ProjectSystem.Discover(tree, "/repo").Discovery);
        result.Sources.Single(s => s.Source.Path == "/repo/morphir.user.toml").Status.Should().Be(kind == "link" ? SourceStatus.Skipped : SourceStatus.Failed);
    }

    [Test]
    public void MachinePathsCannotGrantAccessWithoutAMachineHost()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>());
        var result = ProjectSystem.Load(tree, ProjectSystem.Discover(tree, "/repo").Discovery, options: new ProjectLoadOptions
        {
            Machine = new MachineLocationInput(MachinePlatform.Linux, "/home/user", null, null, MapModule.Empty<string, string>())
        });
        result.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.machine_host_required");
        tree.ObservedPaths.Should().OnlyContain(p => p == "/repo" || p.StartsWith("/repo/"));
    }

    [Test]
    public void RelativeMorphirHomeUsesTheSuppliedInvocationDirectory()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/alternate/morphir.toml"] = "[custom]\nvalue='home'" });
        var result = ProjectSystem.Load(tree, ProjectSystem.Discover(tree, "/repo").Discovery, options: new ProjectLoadOptions
        {
            Machine = new MachineLocationInput(MachinePlatform.Linux, "/home/user", null, null, MapModule.OfSeq(new[] { Tuple.Create("MORPHIR_HOME", "alternate") })),
            MachineTree = tree
        });
        ProjectLoadingTests.Value(result, "custom", "value").Should().Be(ConfigValue.NewString("home"));
    }

    [Test]
    public void InvalidInventoryEntriesCannotBecomeHealthyLoadTargets()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/morphir.toml"] = "[workspace]\nmembers=['absent']" });
        var discovery = ProjectSystem.Discover(tree, "/repo");
        ProjectSystem.Load(tree, discovery.Discovery, discovery.Discovery.Projects.Single()).Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.invalid_selection");
    }

    [Test]
    [Category("PlatformSpecific")]
    public void NativeLocationCaptureCanReadTheAmbientEnvironmentWithoutLoadingFiles()
    {
        ProjectPath.isAbsolute(ProjectSystem.CaptureMachineLocations().HomeDirectory).Should().BeTrue();
    }

    [Test]
    public void ExplicitCandidateLoaderInvokesActiveProfilesAndSkipsInactiveFiles()
    {
        var active = ConfigurationReader.ReadText("active.toml", "[custom]\nvalue='active'", SourceRole.WorkspaceProfile, profile: "ci");
        var inactive = new SourceIdentity("unused.toml", DocumentFormat.Toml, SourceRole.WorkspaceProfile, 0, FSharpOption<string>.Some("unused"));
        var result = ConfigurationLoader.LoadAndResolve(ProjectLoadingTests.Input(ProfileSelection.NewNames(ListModule.OfSeq(new[] { "ci" }))),
            [new(active.Source, () => active), new(inactive, () => throw new InvalidOperationException("Inactive read"))]);
        ProjectLoadingTests.Value(result, "custom", "value").Should().Be(ConfigValue.NewString("active"));
    }
}
