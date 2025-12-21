using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Morphir.Server.Tests.TestHelpers;

/// <summary>
/// Helper class for creating test clients with configured test environment
/// </summary>
public static class WebApplicationFactoryHelper
{
    /// <summary>
    /// Creates a WebApplicationFactory with test environment configuration
    /// </summary>
    public static WebApplicationFactory<Morphir.Server.Program> CreateTestFactory()
    {
        return new WebApplicationFactory<Morphir.Server.Program>()
            .WithWebHostBuilder(builder =>
            {
                // Configure test environment
                builder.UseEnvironment("Test");
            });
    }
}

