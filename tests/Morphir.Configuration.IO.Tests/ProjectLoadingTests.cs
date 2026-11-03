using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO.Tests;

public sealed class ProjectLoadingTests
{
    internal static ConfigValue? Value(DocumentResolution result, params string[] path) => ConfigTree.tryFind(ListModule.OfSeq(path), result.Effective)?.Value;
    internal static ResolutionInput Input(ProfileSelection? profiles = null, Dictionary<string, string>? env = null) =>
        new(FSharpList<SourceInput>.Empty, MapModule.OfSeq((env ?? []).Select(p => Tuple.Create(p.Key, p.Value))), DocumentDefaults.emptyValue,
            new ProfileOptions(profiles ?? ProfileSelection.Automatic, 32, 4096), ProjectScope.Root, null, FSharpList<string>.Empty);

    [Test]
    public void LoadingAMemberKeepsWorkspaceContextWithoutInheritingRootIdentity()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['app']\nout_dir='generated'\n[project]\nname='Root'\n[custom]\nshared=true",
            ["/repo/app/morphir.toml"] = "[project]\nname='App'\n[workspace]\nout_dir='bad'"
        });
        var discovery = ProjectSystem.Discover(tree, "/repo", "app");
        var result = ProjectSystem.Load(tree, discovery.Discovery, discovery.Selection.Projects.Single());
        Value(result, "project", "name").Should().Be(ConfigValue.NewString("App"));
        Value(result, "custom", "shared").Should().Be(ConfigValue.NewBoolean(true));
        Value(result, "workspace", "members").Should().BeNull();
        result.OwningWorkspace!.Value.OutputDirectory!.Value.Should().Be("/repo/generated");
        result.Diagnostics.ToArray().Should().Contain(d => d.Code == "config.member_output");
    }

    [Test]
    public void MemberPersonalFilesFollowWorkspaceFilesAcrossFormats()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['app']\n[custom]\nvalue='primary'",
            ["/repo/app/morphir.yml"] = "project: {name: App}",
            ["/repo/morphir.user.toml"] = "[custom]\nvalue='workspace personal'",
            ["/repo/app/morphir.user.yml"] = "custom: {value: member personal}"
        });
        var discovery = ProjectSystem.Discover(tree, "/repo", "app");
        var result = ProjectSystem.Load(tree, discovery.Discovery, discovery.Selection.Projects.Single());
        Value(result, "custom", "value").Should().Be(ConfigValue.NewString("member personal"));
    }

    [Test]
    public void SelectedPrimaryContributesOnceAtSelectedLayer()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.json5"] = "{project:{name:'Json'},custom:{value:'json'}}",
            ["/repo/morphir.toml"] = "[custom]\nvalue='toml'"
        });
        var discovery = ProjectSystem.Discover(tree, "/repo");
        var result = ProjectSystem.Load(tree, discovery.Discovery, options: new ProjectLoadOptions { SelectedPath = "morphir.json5" });
        Value(result, "custom", "value").Should().Be(ConfigValue.NewString("json"));
        result.Sources.Count(s => s.Source.Path == "/repo/morphir.json5" && s.Contributed).Should().Be(1);
    }

    [Test]
    public void MachineSourcesNeedAnExplicitAuthorizedHostAndExcludeIdentity()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/morphir.toml"] = "[project]\nname='App'" });
        var machine = new InMemoryProjectTree("/", new Dictionary<string, string>
        {
            ["/etc/morphir/morphir.json"] = "{project:{name:'System'},workspace:{members:['bad']},custom:{value:'system'}}",
            ["/home/user/.config/morphir/morphir.yml"] = "custom: {value: global}",
            ["/home/user/.morphir/morphir.toml"] = "[custom]\nvalue='home'"
        });
        var discovery = ProjectSystem.Discover(tree, "/repo");
        var options = new ProjectLoadOptions { Machine = new MachineLocationInput(MachinePlatform.Linux, "/home/user", null, null, MapModule.Empty<string, string>()), MachineTree = machine };
        var result = ProjectSystem.Load(tree, discovery.Discovery, options: options);
        Value(result, "project", "name").Should().Be(ConfigValue.NewString("App"));
        Value(result, "workspace", "members").Should().BeNull();
        Value(result, "custom", "value").Should().Be(ConfigValue.NewString("home"));
        result.Sources.Where(s => s.Status.IsLoaded && s.Source.Role is SourceRole.System or SourceRole.Global).Select(s => s.Source.Format).Should().Equal(DocumentFormat.Json, DocumentFormat.Yaml, DocumentFormat.Toml);
        machine.ObservedPaths.Should().NotBeEmpty();
    }

    [Test]
    public void GlobalSplitSameFormatSourcesAreAmbiguous()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>());
        var machine = new InMemoryProjectTree("/", new Dictionary<string, string>
        {
            ["/home/user/.config/morphir/morphir.toml"] = "[custom]\nvalue=1",
            ["/home/user/.morphir/morphir.toml"] = "[custom]\nvalue=2"
        });
        var result = ProjectSystem.Load(tree, ProjectSystem.Discover(tree, "/repo").Discovery,
            options: new ProjectLoadOptions { Machine = new MachineLocationInput(MachinePlatform.Linux, "/home/user", null, null, MapModule.Empty<string, string>()), MachineTree = machine });
        result.Diagnostics.ToArray().Should().Contain(d => d.Code == "config.ambiguous_sources" && d.Message.Contains(".config") && d.Message.Contains(".morphir"));
    }

    [Test]
    [Arguments("morphir", "toml", "[project]\nname='App'")]
    [Arguments(".morphir/morphir", "json5", "{project:{name:'App'}}")]
    [Arguments(".config/morphir/config", "yml", "project: {name: App}")]
    public void ProfilesAndUsersUseEligibleStemsOnly(string stem, string extension, string contents)
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/" + stem + "." + extension] = contents,
            ["/repo/" + stem + ".profile.ci.json5"] = "{custom:{value:'profile'}}",
            ["/repo/" + stem + ".user.toml"] = "[custom]\nvalue='personal'",
            ["/repo/morphir.profile.inactive.toml"] = "invalid"
        });
        var result = ProjectSystem.Load(tree, ProjectSystem.Discover(tree, "/repo").Discovery,
            options: new ProjectLoadOptions { Input = Input(ProfileSelection.NewNames(ListModule.OfSeq(new[] { "ci" }))) });
        Value(result, "custom", "value").Should().Be(ConfigValue.NewString("personal"));
        result.ActiveProfiles.ToArray().Should().Equal("ci");
        tree.ObservedPaths.Should().NotContain("/repo/morphir.profile.inactive.toml");
    }

    [Test]
    public void MemberPersonalOutputAttemptIsDiagnosed()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['app']\nout_dir='generated'",
            ["/repo/app/morphir.toml"] = "[project]\nname='App'",
            ["/repo/app/morphir.user.toml"] = "[workspace]\nout_dir='escaped'"
        });
        var discovery = ProjectSystem.Discover(tree, "/repo", "app");
        var result = ProjectSystem.Load(tree, discovery.Discovery, discovery.Selection.Projects.Single());
        result.Diagnostics.ToArray().Should().Contain(d => d.Code == "config.member_output");
        Value(result, "workspace", "out_dir").Should().NotBe(ConfigValue.NewString("escaped"));
    }

    [Test]
    public void IsolationFiltersSuppliedMachineSourcesAndRetainsExplicitInputs()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>());
        var system = ConfigurationReader.ReadText("/etc/morphir/morphir.toml", "[custom]\nmachine=true", SourceRole.System);
        var input = new ResolutionInput(ListModule.OfSeq(new[] { system }), MapModule.OfSeq(new[] { Tuple.Create("MORPHIR_CUSTOM__ENV", "true") }),
            ((SourceContent.Available)ConfigurationReader.ReadText("explicit.json", "{custom:{cli:true}}").Content).Item.Values,
            DocumentDefaults.profileOptions, ProjectScope.Root, null, FSharpList<string>.Empty);
        var result = ProjectSystem.Load(tree, ProjectSystem.Discover(tree, "/repo").Discovery, options: new ProjectLoadOptions { Isolated = true, Input = input });
        Value(result, "custom", "machine").Should().BeNull();
        Value(result, "custom", "env").Should().Be(ConfigValue.NewBoolean(true));
        Value(result, "custom", "cli").Should().Be(ConfigValue.NewBoolean(true));
    }

    [Test]
    public void NativeLocationCaptureUsesExplicitEnvironmentValues()
    {
        var machine = ProjectSystem.CaptureMachineLocations(new Dictionary<string, string>
        {
            [OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME"] = OperatingSystem.IsWindows() ? "C:/host" : "/host",
            ["MORPHIR_HOME"] = "alternate",
            ["XDG_CONFIG_HOME"] = "relative"
        });
        machine.HomeDirectory.Should().Be(OperatingSystem.IsWindows() ? "C:/host" : "/host");
        machine.Environment["MORPHIR_HOME"].Should().Be("alternate");
    }

    [Test]
    public void ExplicitDisabledProfilesDoNotInheritPrematureDiscoveryControlErrors()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/morphir.toml"] = "[project]\nname='App'\n[profiles]\nactive=7" });
        var discovery = ProjectSystem.Discover(tree, "/repo");
        var result = ProjectSystem.Load(tree, discovery.Discovery, options: new ProjectLoadOptions { Input = Input(ProfileSelection.Disabled) });
        discovery.Discovery.Projects.Select(p => p.Name!.Value).Should().Equal("App");
        result.Diagnostics.ToArray().Should().NotContain(d => d.Code.StartsWith("config.profile"));
    }

    [Test]
    public void HostWarningSuppressionAppliesWithoutDiscoveryLeakingPrecomputedWarnings()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.yaml"] = "project: {name: App}",
            ["/repo/morphir.toml"] = "[project]\nname='App'"
        });
        var input = new ResolutionInput(FSharpList<SourceInput>.Empty, MapModule.Empty<string, string>(), DocumentDefaults.emptyValue,
            DocumentDefaults.profileOptions, ProjectScope.Root, null, ListModule.OfSeq(new[] { "config.yaml_beside_toml" }));
        var result = ProjectSystem.Load(tree, ProjectSystem.Discover(tree, "/repo").Discovery, options: new ProjectLoadOptions { Input = input });
        result.Diagnostics.ToArray().Should().NotContain(d => d.Code == "config.yaml_beside_toml");
    }

    [Test]
    public void SuppliedDefaultLayersAreNotAlternativeConfigurationFiles()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>());
        var input = new ResolutionInput(ListModule.OfSeq(new[]{
            ConfigurationReader.ReadText("host-a.json","{custom:{a:1}}",SourceRole.Defaults,0),
            ConfigurationReader.ReadText("host-b.json","{custom:{b:2}}",SourceRole.Defaults,1)}),
            MapModule.Empty<string, string>(), DocumentDefaults.emptyValue, DocumentDefaults.profileOptions, ProjectScope.Root, null, FSharpList<string>.Empty);
        var result = ProjectSystem.Load(tree, ProjectSystem.Discover(tree, "/repo").Discovery, options: new ProjectLoadOptions { Input = input });
        result.Diagnostics.ToArray().Should().NotContain(d => d.Code == "config.ambiguous_sources");
        Value(result, "custom", "a").Should().Be(ConfigValue.NewInteger(1));
        Value(result, "custom", "b").Should().Be(ConfigValue.NewInteger(2));
    }

    [Test]
    [Arguments("user")]
    [Arguments("profile")]
    public void RootOutputOverlaysSupplyTheSameOwningContextToRootAndMember(string overlay)
    {
        var files = new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['app']\nout_dir='old'\n[profiles]\nactive=['ci']",
            ["/repo/app/morphir.toml"] = "[project]\nname='App'",
            ["/repo/" + (overlay == "user" ? "morphir.user.toml" : "morphir.profile.ci.toml")] = "[workspace]\nout_dir='new'"
        };
        var tree = new InMemoryProjectTree("/repo", files);
        var discovery = ProjectSystem.Discover(tree, "/repo", "app");
        var root = ProjectSystem.Load(tree, discovery.Discovery);
        var member = ProjectSystem.Load(tree, discovery.Discovery, discovery.Selection.Projects.Single());
        root.OwningWorkspace!.Value.OutputDirectory!.Value.Should().Be("/repo/new");
        member.OwningWorkspace!.Value.OutputDirectory!.Value.Should().Be("/repo/new");
    }

    [Test]
    public void InvalidOwnerOutputOverlayIsVisibleFromAMemberLoad()
    {
        var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=['app']",
            ["/repo/app/morphir.toml"] = "[project]\nname='App'",
            ["/repo/morphir.user.toml"] = "[workspace]\nout_dir=7"
        });
        var discovery = ProjectSystem.Discover(tree, "/repo", "app");
        var result = ProjectSystem.Load(tree, discovery.Discovery, discovery.Selection.Projects.Single());
        result.Diagnostics.ToArray().Should().Contain(d => d.Code == "config.validation" && d.KeyPath.SequenceEqual(new[] { "workspace", "out_dir" }));
    }
}
