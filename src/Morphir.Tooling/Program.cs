using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace Morphir.Tooling;

public static class Program
{
    public static IHost CreateToolingHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddWolverine(opts =>
        {
            // Enable in-memory messaging (no external broker needed)
            opts.Services.AddSingleton<Infrastructure.JsonSchema.SchemaLoader>();
            opts.Services.AddSingleton<Infrastructure.JsonSchema.SchemaValidator>();

            // Auto-discover handlers in Features/ directory
            opts.Discovery.IncludeAssembly(typeof(Program).Assembly);
        });

        return builder.Build();
    }
}
