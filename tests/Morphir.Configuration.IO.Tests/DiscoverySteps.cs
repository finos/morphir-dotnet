using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

[Binding]
public sealed class DiscoverySteps
{
    private InMemoryProjectTree _tree = null!;
    private ProjectDiscoveryResult _result = null!;

    [Given("a declared workspace above a standalone member")]
    public void GivenWorkspace() => _tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
    {
        ["/repo/morphir.toml"] = "[workspace]\nmembers = ['app']",
        ["/repo/app/morphir.json"] = "{project:{name:'App'}}",
        ["/repo/app/src/input.elm"] = "module Input exposing (..)"
    });

    [When("the host discovers from inside the member")]
    public void Discover() => _result = ProjectSystem.Discover(_tree, "/repo/app/src/input.elm");

    [Then("the owning workspace and member are both identified")]
    public void Identified()
    {
        _result.Discovery.Location.Should().Be("/repo");
        _result.Selection.Projects.Single().Path.Should().Be("/repo/app");
    }
    [Given("a primary at {string} with contents {string}")]
    public void Primary(string path, string contents) => _tree = new InMemoryProjectTree("/repo",
        new Dictionary<string, string> { ["/repo/" + path] = contents }, ["/repo/src"]);

    [When("discovery starts below its enclosing location")]
    public void FromChild() => _result = ProjectSystem.Discover(_tree, "/repo/src");

    [Then("the enclosing project location is selected")]
    public void Enclosing() => _result.Discovery.Location.Should().Be("/repo");

    [Given("conflicting TOML primaries across layouts")]
    public void Conflicts() => _tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
    {
        ["/repo/morphir.toml"] = "[project]",
        ["/repo/.morphir/morphir.toml"] = "[project]",
        ["/repo/app/src/input.elm"] = ""
    });

    [Then("both conflicting paths are diagnosed")]
    public void Ambiguous()
    {
        var message = string.Join(" ", _result.Discovery.Diagnostics.Select(d => d.Message));
        message.Should().Contain("/repo/morphir.toml").And.Contain("/repo/.morphir/morphir.toml");
        _result.Discovery.Diagnostics.ToArray().Should().Contain(d => d.Code == "discovery.ambiguous");
    }
    private DocumentResolution _resolution = null!;

    [Given("a project with an active profile and personal settings")]
    public void ProfileFiles() => _tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
    {
        ["/repo/morphir.toml"] = "[project]\nname='App'\n[profiles]\nactive=['ci']",
        ["/repo/morphir.profile.ci.yml"] = "custom: {value: profile}",
        ["/repo/morphir.user.toml"] = "[custom]\nvalue='personal'",
        ["/repo/morphir.profile.unused.toml"] = "broken TOML"
    });

    [When("the host loads the project in isolation")]
    public void IsolatedLoad()
    {
        var discovery = ProjectSystem.Discover(_tree, "/repo");
        _resolution = ProjectSystem.Load(_tree, discovery.Discovery, options: new ProjectLoadOptions { Isolated = true });
    }

    [Then("only the primary and active profile supply project settings")]
    public void ProfileOnly()
    {
        ProjectLoadingTests.Value(_resolution, "custom", "value").Should().Be(ConfigValue.NewString("profile"));
        _tree.ObservedPaths.Should().NotContain("/repo/morphir.user.toml").And.NotContain("/repo/morphir.profile.unused.toml");
    }
}
