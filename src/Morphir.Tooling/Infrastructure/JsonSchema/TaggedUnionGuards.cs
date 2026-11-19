using System.Text.Json.Nodes;

namespace Morphir.Tooling.Infrastructure.JsonSchema;

/// <summary>
/// Rewrites the tagged unions of a draft-07 Morphir IR schema so that evaluation descends only into the branch whose
/// tag matches.
/// </summary>
/// <remarks>
/// <para>
/// The IR schemas state each tree (<c>Type</c>, <c>Value</c>, <c>Pattern</c>, ...) as a <c>oneOf</c> of tuples whose
/// first item is a <c>const</c> tag, for example <c>["Apply", attributes, function, argument]</c>. A <c>oneOf</c>
/// evaluates every branch, and an evaluator that collects every error evaluates every item of a tuple even after its
/// tag failed. Each node of a model is then evaluated once for each branch that has a subtree at its position, so the
/// cost grows with the number of branches to the power of the nesting depth: the greeting example of finos/morphir
/// (29 KB, depth 30) did not finish in ten minutes.
/// </para>
/// <para>
/// Each branch <c>B</c> with the tag <c>t</c> becomes <c>{ "if": G, "then": B, "else": G }</c>, where <c>G</c> is
/// <c>{ "items": [{ "const": t }] }</c>. The two are equivalent for every instance: when the instance is an array with
/// the tag <c>t</c>, or not an array, or an empty array, <c>G</c> holds and the branch is <c>B</c>; when the instance
/// is an array with another tag, <c>B</c> fails on its tag and so does the branch, with the error of <c>G</c>, which is
/// the same error that <c>B</c> gives for its tag. Because the rewrite happens at load time, the schema files need no
/// change for evaluation; what they take from finos/morphir is recorded in <c>Schemas/PROVENANCE.md</c>.
/// </para>
/// </remarks>
internal static class TaggedUnionGuards
{
    private const string LocalDefinition = "#/definitions/";

    /// <summary>Rewrites every tagged <c>oneOf</c> branch of <paramref name="schema"/> in place.</summary>
    public static void Apply(JsonObject schema)
    {
        var definitions = schema["definitions"] as JsonObject;
        Rewrite(schema, definitions);
    }

    private static void Rewrite(JsonNode? node, JsonObject? definitions)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, child) in obj) Rewrite(child, definitions);
                if (obj["oneOf"] is JsonArray branches) GuardBranches(branches, definitions);
                break;
            case JsonArray array:
                foreach (var child in array) Rewrite(child, definitions);
                break;
        }
    }

    private static void GuardBranches(JsonArray branches, JsonObject? definitions)
    {
        for (var i = 0; i < branches.Count; i++)
        {
            if (branches[i] is not JsonObject branch || TagOf(branch, definitions) is not { } tag) continue;
            branches[i] = new JsonObject
            {
                ["if"] = TagGuard(tag),
                ["then"] = branch.DeepClone(),
                ["else"] = TagGuard(tag),
            };
        }
    }

    /// <summary>
    /// The tag of a branch: the <c>const</c> of the first item of its tuple, read through one local <c>$ref</c>.
    /// </summary>
    private static JsonNode? TagOf(JsonObject branch, JsonObject? definitions)
    {
        var target = branch;
        if (branch.Count == 1 && branch["$ref"] is JsonValue reference
            && reference.TryGetValue<string>(out var pointer)
            && pointer.StartsWith(LocalDefinition, StringComparison.Ordinal))
        {
            if (definitions?[pointer[LocalDefinition.Length..]] is not JsonObject definition) return null;
            target = definition;
        }

        return target["items"] is JsonArray { Count: > 0 } items
            && items[0] is JsonObject first
            && first.TryGetPropertyValue("const", out var tag)
            && tag is not null
                ? tag
                : null;
    }

    private static JsonObject TagGuard(JsonNode tag) =>
        new() { ["items"] = new JsonArray(new JsonObject { ["const"] = tag.DeepClone() }) };
}
