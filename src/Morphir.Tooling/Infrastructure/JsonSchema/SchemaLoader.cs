using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Morphir.Tooling.Infrastructure.JsonSchema;

public class SchemaLoader
{
    private readonly ConcurrentDictionary<string, Json.Schema.JsonSchema> _cache = new();

    public Task<Json.Schema.JsonSchema> LoadSchemaAsync(string version, CancellationToken ct)
    {
        return Task.FromResult(_cache.GetOrAdd(version, v =>
        {
            // Load embedded YAML schema
            var resourceName = $"Morphir.Tooling.Infrastructure.Schemas.morphir-ir-v{v}.yaml";
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"Schema file not found: {resourceName}");

            using var reader = new StreamReader(stream);
            var yamlContent = reader.ReadToEnd();

            // Convert YAML to JSON
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var yamlObject = deserializer.Deserialize(new StringReader(yamlContent));

            var serializer = new SerializerBuilder()
                .JsonCompatible()
                .Build();

            var jsonContent = serializer.Serialize(yamlObject);

            // Parse as JsonSchema
            return Json.Schema.JsonSchema.FromText(jsonContent);
        }));
    }
}
