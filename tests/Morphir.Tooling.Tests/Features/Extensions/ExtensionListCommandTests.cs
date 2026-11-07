using System.Text.Json;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>The command core: one registry in, text or one JSON document on stdout, diagnostics on stderr.</summary>
public sealed class ExtensionListCommandTests
{
    private static readonly HostIdentity Native = new(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));

    private sealed class FixedSource(params ExtensionEntry[] entries) : IExtensionSource
    {
        public IReadOnlyList<ExtensionEntry> Entries { get; } = entries;
    }

    private static ExtensionRegistry Registry(HostIdentity host, params IExtensionSource[] extra) =>
        new([new BackendExtensionSource([new FSharpGenerationBackend(), new CSharpGenerationBackend()], host), .. extra], host);

    private static ExtensionEntry ToolOnly() => ExtensionEntry.BuiltInBackend(
            ExtensionIdentity.Create(ExtensionId.Create("acme.tool-only"), ExtensionName.Create("Tool only"), Native.Version, null),
            new BackendClaim(NonEmptySet<TargetId>.Create(TargetId.Create("acme")), SourceFormatVersions.Create("4.0.0")))
        .WithHosts(HostAvailability.NewOnly(MorphirHost.DotnetTool));

    private static (int Exit, List<string> Out, string Err) Run(ExtensionRegistry registry, bool json)
    {
        var stdout = new List<string>();
        var stderr = new StringWriter();
        var exit = ExtensionListCommand.Execute(registry, json, stdout.Add, stderr);
        return (exit, stdout, stderr.ToString());
    }

    [Test]
    public void TextOutputHasAHeaderAndOneSortedRowForEachEntry()
    {
        var (exit, lines, err) = Run(Registry(Native), json: false);
        exit.Should().Be(0);
        err.Should().BeEmpty();
        var rows = string.Join('\n', lines).Split('\n');
        rows.Should().HaveCount(3);
        rows[0].Should().MatchRegex(@"^ID\s+NAME\s+VERSION\s+TYPES\s+DELIVERY\s+HOSTS$");
        rows[1].Should().MatchRegex(@"^morphir\.csharp\s+C# generator\s+0\.4\.0\s+backend\s+built-in\s+morphir, dotnet-morphir$");
        rows[2].Should().StartWith("morphir.fsharp ");
    }

    /// <summary>The text table in docs/content/docs/cli/management.md, for version 0.4.0.</summary>
    [Test]
    public void TextOutputMatchesTheDocumentation()
    {
        var (_, lines, _) = Run(Registry(Native), json: false);
        string.Join('\n', lines).Should().Be(
            "ID              NAME          VERSION  TYPES    DELIVERY  HOSTS\n" +
            "morphir.csharp  C# generator  0.4.0    backend  built-in  morphir, dotnet-morphir\n" +
            "morphir.fsharp  F# generator  0.4.0    backend  built-in  morphir, dotnet-morphir");
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
        var host = tool ? new HostIdentity(MorphirHost.DotnetTool, Native.Version) : Native;
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
            [new BackendExtensionSource([new CSharpGenerationBackend()], Native), new BackendExtensionSource([new CSharpGenerationBackend()], Native)], Native);
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
