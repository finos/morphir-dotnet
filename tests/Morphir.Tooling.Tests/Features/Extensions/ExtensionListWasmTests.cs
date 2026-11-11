using System.Text.Json;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// A <c>wasm</c> entry in <c>extension list</c>. Nothing registers one from configuration yet (issue #565), so these
/// tests build the entry, and the WASM runtime availability that the runtime report of the host would state.
/// </summary>
public sealed class ExtensionListWasmTests
{
    private static readonly HostIdentity Native = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));
    private static readonly HostIdentity Tool = TestHosts.Unprofiled(MorphirHost.DotnetTool, Native.Version);

    private sealed class FixedSource(params ExtensionEntry[] entries) : IExtensionSource
    {
        public IReadOnlyList<ExtensionEntry> Entries { get; } = entries;
    }

    /// <summary>A module-delivered entry for both hosts. Its id sorts first.</summary>
    private static ExtensionEntry WasmEntry() => ExtensionEntry.BuiltInBackend(
            ExtensionIdentity.Create(ExtensionId.Create("acme.python"), ExtensionName.Create("Python binding"), Native.Version, null),
            new BackendClaim(NonEmptySet<TargetId>.Create(TargetId.Create("python")), SourceFormatVersions.Create("4.0.0")))
        .WithDelivery(ExtensionDelivery.Wasm);

    private static ExtensionRegistry Registry(HostIdentity host, WasmRuntimeAvailability wasmRuntime) =>
        new([new BackendExtensionSource([new FSharpGenerationBackend(), new CSharpGenerationBackend()], host), new FixedSource(WasmEntry())], host,
            new WasmRuntime("win-arm64", () => wasmRuntime));

    private static string Run(ExtensionRegistry registry, bool json)
    {
        var stdout = new List<string>();
        ExtensionListCommand.Execute(registry, json, stdout.Add, new StringWriter(), TerminalProfile.Plain).Should().Be(0);
        return string.Join('\n', stdout).Replace("\r\n", "\n");
    }

    private static Dictionary<string, (string Delivery, bool Available)> Entries(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("extensions").EnumerateArray().ToDictionary(
            entry => entry.GetProperty("id").GetString()!,
            entry => (entry.GetProperty("delivery").GetString()!, entry.GetProperty("available").GetBoolean()));
    }

    /// <summary>The unavailable reasons of the runtime report.</summary>
    private static readonly WasmRuntimeUnavailableReason[] Reasons =
    [
        WasmRuntimeUnavailableReason.PlatformUnsupported,
        WasmRuntimeUnavailableReason.EngineMissing,
        WasmRuntimeUnavailableReason.EngineLoadFailed,
    ];

    [Test]
    public void AWasmEntryHasTheDeliveryWasmAndEveryBuiltInKeepsBuiltIn()
    {
        var entries = Entries(Run(Registry(Native, WasmRuntimeAvailability.Available), json: true));
        entries.Keys.Should().Equal("acme.python", "morphir.csharp", "morphir.fsharp");
        entries["acme.python"].Delivery.Should().Be("wasm");
        entries["morphir.csharp"].Delivery.Should().Be("built-in");
        entries["morphir.fsharp"].Delivery.Should().Be("built-in");
    }

    [Test]
    public void TheTextTableShowsTheWasmDelivery()
    {
        var rows = Run(Registry(Native, WasmRuntimeAvailability.Available), json: false).Split('\n').Where(row => row.StartsWith('│')).ToArray();
        rows[1].Should().MatchRegex(@"^│ acme\.python\s+│ Python binding\s+│ 0\.4\.0\s+│ backend\s+│ wasm\s+│ morphir, dotnet-morphir\s+│$");
        rows[2].Should().MatchRegex(@"│ built-in\s+│");
    }

    [Test]
    [Arguments(false, 0)]
    [Arguments(false, 1)]
    [Arguments(false, 2)]
    [Arguments(true, 0)]
    [Arguments(true, 1)]
    [Arguments(true, 2)]
    public void HostWithoutTheEngineListsTheWasmEntryAsNotAvailable(bool tool, int reason)
    {
        var host = tool ? Tool : Native;
        var entries = Entries(Run(Registry(host, WasmRuntimeAvailability.UnavailableBecause(Reasons[reason])), json: true));
        entries.Should().ContainKey("acme.python");
        entries["acme.python"].Available.Should().BeFalse();
        entries["morphir.csharp"].Available.Should().BeTrue();
        entries["morphir.fsharp"].Available.Should().BeTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void HostWithTheEngineListsTheWasmEntryAsAvailable(bool tool)
    {
        var entries = Entries(Run(Registry(tool ? Tool : Native, WasmRuntimeAvailability.Available), json: true));
        entries["acme.python"].Available.Should().BeTrue();
        entries["morphir.csharp"].Available.Should().BeTrue();
    }

    /// <summary>The registry reads the runtime of its host (task 4.2): this test process carries the engine.</summary>
    [Test]
    public void ARegistryOfThisProcessListsTheWasmEntryAsTheProcessRuntimeReportsIt()
    {
        var registry = new ExtensionRegistry([new FixedSource(WasmEntry())], Native, WasmRuntime.Process);
        Entries(Run(registry, json: true))["acme.python"].Available.Should().Be(WasmRuntime.Process.Availability.CanRunModules);
        WasmRuntime.Process.Availability.Should().Be(WasmRuntimeAvailability.Available);
    }

    [Test]
    public void CSharpBranchesOnTheDeliveryAndTheRuntimeWithMatch()
    {
        var delivery = WasmEntry().Delivery.Match(builtIn: () => "built-in case", wasm: () => "wasm case");
        delivery.Should().Be("wasm case");
        var reason = WasmRuntimeAvailability.UnavailableBecause(WasmRuntimeUnavailableReason.EngineLoadFailed)
            .Match(available: () => "available", unavailable: why => why.WireValue);
        reason.Should().Be("engine_load_failed");
        WasmRuntimeAvailability.Available.CanRunModules.Should().BeTrue();
        WasmRuntimeAvailability.UnavailableBecause(WasmRuntimeUnavailableReason.EngineMissing).CanRunModules.Should().BeFalse();
    }
}
