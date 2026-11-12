// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Extensions;
using Morphir.Extensions.Wasm;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// <c>morphir extension worker</c> in a host whose WASM runtime is unavailable (task 4.2 of
/// <c>add-wasm-extension-host</c>; requirement "Unavailable runtime fails by name"): one <c>refused</c> event with
/// <c>extension.runtime_unavailable</c> before the module is read, exit 3 (a refusal before any request), and no stdout
/// bytes.
/// </summary>
public sealed class ExtensionWorkerRuntimeTests
{
    private sealed class CountingLoader(WasmRuntimeAvailability answer, string platform = "win-arm64")
    {
        public int Probes;

        public IWasmModuleLoader Loader => field ??= new WasmModuleLoader(new WasmRuntime(platform, () =>
        {
            Interlocked.Increment(ref Probes);
            return answer;
        }), WorkerHarness.Host);
    }

    private static Task<WorkerRunResult> Run(IReadOnlyList<string> arguments, IWasmModuleLoader loader) =>
        WorkerHarness.RunAsync(arguments, new MemoryStream(WorkerHarness.Frame(WorkerHarness.Initialize)), null, CancellationToken.None, loader: loader);

    public static IEnumerable<(string Reason, string Platform)> Reasons() =>
    [
        ("platform_unsupported", "linux-musl-x64"),
        ("engine_missing", "win-arm64"),
        ("engine_load_failed", "linux-x64"),
        ("win_x64_trap_unsupported", "win-x64"),
    ];

    private static WasmRuntimeAvailability Unavailable(string reason) => WasmRuntimeAvailability.UnavailableBecause(reason switch
    {
        "platform_unsupported" => WasmRuntimeUnavailableReason.PlatformUnsupported,
        "engine_missing" => WasmRuntimeUnavailableReason.EngineMissing,
        "engine_load_failed" => WasmRuntimeUnavailableReason.EngineLoadFailed,
        "win_x64_trap_unsupported" => WasmRuntimeUnavailableReason.WinX64TrapUnsupported,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    });

    [Test]
    [MethodDataSource(nameof(Reasons))]
    public async Task AWorkerWithoutTheRuntimeRefusesByNameWithNoStdoutBytesAndStatus3(string reason, string platform)
    {
        var loader = new CountingLoader(Unavailable(reason), platform);

        var run = await Run(WorkerHarness.Arguments(WorkerHarness.Corpus("extism-echo")), loader.Loader);

        run.ExitCode.Should().Be(ExtensionWorker.Refused, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.EventNames.Should().Equal(["refused", "exited"], run.Describe());
        var refused = run.Single("refused");
        refused.Text("error.code").Should().Be("extension.runtime_unavailable");
        refused.Text("error.message").Should().Contain(reason).And.Contain(platform).And.Contain("morphir");
        run.Single("exited").Text("exitCode").Should().Be("3");
        loader.Probes.Should().Be(1);
    }

    [Test]
    public async Task AWorkerWithoutTheRuntimeDoesNotReadTheModule()
    {
        using var sandbox = TestSandbox.Create();
        var loader = new CountingLoader(Unavailable("engine_missing"));

        var run = await Run(WorkerHarness.Arguments(Path.Combine(sandbox.Work, "no-such-module.wasm")), loader.Loader);

        run.ExitCode.Should().Be(ExtensionWorker.Refused, run.Describe());
        run.Single("refused").Text("error.code").Should().Be("extension.runtime_unavailable");
        run.EventNames.Should().NotContain("fault", "a module that is not read cannot be missing");
    }

    [Test]
    public async Task WithoutJsonTheRefusalIsOneTextLineOnStderr()
    {
        var loader = new CountingLoader(Unavailable("engine_missing"));
        List<string> arguments = ["--abi", "extism-kernel-v1", "--module", WorkerHarness.Corpus("extism-echo"), "--build-identity", WorkerHarness.Identity];

        var run = await Run(arguments, loader.Loader);

        run.ExitCode.Should().Be(ExtensionWorker.Refused, run.Describe());
        run.Stdout.Should().BeEmpty();
        string.Join('\n', run.StderrLines).Should().Contain("extension.runtime_unavailable").And.Contain("engine_missing");
    }

    [Test]
    public async Task AnotherBuildIdentityIsRefusedFirstAndTheRuntimeIsNotProbed()
    {
        var loader = new CountingLoader(Unavailable("engine_missing"));
        List<string> arguments = ["--abi", "extism-kernel-v1", "--module", WorkerHarness.Corpus("extism-echo"), "--build-identity", new string('0', 64), "--json"];

        var run = await Run(arguments, loader.Loader);

        run.ExitCode.Should().Be(ExtensionWorker.Refused, run.Describe());
        run.Single("refused").Text("error.code").Should().Be("extension.worker_runtime_mismatch");
        loader.Probes.Should().Be(0);
    }

    [Test]
    [Arguments("--help --json")]
    [Arguments("--help")]
    public async Task TheHelpNeedsNoRuntime(string arguments)
    {
        var loader = new CountingLoader(Unavailable("engine_missing"));

        var run = await Run(arguments.Split(' '), loader.Loader);

        run.ExitCode.Should().Be(ExtensionWorker.Served, run.Describe());
        loader.Probes.Should().Be(0);
    }

    [Test]
    public async Task ArgumentsAreCheckedBeforeTheRuntime()
    {
        var loader = new CountingLoader(Unavailable("engine_missing"));

        var run = await Run(["--module", WorkerHarness.Corpus("extism-echo"), "--json"], loader.Loader);

        run.ExitCode.Should().Be(ExtensionWorker.Usage, run.Describe());
        loader.Probes.Should().Be(0);
    }

    /// <summary>
    /// Each row of the contract's worker rule <c>start-order</c> (<see cref="WasmProfileContract.WorkerStartOrder"/>)
    /// with that check and every later one failing, so that only the order decides which failure is reported.
    /// </summary>
    public static IEnumerable<(string FirstFailing, int ExitCode, string? Code)> StartOrder() =>
    [
        ("arguments", ExtensionWorker.Usage, null),
        ("build-identity", ExtensionWorker.Refused, "extension.worker_runtime_mismatch"),
        ("engine", ExtensionWorker.Refused, "extension.runtime_unavailable"),
        ("module", ExtensionWorker.Faulted, null),
    ];

    [Test]
    [MethodDataSource(nameof(StartOrder))]
    public async Task TheWorkerReportsTheFirstFailingCheckInTheContractStartOrder(string firstFailing, int exitCode, string? code)
    {
        using var sandbox = TestSandbox.Create();
        var order = WasmProfileContract.WorkerStartOrder;
        var failing = order.SkipWhile(check => check != firstFailing).ToHashSet(StringComparer.Ordinal);
        failing.Should().Contain(firstFailing, "every row names a check of the contract's order");
        var loader = new CountingLoader(failing.Contains("engine") ? Unavailable("engine_missing") : WasmRuntimeAvailability.Available);
        var module = failing.Contains("module") ? Path.Combine(sandbox.Work, "no-such-module.wasm") : WorkerHarness.Corpus("extism-echo");
        List<string> arguments = failing.Contains("arguments")
            ? ["--module", module, "--json"]
            : ["--abi", "extism-kernel-v1", "--module", module, "--build-identity", failing.Contains("build-identity") ? new string('0', 64) : WorkerHarness.Identity, "--json"];

        var run = await Run(arguments, loader.Loader);

        run.ExitCode.Should().Be(exitCode, run.Describe());
        run.Stdout.Should().BeEmpty();
        if (code is null)
            run.EventNames.Should().NotContain("refused", run.Describe());
        else
            run.Single("refused").Text("error.code").Should().Be(code);
        if (firstFailing == "module") run.Single("fault").Text("fault.kind").Should().Be("failure");
        var position = order.ToList();
        loader.Probes.Should().Be(position.IndexOf(firstFailing) >= position.IndexOf("engine") ? 1 : 0,
            "the arguments and the build identity are checked without the engine");
    }

    [Test]
    public void EveryCodeARefusedEventCanCarryIsARefusalOfTheContract()
    {
        // ExtensionWorker writes refused with RuntimeMismatchCode or with the code of the loader's failure: a
        // RuntimeUnavailable failure, or a module refused with one of WasmRefusal.Codes. The contract lists the module
        // refusals in refusals and the worker's own in worker.refusals.
        var unavailable = new WasmModuleFailure.RuntimeUnavailable("worker", "module.wasm", WorkerHarness.Host, "win-arm64",
            WasmRuntimeUnavailableReason.EngineMissing);
        string[] codes = [ExtensionWorker.RuntimeMismatchCode, unavailable.Code!, .. WasmRefusal.Codes];
        var contract = WasmProfileContract.Pinned;

        codes.Should().OnlyHaveUniqueItems().And.BeEquivalentTo([.. contract.RefusalCodes, .. contract.WorkerRefusalCodes]);
        contract.WorkerRefusalCodes.Should().BeEquivalentTo([ExtensionWorker.RuntimeMismatchCode, WasmRuntimeUnavailable.Code]);
    }

    [Test]
    public async Task AWorkerWithTheRuntimeServesAsBefore()
    {
        var loader = new CountingLoader(WasmRuntimeAvailability.Available);

        var run = await WorkerHarness.RunAsync(WorkerHarness.Arguments(WorkerHarness.Corpus("extism-echo")),
            new MemoryStream([.. WorkerHarness.Frame(WorkerHarness.Initialize), .. WorkerHarness.Frame(WorkerHarness.Exit)]), null, CancellationToken.None,
            loader: loader.Loader);

        run.ExitCode.Should().Be(ExtensionWorker.Served, run.Describe());
        run.EventNames.Should().Equal(["started", "exited"], run.Describe());
        run.FrameTexts.Should().ContainSingle();
        loader.Probes.Should().Be(1);
    }
}
