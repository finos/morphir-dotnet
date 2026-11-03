using Shouldly;

namespace Morphir.SDK.CSharp.Tests;

public sealed class ConformanceTests
{
    [Test]
    public void EveryFrozenRequiredIdExecutesAndPasses()
    {
        var report = RuntimeAdapter.RunAll();
        var json = new System.Text.Json.Nodes.JsonObject
        {
            ["contractId"] = "morphir-dotnet-sdk-v1",
            ["profile"] = "dotnet-sdk-v1",
            ["executed"] = new System.Text.Json.Nodes.JsonArray(report.Executed.Select(x => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(x)).ToArray()),
            ["passed"] = new System.Text.Json.Nodes.JsonArray(report.Passed.Select(x => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(x)).ToArray()),
            ["excluded"] = new System.Text.Json.Nodes.JsonArray(report.Excluded.Select(x => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(x)).ToArray()),
            ["failures"] = new System.Text.Json.Nodes.JsonArray(report.Failures.Select(x => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(x)).ToArray())
        };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "conformance-results.json"), json.ToJsonString());
        report.Failures.ShouldBeEmpty();
        report.Executed.Length.ShouldBe(207);
        report.Passed.ShouldBe(report.Executed);
        report.Excluded.Length.ShouldBe(8);
    }
}
