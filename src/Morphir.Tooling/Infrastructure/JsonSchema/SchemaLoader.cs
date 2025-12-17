using System.Collections.Concurrent;
using System.Reflection;

namespace Morphir.Tooling.Infrastructure.JsonSchema;

public class SchemaLoader
{
    private readonly ConcurrentDictionary<string, Json.Schema.JsonSchema> _cache = new();

    public Task<Json.Schema.JsonSchema> LoadSchemaAsync(string version, CancellationToken ct)
    {
        return Task.FromResult(_cache.GetOrAdd(version, LoadSchemaFromEmbeddedResource));
    }

    private static Json.Schema.JsonSchema LoadSchemaFromEmbeddedResource(string version)
    {
        var resourceName = $"Morphir.Tooling.Infrastructure.Schemas.morphir-ir-v{version}.json";
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Schema file not found: {resourceName}");

        using var reader = new StreamReader(stream);
        var jsonContent = reader.ReadToEnd();

        // Use a local registry to avoid global state pollution and test conflicts
        // Each schema instance gets its own isolated registry for thread safety
        var options = new Json.Schema.BuildOptions
        {
            SchemaRegistry = new Json.Schema.SchemaRegistry()
        };

        return Json.Schema.JsonSchema.FromText(jsonContent, options);
    }
}
