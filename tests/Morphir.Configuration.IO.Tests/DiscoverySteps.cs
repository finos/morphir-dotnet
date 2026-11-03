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
        ["/repo/app/morphir.json"] = "{name:'App'}",
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
}
