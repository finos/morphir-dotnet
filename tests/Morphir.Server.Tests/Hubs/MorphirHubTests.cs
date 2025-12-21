using TUnit.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Morphir.Server;

namespace Morphir.Server.Tests.Hubs;

public class MorphirHubTests : IDisposable
{
    private WebApplicationFactory<Program>? _factory;

    public MorphirHubTests()
    {
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        _factory?.Dispose();
    }

    [Test]
    public async Task MorphirHub_Endpoint_Should_Be_Accessible()
    {
        var client = _factory.CreateClient();
        // SignalR hubs require WebSocket connection, so we just verify the endpoint exists
        // by checking that it doesn't return 404
        var response = await client.GetAsync("/morphirHub");

        // SignalR endpoints typically return 400 Bad Request for GET requests without upgrade header
        // or 404 if not found. We expect something other than 404.
        response.StatusCode.Should().NotBe(System.Net.HttpStatusCode.NotFound);
    }
}

