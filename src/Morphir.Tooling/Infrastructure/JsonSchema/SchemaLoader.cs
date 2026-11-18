using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Morphir.Tooling.Infrastructure.JsonSchema;

/// <summary>
/// The kind of a file of a v4 document tree, with the definition of <c>morphir-ir-v4-document-tree-files.json</c> that
/// states it.
/// </summary>
public enum TreeFileKind
{
    /// <summary><c>manifest.json</c> at the root of the tree: <c>DistributionManifestFile</c>.</summary>
    DistributionManifest,

    /// <summary><c>module.json</c>: <c>ModuleManifestFile</c>.</summary>
    ModuleManifest,

    /// <summary><c>&lt;stem&gt;.type.json</c>: <c>TypeDefinitionFile</c>.</summary>
    TypeDefinition,

    /// <summary><c>&lt;stem&gt;.value.json</c>: <c>ValueDefinitionFile</c>.</summary>
    ValueDefinition,
}

/// <summary>Functions for <see cref="TreeFileKind"/>.</summary>
public static class TreeFileKinds
{
    /// <summary>The name of the definition of the kind in <c>morphir-ir-v4-document-tree-files.json</c>.</summary>
    public static string DefinitionName(this TreeFileKind kind) => kind switch
    {
        TreeFileKind.DistributionManifest => "DistributionManifestFile",
        TreeFileKind.ModuleManifest => "ModuleManifestFile",
        TreeFileKind.TypeDefinition => "TypeDefinitionFile",
        TreeFileKind.ValueDefinition => "ValueDefinitionFile",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind has no definition."),
    };

    /// <summary>
    /// The kind of the file at <paramref name="location"/> (a path from the root of the tree, with <c>/</c>) by its
    /// name, or null for a file that is no manifest, module, type or value file, such as <c>package.json</c>.
    /// </summary>
    public static TreeFileKind? Of(string location)
    {
        var name = location[(location.LastIndexOf('/') + 1)..];
        if (location == "manifest.json") return TreeFileKind.DistributionManifest;
        if (name == "module.json") return TreeFileKind.ModuleManifest;
        if (name.EndsWith(".type.json", StringComparison.Ordinal)) return TreeFileKind.TypeDefinition;
        if (name.EndsWith(".value.json", StringComparison.Ordinal)) return TreeFileKind.ValueDefinition;
        return null;
    }
}

/// <summary>
/// Loads the embedded JSON Schemas of <c>model verify</c>: the schema of each IR format version, 1 to 4, and the
/// schema of each kind of file of a v4 document tree. The files are the parent's (see <c>Schemas/PROVENANCE.md</c>);
/// the changes that evaluation needs are made here, when a schema is loaded.
/// </summary>
public class SchemaLoader
{
    private const string TreeFilesResource = "Morphir.Tooling.Infrastructure.Schemas.morphir-ir-v4-document-tree-files.json";

    private readonly ConcurrentDictionary<string, global::Json.Schema.JsonSchema> _cache = new();

    /// <summary>
    /// The schema of the IR format version <paramref name="version"/>: <c>"1"</c> to <c>"4"</c>, or <c>"3.1.0"</c>, the
    /// parent's schema of a v3 file that says <c>"3.1.0"</c>.
    /// </summary>
    public Task<global::Json.Schema.JsonSchema> LoadSchemaAsync(string version, CancellationToken ct)
    {
        return Task.FromResult(_cache.GetOrAdd(version, LoadSchemaFromEmbeddedResource));
    }

    /// <summary>
    /// The schema of one kind of file of a v4 document tree: the tree-file schema with its definition of the kind as
    /// the root.
    /// </summary>
    public Task<global::Json.Schema.JsonSchema> LoadTreeFileSchemaAsync(TreeFileKind kind, CancellationToken ct)
    {
        return Task.FromResult(_cache.GetOrAdd($"tree-file:{kind.DefinitionName()}", _ => LoadTreeFileSchema(kind)));
    }

    private static global::Json.Schema.JsonSchema LoadSchemaFromEmbeddedResource(string version)
    {
        var resourceName = $"Morphir.Tooling.Infrastructure.Schemas.morphir-ir-v{version}.json";
        var schemaNode = ReadResource(resourceName);

        // Evaluate only the branch of a tagged union whose tag matches; without it the cost of evaluation grows
        // exponentially with the nesting depth of the model. See TaggedUnionGuards.
        TaggedUnionGuards.Apply(schemaNode);
        return Build(schemaNode);
    }

    /// <summary>
    /// The tree-file schema has no root schema, only <c>definitions</c>. The definition of the kind becomes the root
    /// through a <c>$ref</c>; the other members of the root are annotations, so the schema keeps their meaning.
    /// </summary>
    private static global::Json.Schema.JsonSchema LoadTreeFileSchema(TreeFileKind kind)
    {
        var schemaNode = ReadResource(TreeFilesResource);
        if (schemaNode["definitions"] is not JsonObject definitions || definitions[kind.DefinitionName()] is null)
            throw new InvalidDataException($"The schema {TreeFilesResource} has no definition {kind.DefinitionName()}.");

        TaggedUnionGuards.Apply(schemaNode);
        schemaNode["$ref"] = $"#/definitions/{kind.DefinitionName()}";
        return Build(schemaNode);
    }

    private static JsonObject ReadResource(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Schema file not found: {resourceName}");

        using var reader = new StreamReader(stream);
        return JsonNode.Parse(reader.ReadToEnd())?.AsObject()
            ?? throw new InvalidDataException($"Schema is not a JSON object: {resourceName}");
    }

    private static global::Json.Schema.JsonSchema Build(JsonObject schemaNode)
    {
        // Use a local registry to avoid global state pollution and test conflicts
        // Each schema instance gets its own isolated registry for thread safety
        var options = new global::Json.Schema.BuildOptions
        {
            SchemaRegistry = new global::Json.Schema.SchemaRegistry()
        };

        return global::Json.Schema.JsonSchema.FromText(schemaNode.ToJsonString(), options);
    }
}
