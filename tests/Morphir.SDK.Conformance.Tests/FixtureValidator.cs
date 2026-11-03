using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Morphir.SDK.Conformance.Tests;

/// <summary>Validates evidence and fixture contracts. It never executes SDK operations.</summary>
internal static partial class FixtureValidator
{
    internal static IReadOnlyList<string> Validate(string root, string? corpusText = null,
        string? inventoryText = null, string? provenanceText = null)
    {
        var errors = new List<string>();
        try
        {
            var corpus = Parse(corpusText ?? File.ReadAllText(Path.Combine(root, "cases.json")), "cases.json", errors);
            var inventory = Parse(inventoryText ?? File.ReadAllText(Path.Combine(root, "inventory.json")), "inventory.json", errors);
            var provenance = Parse(provenanceText ?? File.ReadAllText(Path.Combine(root, "provenance.json")), "provenance.json", errors);
            if (errors.Count > 0) return errors;

            ValidateSchema(root, "cases", corpus, errors);
            ValidateSchema(root, "inventory", inventory, errors);
            ValidateSchema(root, "provenance", provenance, errors);
            if (errors.Count > 0) return errors;

            var evidenceIds = ValidateProvenance(root, provenance, errors);
            var operations = Unique(inventory["operations"]!.AsArray(), "operation", "inventory/operations", errors);
            var gaps = Unique(inventory["knownGaps"]!.AsArray(), "id", "inventory/knownGaps", errors);
            var cases = Unique(corpus["cases"]!.AsArray(), "caseId", "cases", errors);
            if (inventory["schemaVersion"]!.GetValue<int>() != 1 || inventory["contractId"]!.ToString() != "morphir-dotnet-sdk-v1")
                errors.Add("inventory/schemaVersion or contractId: unsupported contract");
            foreach (var (id, operation) in operations)
            {
                if (operation["classification"]?.ToString() != "required") errors.Add($"{id}/classification: unclassified operation");
                CheckEvidence(operation["evidence"]!.AsArray(), id, evidenceIds, errors);
                var binding = operation["sourceBinding"]!;
                var source = provenance["sources"]!.AsArray().SingleOrDefault(x => x!["id"]!.ToString() == binding["source"]!.ToString());
                if (source is null || !operation["evidence"]!.AsArray().Any(x => x!.ToString() == binding["source"]!.ToString()))
                    errors.Add($"{id}/sourceBinding: unresolved operation source");
                else
                {
                    var lines = File.ReadAllLines(Path.Combine(root, source["path"]!.ToString()));
                    var line = binding["line"]!.GetValue<int>();
                    if (line > lines.Length || lines[line - 1] != binding["declaration"]!.ToString())
                        errors.Add($"{id}/sourceBinding: declaration does not match pinned source line");
                }
                var required = operation["requiredCaseIds"]!.AsArray().Select(x => x!.ToString()).ToArray();
                if (required.Length == 0 || required.Distinct(StringComparer.Ordinal).Count() != required.Length)
                    errors.Add($"{id}/requiredCaseIds: require nonempty unique coverage");
                foreach (var requiredId in required)
                {
                    if (!cases.TryGetValue(requiredId, out var entry) || entry["operation"]!.ToString() != id || entry["classification"]!.ToString() != "required")
                        errors.Add($"{id}/requiredCaseIds/{requiredId}: missing required case; exclusion or gap cannot replace coverage");
                }
            }
            foreach (var (id, entry) in cases)
            {
                var operationId = entry["operation"]!.ToString();
                if (!operations.TryGetValue(operationId, out var operation))
                {
                    errors.Add($"{id}/operation: unknown operation {operationId}");
                    continue;
                }
                CheckEvidence(entry["evidence"]!.AsArray(), id, evidenceIds, errors);
                foreach (var gap in entry["knownGaps"]!.AsArray())
                    if (!gaps.ContainsKey(gap!.ToString())) errors.Add($"{id}/knownGaps: unknown gap {gap}");
                if (entry["classification"]!.ToString() == "required" && !operation["requiredCaseIds"]!.AsArray().Any(x => x!.ToString() == id))
                    errors.Add($"{id}/classification: required case is absent from the frozen coverage inventory");
            }
            foreach (var id in gaps.Keys)
                if (!cases.Values.Any(x => x["classification"]!.ToString() == "required" && x["knownGaps"]!.AsArray().Any(gap => gap!.ToString() == id)))
                    errors.Add($"{id}/knownGaps: named gap must retain required case coverage");
            foreach (var discrepancy in inventory["discrepancies"]!.AsArray())
                CheckEvidence(discrepancy!["evidence"]!.AsArray(), discrepancy["id"]!.ToString(), evidenceIds, errors);
            FixtureProfiles.Validate(inventory, operations, cases, errors);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or ArgumentException or KeyNotFoundException or NullReferenceException or FormatException)
        {
            errors.Add($"fixture document: {ex.Message}");
        }
        return errors;
    }

    private static void ValidateSchema(string root, string name, JsonNode data, List<string> errors)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(root, name + ".schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry { Fetch = (_, _) => throw new InvalidOperationException("External schema fetch is forbidden by offline validation.") } });
        using var document = JsonDocument.Parse(data.ToJsonString());
        var evaluation = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
        if (evaluation.IsValid) return;
        var located = new List<string>();
        foreach (var detail in InvalidResults(evaluation))
        {
            if (detail.IsValid || detail.Errors is null) continue;
            var pointer = detail.InstanceLocation.ToString();
            var parts = pointer.Split('/');
            var identity = parts.Length > 2 && int.TryParse(parts[2], out var index)
                ? data[parts[1]]?[index]?[parts[1] == "cases" ? "caseId" : parts[1] == "operations" ? "operation" : "id"]?.ToString() ?? name
                : name;
            foreach (var error in detail.Errors) located.Add($"{identity}{pointer}: {error.Key}: {error.Value}");
        }
        errors.AddRange(located.Distinct(StringComparer.Ordinal).Take(100));
        if (located.Count == 0) errors.Add($"{name}: schema validation failed");
    }

    private static IEnumerable<EvaluationResults> InvalidResults(EvaluationResults result)
    {
        if (result.IsValid) yield break;
        yield return result;
        foreach (var detail in result.Details ?? [])
            foreach (var invalid in InvalidResults(detail)) yield return invalid;
    }

    private static JsonNode Parse(string text, string path, List<string> errors)
    {
        using var document = JsonDocument.Parse(text);
        CheckDuplicateProperties(document.RootElement, path, errors);
        return JsonNode.Parse(text)!;
    }

    private static void CheckDuplicateProperties(JsonElement value, string path, List<string> errors)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name)) errors.Add($"{path}/{property.Name}: duplicate JSON property");
                CheckDuplicateProperties(property.Value, $"{path}/{property.Name}", errors);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray()) CheckDuplicateProperties(item, $"{path}/{index++}", errors);
        }
    }

    private static Dictionary<string, JsonNode> Unique(JsonArray entries, string key, string path, List<string> errors)
    {
        var result = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var id = entry?[key]?.ToString();
            if (string.IsNullOrWhiteSpace(id)) errors.Add($"{path}/{key}: missing identity");
            else if (!result.TryAdd(id, entry!)) errors.Add($"{id}/{key}: duplicate identity");
        }
        return result;
    }

    private static HashSet<string> ValidateProvenance(string root, JsonNode provenance, List<string> errors)
    {
        if (provenance["schemaVersion"]!.GetValue<int>() != 1) errors.Add("provenance/schemaVersion: unsupported version");
        var sources = Unique(provenance["sources"]!.AsArray(), "id", "provenance", errors);
        foreach (var (id, source) in sources)
        {
            var revision = source["revision"]!.ToString();
            if (!RevisionRegex().IsMatch(revision)) errors.Add($"{id}/revision: require immutable 40-digit commit");
            if (source["license"]!.ToString() != "Apache-2.0") errors.Add($"{id}/license: unknown license");
            var url = $"https://github.com/{source["repository"]}/blob/{revision}/{source["upstreamPath"]}";
            if (source["url"]!.ToString() != url) errors.Add($"{id}/url: inconsistent pinned source URL");
            var relative = source["path"]!.ToString();
            var absolute = Path.GetFullPath(Path.Combine(root, relative));
            if (!absolute.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !relative.StartsWith("evidence/", StringComparison.Ordinal))
            {
                errors.Add($"{id}/path: source must stay within checked-in evidence");
                continue;
            }
            if (!File.Exists(absolute)) errors.Add($"{id}/path: missing checked-in source {relative}");
            else if (!Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(absolute))).Equals(source["sha256"]!.ToString(), StringComparison.Ordinal))
                errors.Add($"{id}/sha256: checked-in source bytes differ");
            foreach (var field in new[] { "licenseSource", "noticeSource" })
                if (!sources.ContainsKey(source[field]!.ToString())) errors.Add($"{id}/{field}: unresolved attribution");
        }
        return sources.Keys.ToHashSet(StringComparer.Ordinal);
    }

    private static void CheckEvidence(JsonArray evidence, string id, HashSet<string> sourceIds, List<string> errors)
    {
        if (evidence.Count == 0) errors.Add($"{id}/evidence: missing source");
        foreach (var source in evidence)
            if (!sourceIds.Contains(source!.ToString())) errors.Add($"{id}/evidence: unresolved source {source}");
    }

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionRegex();
}
