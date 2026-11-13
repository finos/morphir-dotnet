using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Morphir.Tooling.Infrastructure.JsonSchema;

public class SchemaLoader
{
    private readonly ConcurrentDictionary<string, global::Json.Schema.JsonSchema> _cache = new();

    public Task<global::Json.Schema.JsonSchema> LoadSchemaAsync(string version, CancellationToken ct)
    {
        return Task.FromResult(_cache.GetOrAdd(version, LoadSchemaFromEmbeddedResource));
    }

    private static global::Json.Schema.JsonSchema LoadSchemaFromEmbeddedResource(string version)
    {
        var resourceName = $"Morphir.Tooling.Infrastructure.Schemas.morphir-ir-v{version}.json";
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Schema file not found: {resourceName}");

        using var reader = new StreamReader(stream);
        var jsonContent = reader.ReadToEnd();

        // Evaluate only the branch of a tagged union whose tag matches; without it the cost of evaluation grows
        // exponentially with the nesting depth of the model. See TaggedUnionGuards.
        var schemaNode = JsonNode.Parse(jsonContent)?.AsObject()
            ?? throw new InvalidDataException($"Schema is not a JSON object: {resourceName}");
        TaggedUnionGuards.Apply(schemaNode);

        // Use a local registry to avoid global state pollution and test conflicts
        // Each schema instance gets its own isolated registry for thread safety
        var options = new global::Json.Schema.BuildOptions
        {
            SchemaRegistry = new global::Json.Schema.SchemaRegistry()
        };

        return global::Json.Schema.JsonSchema.FromText(schemaNode.ToJsonString(), options);
    }
}
