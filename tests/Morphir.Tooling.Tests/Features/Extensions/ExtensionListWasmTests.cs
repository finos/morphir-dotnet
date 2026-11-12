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
[Category("PlatformSpecific")]
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
        WasmRuntimeUnavailableReason.WinX64TrapUnsupported,
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
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public void RuntimeRefusalsListTheSameCommonReasonAsTheRegistryQuery(int reason)
    {
        var entry = WasmEntry();
        var availability = WasmRuntimeAvailability.UnavailableBecause(Reasons[reason]);
        var runtime = new WasmRuntime("win-arm64", () => availability);
        ExtensionCatalog.TryCreate(Native, [entry], out var catalog, out var error).Should().BeTrue();
        error.Should().BeNull();
        var listed = ExtensionListReport.From(catalog!, () => runtime.Availability).Extensions.Single();
        listed.Available.Should().BeFalse();
        listed.Reason.Should().Be(entry.ReasonOrNullIn(Native, () => runtime.Availability)!.Value);
        listed.Reason.Should().Be($"The WASM runtime is unavailable: {Reasons[reason].Description}.");
        listed.Deployments.Should().Equal(Deployment.All.Select(deployment => deployment.WireValue));
    }

    [Test]
    public void AvailableListingsHaveNoReasonAndShareOneLazyProbe()
    {
        var probes = 0;
        var runtime = new WasmRuntime("win-arm64", () =>
        {
            probes++;
            return WasmRuntimeAvailability.Available;
        });
        var first = WasmEntry();
        var second = ExtensionEntry.BuiltInBackend(
                ExtensionIdentity.Create(ExtensionId.Create("acme.avro"), ExtensionName.Create("Avro binding"), Native.Version, null),
                first.BackendClaimOrNull!)
            .WithDelivery(ExtensionDelivery.Wasm);
        ExtensionCatalog.TryCreate(Native, [first, second], out var catalog, out _).Should().BeTrue();
        var report = ExtensionListReport.From(catalog!, () => runtime.Availability);
        report.Extensions.Should().OnlyContain(entry => entry.Available && entry.Reason == null);
        probes.Should().Be(1);
    }

    [Test]
    public void StaticRefusalsListTheirCommonReasonWithoutProbing()
    {
        var hostOnly = WasmEntry().WithHosts(HostAvailability.OnlyIn(MorphirHost.DotnetTool));
        var reason = UnavailableReason.Create("Configure a WASM delivery for this offering.");
        var deploymentOnly = WasmEntry().WithDeployments(EntryDeployments.OnlyIn(NonEmptySet<Deployment>.Create(Deployment.NativeAot), reason));
        foreach (var entry in new[] { hostOnly, deploymentOnly })
        {
            ExtensionCatalog.TryCreate(Native, [entry], out var catalog, out _).Should().BeTrue();
            var runtime = new WasmRuntime("win-arm64", () => throw new InvalidOperationException("Static refusal probed the engine"));
            var listed = ExtensionListReport.From(catalog!, () => runtime.Availability).Extensions.Single();
            listed.Available.Should().BeFalse();
            listed.Reason.Should().Be(entry.ReasonOrNullIn(Native)!.Value);
            runtime.HasProbed.Should().BeFalse();
        }
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

    /// <summary>The registry reports the availability and reason of this process, including an unsupported engine.</summary>
    [Test]
    public void ARegistryOfThisProcessListsTheWasmEntryAsTheProcessRuntimeReportsIt()
    {
        var registry = new ExtensionRegistry([new FixedSource(WasmEntry())], Native, WasmRuntime.Process);
        var report = Run(registry, json: true);
        Entries(report)["acme.python"].Available.Should().Be(WasmRuntime.Process.Availability.CanRunModules);
        using var document = JsonDocument.Parse(report);
        var entry = document.RootElement.GetProperty("extensions").EnumerateArray()
            .Single(value => value.GetProperty("id").GetString() == "acme.python");
        var expectedReason = WasmRuntime.Process.Availability.Match<string?>(
            available: () => null, unavailable: reason => $"The WASM runtime is unavailable: {reason.Description}.");
        if (expectedReason is null)
            entry.TryGetProperty("reason", out var reason).Should().BeFalse();
        else
            entry.GetProperty("reason").GetString().Should().Be(expectedReason);
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
