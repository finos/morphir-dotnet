using System.Collections.Concurrent;
using System.Reflection;

namespace Morphir.Tooling.Infrastructure.JsonSchema;

public class SchemaLoader
{
    private readonly ConcurrentDictionary<string, Json.Schema.JsonSchema> _cache = new();

    public Task<Json.Schema.JsonSchema> LoadSchemaAsync(string version, CancellationToken ct)
    {
        return Task.FromResult(_cache.GetOrAdd(version, v =>
        {
            // Load embedded JSON schema
            var resourceName = $"Morphir.Tooling.Infrastructure.Schemas.morphir-ir-v{v}.json";
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"Schema file not found: {resourceName}");

            using var reader = new StreamReader(stream);
            var jsonContent = reader.ReadToEnd();

            // Parse as JsonSchema
            var schema = Json.Schema.JsonSchema.FromText(jsonContent);

            // Register the schema in the global registry to handle $ref resolution and circular references
            Json.Schema.SchemaRegistry.Global.Register(schema);

            return schema;
        }));
    }
}
