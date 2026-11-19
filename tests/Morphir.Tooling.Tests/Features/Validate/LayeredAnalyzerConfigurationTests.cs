using FluentAssertions;
using Morphir.Extensions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Validate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Features.Validate;

/// <summary><c>extensions."&lt;id&gt;".enabled</c> from the layered configuration of the invocation directory.</summary>
[Category("PlatformSpecific")]
public sealed class LayeredAnalyzerConfigurationTests
{
    private static readonly ExtensionId TypeValidator = ExtensionId.Create("morphir.type-validator");
    private static readonly ExtensionId Other = ExtensionId.Create("acme.lint");

    private static Outcome<IReadOnlyList<ExtensionId>> Read(TestSandbox sandbox, string directory) =>
        new LayeredAnalyzerConfiguration(sandbox.EnvironmentSnapshot(Containment.CurrentEnvironment()))
            .TurnedOff(directory, [TypeValidator, Other], default);

    private static IReadOnlyList<string> TurnedOff(Outcome<IReadOnlyList<ExtensionId>> outcome) =>
        outcome.Should().BeOfType<Outcome<IReadOnlyList<ExtensionId>>.Success>().Subject.Value.Select(id => id.Value).ToArray();

    [Test]
    public async Task EnabledFalseInAProjectConfigurationTurnsOffTheAnalyzer()
    {
        await using var sandbox = TestSandbox.Create("morphir-validate-config-");
        await File.WriteAllTextAsync(Path.Combine(sandbox.Work, "morphir.toml"),
            "[extensions.\"morphir.type-validator\"]\nenabled = false\n");
        TurnedOff(Read(sandbox, sandbox.Work)).Should().Equal("morphir.type-validator");
    }

    [Test]
    public async Task ANestedDirectoryReadsTheProjectConfigurationAbove()
    {
        await using var sandbox = TestSandbox.Create("morphir-validate-config-");
        await File.WriteAllTextAsync(Path.Combine(sandbox.Work, "morphir.json"),
            """{ "extensions": { "acme.lint": { "enabled": false }, "morphir.type-validator": { "enabled": true } } }""");
        var nested = Directory.CreateDirectory(Path.Combine(sandbox.Work, "src", "models")).FullName;
        TurnedOff(Read(sandbox, nested)).Should().Equal("acme.lint");
    }

    [Test]
    public async Task NoConfigurationTurnsOffNothing()
    {
        await using var sandbox = TestSandbox.Create("morphir-validate-config-");
        TurnedOff(Read(sandbox, sandbox.Work)).Should().BeEmpty();
    }

    [Test]
    public async Task AnInvalidConfigurationIsAFailure()
    {
        await using var sandbox = TestSandbox.Create("morphir-validate-config-");
        await File.WriteAllTextAsync(Path.Combine(sandbox.Work, "morphir.toml"), "[extensions.\"morphir.type-validator\"\nenabled = ");
        Read(sandbox, sandbox.Work).Should().BeOfType<Outcome<IReadOnlyList<ExtensionId>>.Failure>()
            .Which.Error.Diagnostics.Should().NotBeEmpty();
    }
}
