// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Tooling.Features.Configuration;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
ConfigValue Obj(params (string Key, ConfigValue Value)[] fields) => ConfigValue.NewObject(MapModule.OfSeq(fields.Select(f => Tuple.Create(f.Key, f.Value))));
var tag = Obj(("$number", ConfigValue.NewString("NaN")));
var values = Obj(("custom", Obj(
    ("nan", ConfigValue.NewNumber(double.NaN)),
    ("positive", ConfigValue.NewNumber(double.PositiveInfinity)),
    ("negative", ConfigValue.NewNumber(double.NegativeInfinity)),
    ("finite", ConfigValue.NewNumber(1.25)),
    ("integer", ConfigValue.NewInteger(4)),
    ("text", ConfigValue.NewString("NaN")),
    ("tag", tag),
    ("array", ConfigValue.NewArray(ListModule.OfSeq(new[] { ConfigValue.NewNumber(double.NaN), tag, ConfigValue.Null }))),
    ("a/b~c", ConfigValue.NewNumber(double.PositiveInfinity)),
    ("when", ConfigValue.NewTemporal("datetime", "2026-11-03T10:00:00Z")),
    ("password", ConfigValue.NewNumber(double.NaN)),
    ("reference", ConfigValue.NewSecretReference(MapModule.OfSeq(new[] { Tuple.Create("env", ConfigValue.NewString("NEVER_READ")) }))))));
var defaults = DocumentDefaults.resolutionInput;
var details = Resolver.resolveDetailed(new(defaults.Sources, defaults.Environment, values, defaults.Profiles, defaults.Scope, defaults.OwningWorkspace, defaults.SuppressWarnings));
using var show = JsonDocument.Parse(ConfigurationReport.From(Inspection.show(QueryScope.Effective, details), true, true).ToJson());
var root = show.RootElement;
var custom = root.GetProperty("value").GetProperty("custom");
foreach (var pair in new[] { ("nan", "NaN"), ("positive", "Infinity"), ("negative", "-Infinity") })
{
    Check(custom.GetProperty(pair.Item1).GetProperty("$number").GetString() == pair.Item2, pair.Item1);
    Check(root.GetProperty("valueTypes").EnumerateArray().Any(t => t.GetProperty("pointer").GetString() == "/value/custom/" + pair.Item1 && t.GetProperty("kind").GetString() == "number" && t.GetProperty("encoding").GetString() == "nonfinite"), "typed " + pair.Item1);
}
Check(custom.GetProperty("finite").GetDouble() == 1.25, "finite number");
Check(custom.GetProperty("integer").GetInt64() == 4, "integer");
Check(custom.GetProperty("text").GetString() == "NaN", "ordinary string");
Check(custom.GetProperty("tag").GetProperty("$number").GetString() == "NaN", "ordinary tag-shaped object");
Check(!root.GetProperty("valueTypes").EnumerateArray().Any(t => t.GetProperty("pointer").GetString() is "/value/custom/tag" or "/value/custom/array/1"), "user objects have no numeric discriminator");
Check(root.GetProperty("valueTypes").EnumerateArray().Any(t => t.GetProperty("pointer").GetString() == "/value/custom/a~1b~0c"), "JSON Pointer escaping");
Check(root.GetProperty("valueTypes").EnumerateArray().Any(t => t.GetProperty("pointer").GetString() == "/value/custom/array/0"), "nested array number");
Check(custom.GetProperty("array")[2].ValueKind == JsonValueKind.Null, "null array item");
Check(custom.GetProperty("password").GetString() == "[REDACTED]", "redact before serializing numbers");
Check(custom.GetProperty("reference").GetString() == "[REDACTED]", "opaque references");
Check(!root.GetProperty("valueTypes").EnumerateArray().Any(t => t.GetProperty("pointer").GetString() == "/value/custom/password"), "no number metadata for redacted value");
Check(root.GetProperty("valueTypes").EnumerateArray().Any(t => t.GetProperty("pointer").GetString() == "/value/custom/when" && t.GetProperty("kind").GetString() == "temporal" && t.GetProperty("encoding").GetString() == "datetime"), "temporal kind retained");
var path = ListModule.OfSeq(new[] { "custom", "nan" });
using var explain = JsonDocument.Parse(ConfigurationReport.From(Inspection.explain(QueryScope.Effective, path, details), true, true).ToJson());
Check(explain.RootElement.GetProperty("valueTypes").EnumerateArray().Any(t => t.GetProperty("pointer").GetString() == "/contributions/0/value"), "contribution number metadata");
Check(explain.RootElement.GetProperty("valueTypes").EnumerateArray().Any(t => t.GetProperty("pointer").GetString() == "/entries/0/value"), "entry number metadata");
Check(explain.RootElement.GetProperty("exitCode").GetInt32() == 0, "valid number succeeds");
var classicSource = new SourceIdentity("morphir.json", DocumentFormat.Json, SourceRole.RootPrimary, 0, null);
var classic = Normalization.normalize(classicSource, Obj(("name", ConfigValue.NewString("Classic")),
    ("exposedModules", ConfigValue.NewArray(FSharpList<ConfigValue>.Empty))), MapModule.Empty<FSharpList<string>, SourcePosition>());
var classicDetails = Resolver.resolveDetailed(new ResolutionInput(ListModule.OfSeq(new[] { new SourceInput(classicSource, SourceContent.NewAvailable(classic), true) }),
    defaults.Environment, defaults.Explicit, defaults.Profiles, defaults.Scope, defaults.OwningWorkspace, defaults.SuppressWarnings));
var backend = ListModule.OfSeq(new[] { "pipeline", "backend" });
Check(Inspection.get(QueryScope.Effective, backend, classicDetails).Found, "effective classic default remains");
using var fileGet = JsonDocument.Parse(ConfigurationReport.From(Inspection.get(QueryScope.NewFile("morphir.json"), backend, classicDetails), true, true).ToJson());
Check(fileGet.RootElement.GetProperty("exitCode").GetInt32() == 1 && !fileGet.RootElement.GetProperty("found").GetBoolean(), "file query does not fill a synthetic default");
using var fileExplain = JsonDocument.Parse(ConfigurationReport.From(Inspection.explain(QueryScope.NewFile("morphir.json"), backend, classicDetails), true, true).ToJson());
Check(fileExplain.RootElement.GetProperty("contributions").GetArrayLength() == 0, "file explanation omits synthetic layers");
foreach (var depth in new[] { 65, 127 })
{
    ConfigValue nested = Obj(("x", ConfigValue.NewNumber(double.PositiveInfinity)),
        ("password", ConfigValue.NewString("NEVER_PRINT_DEEP_SECRET")));
    for (var level = 0; level < depth; level++) nested = Obj(("a", nested));
    var nestedDetails = Resolver.resolveDetailed(new(defaults.Sources, defaults.Environment, nested,
        defaults.Profiles, defaults.Scope, defaults.OwningWorkspace, defaults.SuppressWarnings));
    var subtree = ListModule.OfSeq(new[] { "a" });
    foreach (var inspection in new[] { Inspection.show(QueryScope.Effective, nestedDetails),
                 Inspection.get(QueryScope.Effective, subtree, nestedDetails),
                 Inspection.explain(QueryScope.Effective, subtree, nestedDetails) })
    {
        var json = ConfigurationReport.From(inspection, true, true).ToJson();
        using var deepReport = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 256 });
        Check(deepReport.RootElement.GetProperty("exitCode").GetInt32() == 0, "supported depth succeeds: " + depth);
        var leaf = deepReport.RootElement.GetProperty("value");
        for (var level = inspection.Operation == "show" ? 0 : 1; level < depth; level++) leaf = leaf.GetProperty("a");
        Check(leaf.GetProperty("x").GetProperty("$number").GetString() == "Infinity", "deep numeric value retained");
        Check(leaf.GetProperty("password").GetString() == "[REDACTED]" && !json.Contains("NEVER_PRINT_DEEP_SECRET", StringComparison.Ordinal), "deep value redacted");
        Check(deepReport.RootElement.GetProperty("valueTypes").EnumerateArray().Any(t =>
            t.GetProperty("pointer").GetString()!.EndsWith("/x", StringComparison.Ordinal)), "deep type metadata retained");
    }
}
Console.WriteLine($"{{\"success\":true,\"checks\":{checks},\"generatedSerialization\":true}}");
