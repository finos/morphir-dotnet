using System.Collections.Concurrent;
using System.Reflection;

namespace Morphir.Tooling.Infrastructure.JsonSchema;

public class SchemaLoader
{
    private const string SchemaBaseUri = "https://finos.github.io/morphir/schemas";
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

        // Try to parse and register schema in global registry
        // If already registered, retrieve from registry instead
        try
        {
            return Json.Schema.JsonSchema.FromText(jsonContent);
        }
        catch (Json.Schema.JsonSchemaException ex) when (ex.Message.Contains("Overwriting registered schemas"))
        {
            return GetSchemaFromGlobalRegistry(version);
        }
    }

    private static Json.Schema.JsonSchema GetSchemaFromGlobalRegistry(string version)
    {
        var schemaId = new Uri($"{SchemaBaseUri}/morphir-ir-v{version}.yaml");
        var schema = Json.Schema.SchemaRegistry.Global.Get(schemaId);
        if (schema is not Json.Schema.JsonSchema jsonSchema)
            throw new InvalidOperationException($"Schema {schemaId} not found in global registry");
        return jsonSchema;
    }
}
