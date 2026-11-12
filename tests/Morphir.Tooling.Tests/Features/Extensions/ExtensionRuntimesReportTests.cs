// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// <c>morphir extension runtimes</c> (task 4.2 of <c>add-wasm-extension-host</c>, design RPT-1): the report of the
/// WASM runtime, as one JSON document and as text, for an available runtime and for each unavailable reason. The
/// report of the real CLI process is in tests/Morphir.BlackBox.Tests/Integration/Extensions.feature.
/// </summary>
public sealed class ExtensionRuntimesReportTests
{
    private static readonly HostIdentity Native = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));
    private static readonly HostIdentity Tool = TestHosts.Unprofiled(MorphirHost.DotnetTool, Native.Version);

    /// <summary>The unavailable reasons, with the platform a probe would give each on.</summary>
    public static IEnumerable<(string Reason, string Platform)> Reasons() =>
    [
        ("platform_unsupported", "linux-musl-x64"),
        ("engine_missing", "win-arm64"),
        ("engine_load_failed", "linux-x64"),
        ("win_x64_trap_unsupported", "win-x64"),
    ];

    private static WasmRuntimeUnavailableReason Reason(string wire) => wire switch
    {
        "platform_unsupported" => WasmRuntimeUnavailableReason.PlatformUnsupported,
        "engine_missing" => WasmRuntimeUnavailableReason.EngineMissing,
        "engine_load_failed" => WasmRuntimeUnavailableReason.EngineLoadFailed,
        "win_x64_trap_unsupported" => WasmRuntimeUnavailableReason.WinX64TrapUnsupported,
        _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, null),
    };

    private static WasmRuntime Runtime(string platform, WasmRuntimeAvailability availability) => new(platform, () => availability);

    private static (int Exit, string Stdout) Run(HostIdentity host, WasmRuntime runtime, bool json)
    {
        var stdout = new List<string>();
        var exit = ExtensionRuntimesCommand.Execute(host, runtime, json, stdout.Add);
        return (exit, string.Join('\n', stdout).Replace("\r\n", "\n"));
    }

    private static JsonElement Json(HostIdentity host, WasmRuntime runtime)
    {
        var (exit, stdout) = Run(host, runtime, json: true);
        exit.Should().Be(0);
        using var document = JsonDocument.Parse(stdout);
        return document.RootElement.Clone();
    }

    private static string[] Members(JsonElement element) => element.EnumerateObject().Select(member => member.Name).ToArray();

    // --- JSON ---------------------------------------------------------------------------------------------------

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void AnAvailableRuntimeIsReportedWithItsEngineProfilesAndBounds(bool tool)
    {
        var report = Json(tool ? Tool : Native, Runtime("win-arm64", WasmRuntimeAvailability.Available));

        Members(report).Should().Equal("host", "platform", "runtimes");
        report.GetProperty("host").GetString().Should().Be(tool ? "dotnet-morphir" : "morphir");
        report.GetProperty("platform").GetString().Should().Be("win-arm64");
        var wasm = report.GetProperty("runtimes").EnumerateArray().Should().ContainSingle().Subject;
        Members(wasm).Should().Equal("kind", "available", "engine", "profiles", "bounds");
        wasm.GetProperty("kind").GetString().Should().Be("wasm");
        wasm.GetProperty("available").GetBoolean().Should().BeTrue();
        Members(wasm.GetProperty("engine")).Should().Equal("name", "version");
        wasm.GetProperty("engine").GetProperty("name").GetString().Should().Be("wasmtime");
        wasm.GetProperty("engine").GetProperty("version").GetString().Should().Be(WasmRuntimeIdentity.EngineVersion);
        wasm.GetProperty("profiles").EnumerateArray().Select(profile => profile.GetString()).Should().Equal("extism-kernel-v1");
        var bounds = wasm.GetProperty("bounds");
        Members(bounds).Should().Equal("memoryBytes", "deadline", "budget");
        bounds.GetProperty("memoryBytes").GetInt64().Should().Be(268435456);
        bounds.GetProperty("deadline").GetString().Should().Be("enforced");
        bounds.GetProperty("budget").GetString().Should().Be("enforced");
    }

    [Test]
    [MethodDataSource(nameof(Reasons))]
    public void AnUnavailableRuntimeIsReportedWithItsReasonAndItsEngine(string reason, string platform)
    {
        var report = Json(Native, Runtime(platform, WasmRuntimeAvailability.UnavailableBecause(Reason(reason))));

        Members(report).Should().Equal("host", "platform", "runtimes");
        report.GetProperty("platform").GetString().Should().Be(platform);
        var wasm = report.GetProperty("runtimes").EnumerateArray().Should().ContainSingle().Subject;
        Members(wasm).Should().Equal("kind", "available", "reason", "engine");
        wasm.GetProperty("kind").GetString().Should().Be("wasm");
        wasm.GetProperty("available").GetBoolean().Should().BeFalse();
        wasm.GetProperty("reason").GetString().Should().Be(reason);
        wasm.GetProperty("engine").GetProperty("name").GetString().Should().Be("wasmtime");
        wasm.GetProperty("engine").GetProperty("version").GetString().Should().Be(WasmRuntimeIdentity.EngineVersion);
    }

    /// <summary>No build identity of the deployment exists at run time yet, so the report does not guess one.</summary>
    [Test]
    public void TheReportHasNoDeploymentItCannotKnow()
    {
        var report = Json(Native, Runtime("win-arm64", WasmRuntimeAvailability.Available));
        report.TryGetProperty("deployment", out _).Should().BeFalse();
        report.TryGetProperty("offering", out _).Should().BeFalse();
    }

    // --- Text ---------------------------------------------------------------------------------------------------

    [Test]
    public void TheTextOfAnAvailableRuntime()
    {
        var (exit, stdout) = Run(Native, Runtime("win-arm64", WasmRuntimeAvailability.Available), json: false);

        exit.Should().Be(0);
        stdout.Should().Be("""
            Host: morphir
            Platform: win-arm64
            wasm: available
              Engine: wasmtime 50.0.1
              Profiles: extism-kernel-v1
              Bounds: memory ceiling 268435456 bytes (256 MiB), call deadline enforced, instruction budget enforced
            """.Replace("\r\n", "\n"));
    }

    [Test]
    [MethodDataSource(nameof(Reasons))]
    public void TheTextOfAnUnavailableRuntimeNamesTheReasonAndExplainsIt(string reason, string platform)
    {
        var why = Reason(reason);
        var (exit, stdout) = Run(Tool, Runtime(platform, WasmRuntimeAvailability.UnavailableBecause(why)), json: false);

        exit.Should().Be(0);
        stdout.Should().Be($"""
            Host: dotnet-morphir
            Platform: {platform}
            wasm: unavailable ({reason})
              Engine: wasmtime 50.0.1
              Reason: {why.Description}
            """.Replace("\r\n", "\n"));
    }

    // --- The command --------------------------------------------------------------------------------------------

    [Test]
    public void TheReportProbesTheRuntimeOnce()
    {
        var probes = 0;
        var runtime = new WasmRuntime("win-arm64", () =>
        {
            probes++;
            return WasmRuntimeAvailability.UnavailableBecause(WasmRuntimeUnavailableReason.EngineMissing);
        });

        Run(Native, runtime, json: true);
        Run(Native, runtime, json: false);

        probes.Should().Be(1);
    }
}
