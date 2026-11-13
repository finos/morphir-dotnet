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
        // A branch whose tag differs reports only its tag: no other branch evaluates the function at /2 as a pattern,
        // a name or a type. List output also keeps the failed alternatives of a oneOf that holds (the other tags of the
        // valid function, the Type alternative of an object attribute), so those paths stay: the tag and the
        // attributes of the value and of its function. Everything else is the wrong argument at /3.
        var body = result.Errors.First(error => error.Path.EndsWith("/body/3/1", StringComparison.Ordinal)).Path[..^"/3/1".Length];
        string[] tagsAndAttributes = ["/0", "/1", "/2/0", "/2/1"];
        result.Errors.Where(error => error.Path.StartsWith(body + "/", StringComparison.Ordinal))
            .Select(error => error.Path[body.Length..])
            .Should().OnlyContain(path => tagsAndAttributes.Contains(path) || path.StartsWith("/3", StringComparison.Ordinal));
    }

    /// <summary>
    /// A model just within <see cref="SchemaValidator.MaxModelDepth"/> is evaluated on the large stack of the
    /// evaluation thread. A stack overflow ends the test process, so this test shows that the stack holds the limit.
    /// The frames depend on the operating system and the architecture, so it runs on each of them.
    /// </summary>
    [Test]
    [Category("PlatformSpecific")]
    public async Task A_model_just_within_the_depth_limit_is_valid()
    {
        var depth = SchemaValidator.MaxModelDepth - NestingOf(ModelWithBody(NestedApply(0))) - 2;
        var json = ModelWithBody(NestedApply(depth));
        NestingOf(json).Should().BeInRange(SchemaValidator.MaxModelDepth - 12, SchemaValidator.MaxModelDepth);

        var result = await ValidateWithinBudget(json, "3");

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task A_model_deeper_than_the_depth_limit_is_refused_when_it_is_read()
    {
        var depth = SchemaValidator.MaxModelDepth - NestingOf(ModelWithBody(NestedApply(0))) + 8;
        var json = ModelWithBody(NestedApply(depth));
        NestingOf(json).Should().BeGreaterThan(SchemaValidator.MaxModelDepth);

        var act = () => ValidateWithinBudget(json, "3");

        await act.Should().ThrowAsync<System.Text.Json.JsonException>().WithMessage("*depth*512*");
    }

    /// <summary>The deepest nesting of arrays and objects in <paramref name="json"/>.</summary>
    private static int NestingOf(string json)
    {
        var reader = new System.Text.Json.Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json), new System.Text.Json.JsonReaderOptions { MaxDepth = 100_000 });
        var deepest = 0;
        while (reader.Read())
        {
            if (reader.TokenType is System.Text.Json.JsonTokenType.StartArray or System.Text.Json.JsonTokenType.StartObject)
                deepest = Math.Max(deepest, reader.CurrentDepth + 1);
        }

        return deepest;
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
