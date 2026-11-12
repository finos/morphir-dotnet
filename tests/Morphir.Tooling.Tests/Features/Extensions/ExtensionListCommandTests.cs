using System.Text.Json;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Validate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>The command core: one registry in, text or one JSON document on stdout, diagnostics on stderr.</summary>
public sealed class ExtensionListCommandTests
{
    private static readonly HostIdentity Native = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));

    private sealed class FixedSource(params ExtensionEntry[] entries) : IExtensionSource
    {
        public IReadOnlyList<ExtensionEntry> Entries { get; } = entries;
    }

    private static ExtensionRegistry Registry(HostIdentity host, params IExtensionSource[] extra) =>
        new([new BackendExtensionSource([new FSharpGenerationBackend(), new CSharpGenerationBackend()], host), .. extra], host, WasmRuntime.Process);

    /// <summary>The composition of both hosts: the generation backends, the built-in frontends and the built-in analyzers.</summary>
    private static ExtensionRegistry HostRegistry(HostIdentity host) =>
        Registry(host, new FrontendExtensionSource(host), new AnalyzerExtensionSource(new AnalyzerRegistry(host)));

    private static ExtensionEntry ToolOnly() => ExtensionEntry.BuiltInBackend(
            ExtensionIdentity.Create(ExtensionId.Create("acme.tool-only"), ExtensionName.Create("Tool only"), Native.Version, null),
            new BackendClaim(NonEmptySet<TargetId>.Create(TargetId.Create("acme")), SourceFormatVersions.Create("4.0.0")))
        .WithHosts(HostAvailability.OnlyIn(MorphirHost.DotnetTool));

    private static (int Exit, List<string> Out, string Err) Run(ExtensionRegistry registry, bool json)
    {
        var stdout = new List<string>();
        var stderr = new StringWriter();
        var exit = ExtensionListCommand.Execute(registry, json, stdout.Add, stderr, TerminalProfile.Plain);
        // The table keeps the platform's line breaks (CRLF on Windows); the assertions compare LF text.
        return (exit, stdout.Select(text => text.Replace("\r\n", "\n")).ToList(), stderr.ToString());
    }

    [Test]
    [Category("PlatformSpecific")]
    public void TextOutputIsATableWithAHeaderAndOneSortedRowForEachEntry()
    {
        var (exit, lines, err) = Run(Registry(Native), json: false);
        exit.Should().Be(0);
        err.Should().BeEmpty();
        var rows = string.Join('\n', lines).Split('\n').Where(row => row.StartsWith('│')).ToArray();
        rows.Should().HaveCount(3);
        rows[0].Should().MatchRegex(@"^│ ID\s+│ NAME\s+│ VERSION\s+│ TYPES\s+│ DELIVERY\s+│ HOSTS\s+│$");
        rows[1].Should().MatchRegex(@"^│ morphir\.csharp\s+│ C# generator\s+│ 0\.4\.0\s+│ backend\s+│ built-in\s+│ morphir, dotnet-morphir\s+│$");
        rows[2].Should().StartWith("│ morphir.fsharp ");
    }

    [Test]
    [Category("PlatformSpecific")]
    public void ATerminalProfileStylesTheTableAndPlainOutputDoesNot()
    {
        var styled = new List<string>();
        ExtensionListCommand.Execute(Registry(Native), false, styled.Add, new StringWriter(), new TerminalProfile(true, 120));
        string.Join('\n', styled).Should().Contain("\u001b[");
        var (_, plain, _) = Run(Registry(Native), json: false);
        string.Join('\n', plain).Should().NotContain("\u001b[");
    }

    /// <summary>The text table in docs/content/docs/cli/management.md, for version 0.4.0.</summary>
    [Test]
    [Category("PlatformSpecific")]
    public void TextOutputMatchesTheDocumentation()
    {
        var (_, lines, _) = Run(HostRegistry(Native), json: false);
        string.Join('\n', lines).Should().Be(
            "╭────────────────────────┬────────────────┬─────────┬─────────────────────┬──────────┬─────────────────────────╮\n" +
            "│ ID                     │ NAME           │ VERSION │ TYPES               │ DELIVERY │ HOSTS                   │\n" +
            "├────────────────────────┼────────────────┼─────────┼─────────────────────┼──────────┼─────────────────────────┤\n" +
            "│ morphir-dotnet.fsharp  │ F# frontend    │ 0.4.0   │ frontend (fsharp)   │ built-in │ morphir, dotnet-morphir │\n" +
            "│ morphir.csharp         │ C# generator   │ 0.4.0   │ backend             │ built-in │ morphir, dotnet-morphir │\n" +
            "│ morphir.fsharp         │ F# generator   │ 0.4.0   │ backend             │ built-in │ morphir, dotnet-morphir │\n" +
            "│ morphir.type-validator │ Type validator │ 0.4.0   │ validator (1, 2, 3) │ built-in │ morphir, dotnet-morphir │\n" +
            "╰────────────────────────┴────────────────┴─────────┴─────────────────────┴──────────┴─────────────────────────╯");
    }

    [Test]
    [Category("PlatformSpecific")]
    public void TheTypeValidatorRowShowsItsValidatorClaim()
    {
        var (_, lines, _) = Run(HostRegistry(Native), json: false);
        var rows = string.Join('\n', lines).Split('\n').Where(row => row.StartsWith('│')).ToArray();
        rows.Should().HaveCount(5);
        rows[4].Should().MatchRegex(@"^│ morphir\.type-validator\s+│ Type validator\s+│ 0\.4\.0\s+│ validator \(1, 2, 3\)\s+│ built-in\s+│ morphir, dotnet-morphir\s+│$");
    }

    [Test]
    public void JsonOutputHasTheTypeValidatorWithItsValidatorClaim()
    {
        var (exit, lines, _) = Run(HostRegistry(Native), json: true);
        exit.Should().Be(0);
        using var document = JsonDocument.Parse(string.Join('\n', lines));
        var extensions = document.RootElement.GetProperty("extensions");
        extensions.EnumerateArray().Select(entry => entry.GetProperty("id").GetString())
            .Should().Equal("morphir-dotnet.fsharp", "morphir.csharp", "morphir.fsharp", "morphir.type-validator");
        var validator = extensions[3];
        validator.GetProperty("types").EnumerateArray().Select(type => type.GetString()).Should().Equal("validator");
        validator.GetProperty("delivery").GetString().Should().Be("built-in");
        validator.GetProperty("hosts").EnumerateArray().Select(host => host.GetString()).Should().Equal("morphir", "dotnet-morphir");
        var capabilities = validator.GetProperty("capabilities");
        capabilities.TryGetProperty("backend", out _).Should().BeFalse();
        capabilities.GetProperty("validator").GetProperty("irVersions").EnumerateArray().Select(version => version.GetString())
            .Should().Equal("1", "2", "3");
    }

    [Test]
    public void BothHostsListTheSameEntriesApartFromTheHost()
    {
        var tool = TestHosts.Unprofiled(MorphirHost.DotnetTool, Native.Version);
        var (_, native, _) = Run(HostRegistry(Native), json: true);
        var (_, dotnet, _) = Run(HostRegistry(tool), json: true);
        using var first = JsonDocument.Parse(string.Join('\n', native));
        using var second = JsonDocument.Parse(string.Join('\n', dotnet));
        first.RootElement.GetProperty("host").GetString().Should().Be("morphir");
        second.RootElement.GetProperty("host").GetString().Should().Be("dotnet-morphir");
        first.RootElement.GetProperty("extensions").GetRawText().Should().Be(second.RootElement.GetProperty("extensions").GetRawText());
    }

    [Test]
    public void JsonOutputIsOneDocumentWithMepWireNames()
    {
        var (exit, lines, err) = Run(Registry(Native), json: true);
        exit.Should().Be(0);
        err.Should().BeEmpty();
        using var document = JsonDocument.Parse(string.Join('\n', lines));
        var root = document.RootElement;
        root.GetProperty("host").GetString().Should().Be("morphir");
        root.TryGetProperty("diagnostics", out _).Should().BeFalse();
        var extensions = root.GetProperty("extensions");
        extensions.EnumerateArray().Select(entry => entry.GetProperty("id").GetString()).Should().Equal("morphir.csharp", "morphir.fsharp");
        var csharp = extensions[0];
        csharp.GetProperty("name").GetString().Should().Be("C# generator");
        csharp.GetProperty("version").GetString().Should().Be("0.4.0");
        csharp.GetProperty("description").GetString().Should().Be("Generates C# source from a Morphir library model.");
        csharp.GetProperty("types").EnumerateArray().Select(type => type.GetString()).Should().Equal("backend");
        csharp.GetProperty("delivery").GetString().Should().Be("built-in");
        csharp.GetProperty("hosts").EnumerateArray().Select(host => host.GetString()).Should().Equal("morphir", "dotnet-morphir");
        csharp.GetProperty("available").GetBoolean().Should().BeTrue();
        var backend = csharp.GetProperty("capabilities").GetProperty("backend");
        backend.GetProperty("targets").EnumerateArray().Select(target => target.GetString()).Should().Equal("csharp");
        backend.GetProperty("irVersions").EnumerateArray().Select(version => version.GetString()).Should().Equal("1", "2", "3", "3.0.0", "3.1.0", "4", "4.0.0");
        backend.GetProperty("generate").GetBoolean().Should().BeTrue();
    }

    [Test]
    public void AnEntryWithoutADescriptionHasNoDescriptionMember()
    {
        var (_, lines, _) = Run(Registry(Native, new FixedSource(ToolOnly())), json: true);
        using var document = JsonDocument.Parse(string.Join('\n', lines));
        var entry = document.RootElement.GetProperty("extensions")[0];
        entry.GetProperty("id").GetString().Should().Be("acme.tool-only");
        entry.TryGetProperty("description", out _).Should().BeFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, true)]
    public void AnEntryForTheOtherHostIsListedAsNotAvailable(bool tool, bool available)
    {
        var host = tool ? TestHosts.Unprofiled(MorphirHost.DotnetTool, Native.Version) : Native;
        var (_, lines, _) = Run(Registry(host, new FixedSource(ToolOnly())), json: true);
        using var document = JsonDocument.Parse(string.Join('\n', lines));
        document.RootElement.GetProperty("host").GetString().Should().Be(host.Host.WireValue);
        var entry = document.RootElement.GetProperty("extensions")[0];
        entry.GetProperty("hosts").EnumerateArray().Select(value => value.GetString()).Should().Equal("dotnet-morphir");
        entry.GetProperty("available").GetBoolean().Should().Be(available);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void ADuplicateIdExitsWithTwoAndNamesTheId(bool json)
    {
        var registry = new ExtensionRegistry(
            [new BackendExtensionSource([new CSharpGenerationBackend()], Native), new BackendExtensionSource([new CSharpGenerationBackend()], Native)], Native, WasmRuntime.Process);
        var (exit, lines, err) = Run(registry, json);
        exit.Should().Be(2);
        if (json)
        {
            using var document = JsonDocument.Parse(string.Join('\n', lines));
            document.RootElement.GetProperty("extensions").GetArrayLength().Should().Be(0);
            var diagnostic = document.RootElement.GetProperty("diagnostics")[0];
            diagnostic.GetProperty("code").GetString().Should().Be("extension.duplicate_id");
            diagnostic.GetProperty("message").GetString().Should().Contain("morphir.csharp");
        }
        else
        {
            lines.Should().BeEmpty();
            err.Should().Contain("extension.duplicate_id").And.Contain("morphir.csharp");
        }
    }
}
