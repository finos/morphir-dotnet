using TUnit.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Morphir.Server;

namespace Morphir.Server.Tests.Endpoints;

public class UITests : IDisposable
{
    private WebApplicationFactory<Program>? _factory;

    public UITests()
    {
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        _factory?.Dispose();
    }

    [Test]
    public async Task Root_Endpoint_Should_Return_200()
    {
        var client = _factory!.CreateClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Test]
    public async Task Root_Endpoint_Should_Return_HTML()
    {
        var client = _factory!.CreateClient();
        var response = await client.GetAsync("/");

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [Test]
    public async Task Root_Endpoint_Should_Contain_Morphir_Heading()
    {
        var client = _factory!.CreateClient();
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain("Morphir Web UI");
    }

    [Test]
    public async Task Root_Endpoint_Should_Be_Blazor_Server_App()
    {
        var client = _factory!.CreateClient();
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain("Blazor-Server-Component-State");
    }
}
