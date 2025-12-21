using TUnit.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Morphir.Server;

namespace Morphir.Server.Tests.Endpoints;

public class HealthCheckTests : IDisposable
{
    private WebApplicationFactory<Program>? _factory;

    public HealthCheckTests()
    {
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        _factory?.Dispose();
    }

    [Test]
    public async Task Health_Endpoint_Should_Return_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Test]
    public async Task Health_Endpoint_Should_Return_Json()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Test]
    public async Task Health_Endpoint_Should_Contain_Status()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain("Healthy");
    }
}

