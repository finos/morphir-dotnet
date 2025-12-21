using TUnit.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Morphir.Server;

namespace Morphir.Server.Tests.Endpoints;

public class ServerInfoTests : IDisposable
{
    private WebApplicationFactory<Program>? _factory;

    public ServerInfoTests()
    {
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        _factory?.Dispose();
    }

    [Test]
    public async Task ServerInfo_Endpoint_Should_Return_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/server/info");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Test]
    public async Task ServerInfo_Endpoint_Should_Contain_Name()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/server/info");
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain("Morphir Server");
    }
}

