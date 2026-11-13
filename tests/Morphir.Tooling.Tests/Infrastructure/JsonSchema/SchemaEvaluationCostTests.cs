using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.Tooling.Infrastructure.JsonSchema;

namespace Morphir.Tooling.Tests.Infrastructure.JsonSchema;

/// <summary>
/// The cost of schema evaluation grows with the size of the model, not with the number of branches of the tagged
/// unions to the power of its nesting depth. A run that does not finish within the budget fails, and its evaluation
/// keeps a thread busy until the test process ends.
/// </summary>
public class SchemaEvaluationCostTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);

    private readonly SchemaValidator _sut = new(new SchemaLoader());

    private Task<SchemaValidationResult> ValidateWithinBudget(string json, string version) =>
        Task.Run(() => _sut.ValidateAsync(json, version, CancellationToken.None)).WaitAsync(Budget);

    [Test]
    [Category("PlatformSpecific")]
    public async Task The_greeting_example_of_finos_morphir_is_valid_within_the_budget()
    {
        var json = await File.ReadAllTextAsync("TestData/greeting-example-v3.json");

        var result = await ValidateWithinBudget(json, "3");

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Test]
    [Arguments(3)]
    [Arguments(20)]
    [Arguments(200)]
    public async Task A_value_with_deeply_nested_applications_is_valid_within_the_budget(int depth)
    {
        var json = ModelWithBody(NestedApply(depth));

        var result = await ValidateWithinBudget(json, "3");

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task A_value_with_an_unknown_tag_names_the_tags_that_were_expected()
    {
        var json = ModelWithBody(new JsonArray("Aply", new JsonObject(), Unit(), Unit()));

        var result = await ValidateWithinBudget(json, "3");

        result.IsValid.Should().BeFalse();
        var atTheTag = result.Errors.Where(error => error.Path.EndsWith("/body/0", StringComparison.Ordinal)).ToList();
        atTheTag.Should().Contain(error => error.Message.Contains("Apply"));
        atTheTag.Should().Contain(error => error.Message.Contains("Unit"));
    }

    [Test]
    public async Task A_value_with_a_known_tag_and_a_wrong_argument_reports_the_argument()
    {
        var json = ModelWithBody(new JsonArray("Apply", new JsonObject(), Unit(), new JsonArray("Unit", 42)));

        var result = await ValidateWithinBudget(json, "3");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Path.EndsWith("/body/3/1", StringComparison.Ordinal));
    }

    private static JsonArray Unit() => new("Unit", new JsonObject());

    /// <summary><c>Apply</c> nested <paramref name="depth"/> times in the function position.</summary>
    private static JsonNode NestedApply(int depth)
    {
        JsonNode value = Unit();
        for (var i = 0; i < depth; i++) value = new JsonArray("Apply", new JsonObject(), value, Unit());
        return value;
    }

    /// <summary>A v3 library with one module and one value whose body is <paramref name="body"/>.</summary>
    private static string ModelWithBody(JsonNode body)
    {
        var definition = new JsonObject
        {
            ["inputTypes"] = new JsonArray(),
            ["outputType"] = Unit(),
            ["body"] = body,
        };
        var module = new JsonObject
        {
            ["types"] = new JsonArray(),
            ["values"] = new JsonArray(new JsonArray(
                new JsonArray("deep", "value"),
                new JsonObject { ["access"] = "Public", ["value"] = new JsonObject { ["doc"] = "", ["value"] = definition } })),
            ["doc"] = null,
        };
        var model = new JsonObject
        {
            ["formatVersion"] = 3,
            ["distribution"] = new JsonArray(
                "Library",
                new JsonArray(new JsonArray("test"), new JsonArray("package")),
                new JsonArray(),
                new JsonObject
                {
                    ["modules"] = new JsonArray(new JsonArray(
                        new JsonArray(new JsonArray("deep")),
                        new JsonObject { ["access"] = "Public", ["value"] = module })),
                }),
        };
        return model.ToJsonString();
    }
}
