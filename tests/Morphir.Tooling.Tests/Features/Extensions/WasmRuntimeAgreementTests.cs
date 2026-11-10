// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morphir.Extensions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// The registry, <c>extension list</c>, the module loader and <c>extension runtimes</c> read one answer of one probe
/// (task 4.2 of <c>add-wasm-extension-host</c>; the <c>extension-registry</c> requirement "A WASM entry is available
/// only where the WASM runtime is"), and only a reader that needs it asks.
/// </summary>
public sealed class WasmRuntimeAgreementTests
{
    private static readonly HostIdentity Native = new(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));

    private sealed class FixedSource(params ExtensionEntry[] entries) : IExtensionSource
    {
        public IReadOnlyList<ExtensionEntry> Entries { get; } = entries;
    }

    private static ExtensionEntry WasmEntry() => ExtensionEntry.BuiltInBackend(
            ExtensionIdentity.Create(ExtensionId.Create("acme.python"), ExtensionName.Create("Python binding"), Native.Version, null),
            new BackendClaim(NonEmptySet<TargetId>.Create(TargetId.Create("python")), SourceFormatVersions.Create("4.0.0")))
        .WithDelivery(ExtensionDelivery.Wasm);

    private static IExtensionSource BuiltIns() => new BackendExtensionSource([new FSharpGenerationBackend(), new CSharpGenerationBackend()], Native);

    public static IEnumerable<string?> States() => [null, "platform_unsupported", "engine_missing", "engine_load_failed"];

    private static WasmRuntimeAvailability Availability(string? reason) => reason switch
    {
        null => WasmRuntimeAvailability.Available,
        "platform_unsupported" => WasmRuntimeAvailability.UnavailableBecause(WasmRuntimeUnavailableReason.PlatformUnsupported),
        "engine_missing" => WasmRuntimeAvailability.UnavailableBecause(WasmRuntimeUnavailableReason.EngineMissing),
        "engine_load_failed" => WasmRuntimeAvailability.UnavailableBecause(WasmRuntimeUnavailableReason.EngineLoadFailed),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private sealed class CountingRuntime(WasmRuntimeAvailability answer)
    {
        public int Probes;
        public WasmRuntime Runtime => field ??= new WasmRuntime("win-arm64", () =>
        {
            Interlocked.Increment(ref Probes);
            return answer;
        });
    }

    private static JsonElement List(ExtensionRegistry registry)
    {
        var stdout = new List<string>();
        ExtensionListCommand.Execute(registry, json: true, stdout.Add, new StringWriter(), TerminalProfile.Plain).Should().Be(0);
        using var document = JsonDocument.Parse(string.Join('\n', stdout));
        return document.RootElement.Clone();
    }

    private static bool Listed(JsonElement list, string id) =>
        list.GetProperty("extensions").EnumerateArray().Single(entry => entry.GetProperty("id").GetString() == id).GetProperty("available").GetBoolean();

    private static JsonElement Runtimes(WasmRuntime runtime)
    {
        var stdout = new List<string>();
        ExtensionRuntimesCommand.Execute(Native, runtime, json: true, stdout.Add).Should().Be(0);
        using var document = JsonDocument.Parse(string.Join('\n', stdout));
        return document.RootElement.GetProperty("runtimes")[0].Clone();
    }

    [Test]
    [MethodDataSource(nameof(States))]
    public void TheListTheReportAndTheLoaderAgreeWithOneProbe(string? reason)
    {
        using var sandbox = TestSandbox.Create();
        var module = Path.Combine(sandbox.Work, "python.wasm");
        File.Copy(WorkerHarness.Corpus("extism-echo"), module);
        var counting = new CountingRuntime(Availability(reason));
        var registry = new ExtensionRegistry([BuiltIns(), new FixedSource(WasmEntry())], Native, counting.Runtime);
        var loader = new WasmModuleLoader(counting.Runtime, Native);

        var listed = Listed(List(registry), "acme.python");
        var reported = Runtimes(counting.Runtime);
        var load = loader.Load(new WasmModuleSource("python", module));

        var available = reason is null;
        listed.Should().Be(available);
        Listed(List(registry), "morphir.csharp").Should().BeTrue("a built-in entry does not depend on the WASM runtime");
        reported.GetProperty("available").GetBoolean().Should().Be(available);
        if (available)
        {
            reported.TryGetProperty("reason", out _).Should().BeFalse();
            load.Should().BeOfType<WasmModuleLoad.Loaded>();
        }
        else
        {
            reported.GetProperty("reason").GetString().Should().Be(reason);
            var failure = load.Should().BeOfType<WasmModuleLoad.Failed>().Subject.Failure;
            var unavailable = failure.Should().BeOfType<WasmModuleFailure.RuntimeUnavailable>().Subject;
            unavailable.Reason.WireValue.Should().Be(reason);
            unavailable.Code.Should().Be("extension.runtime_unavailable");
        }

        counting.Probes.Should().Be(1, "the list, the report and the loader read one remembered answer");
    }

    [Test]
    public void AListOfOnlyBuiltInEntriesDoesNotProbeTheRuntime()
    {
        var counting = new CountingRuntime(WasmRuntimeAvailability.Available);
        var registry = new ExtensionRegistry([BuiltIns()], Native, counting.Runtime);

        List(registry).GetProperty("extensions").EnumerateArray().Should().OnlyContain(entry => entry.GetProperty("available").GetBoolean());
        var text = new List<string>();
        ExtensionListCommand.Execute(registry, json: false, text.Add, new StringWriter(), TerminalProfile.Plain).Should().Be(0);

        counting.Runtime.HasProbed.Should().BeFalse();
        counting.Probes.Should().Be(0);
    }

    [Test]
    public void ALoaderWithoutTheRuntimeFailsByNameBeforeReadingTheModule()
    {
        using var sandbox = TestSandbox.Create();
        var missing = Path.Combine(sandbox.Work, "no-such-module.wasm");
        var loader = new WasmModuleLoader(new WasmRuntime("win-arm64",
            () => WasmRuntimeProbe.Probe("win-arm64", () => throw new DllNotFoundException("Unable to load DLL 'wasmtime'"), () => false)), Native);

        var load = () => loader.Load(new WasmModuleSource("python", missing));

        var failure = load.Should().NotThrow().Which.Should().BeOfType<WasmModuleLoad.Failed>().Subject.Failure;
        var unavailable = failure.Should().BeOfType<WasmModuleFailure.RuntimeUnavailable>("the module is not read, so its absence is not reported").Subject;
        unavailable.Reason.Should().Be(WasmRuntimeUnavailableReason.EngineMissing);
        unavailable.Code.Should().Be("extension.runtime_unavailable");
        unavailable.Message.Should().Contain("extension.runtime_unavailable").And.Contain("engine_missing").And.Contain("win-arm64")
            .And.Contain("morphir 0.4.0").And.Contain("'python'");
        unavailable.Message.Should().NotContain("Unable to load DLL", "a native library loading error is not the diagnostic");
    }

    // --- The composition ----------------------------------------------------------------------------------------

    private static MorphirCliApp App(WasmRuntime runtime) =>
        MorphirApplication.CreateApp([], Native, builder => builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton(runtime))));

    [Test]
    public void TheCompositionRegistersTheProcessRuntimeOnce()
    {
        using var app = MorphirApplication.CreateApp([], Native);

        app.Services.GetRequiredService<WasmRuntime>().Should().BeSameAs(WasmRuntime.Process);
        app.Services.GetServices<WasmRuntime>().Should().ContainSingle();
        app.Services.GetRequiredService<ExtensionRegistry>().WasmRuntime.Should().BeSameAs(WasmRuntime.Process);
    }

    [Test]
    public void TheRegistryAndTheLoaderOfTheCompositionReadTheRegisteredRuntimeLazily()
    {
        var counting = new CountingRuntime(WasmRuntimeAvailability.UnavailableBecause(WasmRuntimeUnavailableReason.EngineMissing));
        using var app = App(counting.Runtime);

        var registry = app.Services.GetRequiredService<ExtensionRegistry>();
        var loader = app.Services.GetRequiredService<IWasmModuleLoader>();
        List(registry);
        counting.Probes.Should().Be(0, "building the CLI and listing built-in entries do not probe");

        using var sandbox = TestSandbox.Create();
        var load = loader.Load(new WasmModuleSource("python", Path.Combine(sandbox.Work, "python.wasm")));

        load.Should().BeOfType<WasmModuleLoad.Failed>().Which.Failure.Should().BeOfType<WasmModuleFailure.RuntimeUnavailable>();
        registry.WasmRuntime.Should().BeSameAs(counting.Runtime);
        counting.Probes.Should().Be(1);
    }
}
