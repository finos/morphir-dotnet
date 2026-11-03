using System.Text.Json;
using FluentAssertions;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

public class GenerationSettingsTests
{
    private static readonly InvocationContext Context = new(Path.GetTempPath());
    private static readonly GenerateOverrides Valid = new("models/pricing.json", "fsharp", "generated");

    [Test]
    [Arguments("input")]
    [Arguments("target")]
    [Arguments("output")]
    public async Task MissingSettingsReturnInvocationFailure(string missing)
    {
        var input = missing switch { "input" => Valid with { Input = null }, "target" => Valid with { Target = " " }, _ => Valid with { Output = "" } };
        var result = await new CliGenerationSettingsResolver().ResolveAsync(input, Context, CancellationToken.None);
        var failure = result.Should().BeOfType<Outcome<GenerationSettings>.Failure>().Subject.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Invocation);
        failure.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "missing-" + missing);
    }

    [Test]
    public async Task RelativePathsUseTheInvocationAndRequireNoProject()
    {
        var result = await Resolve(Valid);
        result.Input.Path.Should().Be(Path.GetFullPath(Valid.Input!, Context.WorkingDirectory));
        result.Publication.OutputDirectory.Should().Be(Path.GetFullPath("generated", Context.WorkingDirectory));
        result.Input.Format.Should().Be(InputFormat.Auto);
        result.Target.Value.Should().Be("fsharp");
    }

    [Test]
    public async Task OptionsParseJsonFirstAndTheLastOccurrenceWins()
    {
        var settings = await Resolve(Valid with { Options = ["optimize=true", "optimize=false", "label=a=b", "count=12", "object={\"x\":[1]}", "quoted=\"hello\"", "nothing=null"] });
        settings.Options.Values["optimize"].GetBoolean().Should().BeFalse();
        settings.Options.Values["label"].GetString().Should().Be("a=b");
        settings.Options.Values["count"].GetInt32().Should().Be(12);
        settings.Options.Values["object"].GetProperty("x")[0].GetInt32().Should().Be(1);
        settings.Options.Values["quoted"].GetString().Should().Be("hello");
        settings.Options.Values["nothing"].ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    [Arguments("bad")]
    [Arguments("=value")]
    [Arguments(" =value")]
    public async Task MalformedOptionIsNotSilentlyDropped(string option)
    {
        var result = await new CliGenerationSettingsResolver().ResolveAsync(Valid with { Options = [option] }, Context, CancellationToken.None);
        ((Outcome<GenerationSettings>.Failure)result).Error.Diagnostics[0].Code.Should().Be("invalid-option");
    }

    [Test]
    public async Task ExplicitFormatIsValidatedWithoutOpeningTheInput()
    {
        (await Resolve(Valid with { InputFormat = "ion" })).Input.Format.Should().Be(InputFormat.Ion);
        var result = await new CliGenerationSettingsResolver().ResolveAsync(Valid with { InputFormat = "yaml" }, Context, CancellationToken.None);
        ((Outcome<GenerationSettings>.Failure)result).Error.Diagnostics[0].Code.Should().Be("invalid-input-format");
    }

    [Test]
    public async Task CancellationIsTyped()
    {
        var result = await new CliGenerationSettingsResolver().ResolveAsync(Valid, Context, new CancellationToken(true));
        ((Outcome<GenerationSettings>.Failure)result).Error.Kind.Should().Be(GenerationFailureKind.Cancelled);
    }

    private static async Task<GenerationSettings> Resolve(GenerateOverrides input) =>
        ((Outcome<GenerationSettings>.Success)await new CliGenerationSettingsResolver().ResolveAsync(input, Context, CancellationToken.None)).Value;
}
