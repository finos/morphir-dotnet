using FluentAssertions;
using TUnit.Core;
using YamlDotNet.RepresentationModel;

namespace Morphir.Build.Tests;

[Category("PlatformSpecific")]
public class PublishingWorkflowTests
{
    [Test]
    public void Release_UsesTheEnvironmentRequiredByTheNuGetPolicy()
    {
        var release = (YamlMappingNode)ReadWorkflow()["jobs"]["release"];
        release.Children.Should().ContainKey(new YamlScalarNode("environment"));
        release["environment"].ToString().Should().Be("production");
    }

    [Test]
    public void Release_PublishesWithTheTrustedPublishingCredential()
    {
        var workflow = ReadWorkflow();
        var release = (YamlMappingNode)workflow["jobs"]["release"];
        var steps = ((YamlSequenceNode)release["steps"]).Children.Cast<YamlMappingNode>().ToList();
        var publish = steps.Single(step => step.Children.TryGetValue("name", out var name)
            && name.ToString() == "Publish packages to NuGet");
        var login = steps[steps.IndexOf(publish) - 1];

        login.Children.Should().ContainKey(new YamlScalarNode("uses"),
            "publishing must immediately follow a trusted publishing login");
        login["uses"].ToString().Should().StartWith("NuGet/login@");
        var loginId = login["id"].ToString();
        var environment = (YamlMappingNode)publish["env"];
        environment["NUGET_API_KEY"].ToString().Should()
            .Be("${{ steps." + loginId + ".outputs.NUGET_API_KEY }}");
        publish["run"].ToString().Should().Contain("--api-key \"$NUGET_API_KEY\"");
        workflow.ToString().Should().NotContain("secrets.NUGET_KEY").And.NotContain("secrets.NUGET_TOKEN");
    }

    [Test]
    public void OnlyTheReleaseJob_CanRequestAnOidcToken()
    {
        var workflow = ReadWorkflow();
        var permissions = (YamlMappingNode)workflow["permissions"];
        permissions.Children.Should().NotContainKey(new YamlScalarNode("id-token"));

        var jobs = (YamlMappingNode)workflow["jobs"];
        foreach (var (name, node) in jobs.Children)
        {
            var job = (YamlMappingNode)node;
            if (name.ToString() == "release")
            {
                job.Children.Should().ContainKey(new YamlScalarNode("permissions"));
                var jobPermissions = (YamlMappingNode)job["permissions"];
                jobPermissions["id-token"].ToString().Should().Be("write");
                jobPermissions["contents"].ToString().Should().Be("read");
            }
            else if (job.Children.TryGetValue("permissions", out var jobPermissions))
            {
                ((YamlMappingNode)jobPermissions).Children.Should()
                    .NotContainKey(new YamlScalarNode("id-token"));
            }
        }
    }

    private static YamlMappingNode ReadWorkflow()
    {
        using var reader = File.OpenText(Path.Combine(TestFixture.GetRepositoryRoot(),
            ".github", "workflows", "deployment.yml"));
        var yaml = new YamlStream();
        yaml.Load(reader);
        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }
}
