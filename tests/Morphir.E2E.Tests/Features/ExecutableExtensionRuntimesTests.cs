// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Morphir.TestSupport;

namespace Morphir.E2E.Tests.Features;

/// <summary>
/// <c>extension runtimes</c> and <c>extension.runtime_unavailable</c> of the executable under test (task 4.2 of
/// add-wasm-extension-host). The unmodified executable reports the engine of its Wasmtime package. A copy of its output
/// folder with the engine's native library deleted is a host started without its engine library: it reports
/// <c>engine_missing</c> on a runnable platform, or the platform's safety refusal. It refuses a worker by name and still
/// runs the commands that need no WASM. The real build output
/// is never changed. Run the assembly once with <c>MORPHIR_EXECUTABLE_TYPE=tool</c> and once with
/// <c>MORPHIR_EXECUTABLE_PATH</c> at a <c>morphir</c> build to cover both hosts.
/// </summary>
public sealed class ExecutableExtensionRuntimesTests
{
    /// <summary>The file names of the engine's native library in the Wasmtime package, one per operating system.</summary>
    static readonly string[] EngineLibraries = ["wasmtime.dll", "libwasmtime.so", "libwasmtime.dylib"];

    static readonly Lazy<Task<EngineFreeCopy>> Copy = new(() => EngineFreeCopy.CreateAsync(ExecutableExtensionWorkerSteps.Executable));

    [TUnit.Core.After(HookType.Class)]
    public static async Task RemoveTheCopy()
    {
        if (Copy.IsValueCreated) await (await Copy.Value).DisposeAsync();
    }

    static async Task<ExecutableExecutionResult> Run(string executable, string arguments) =>
        await new ExecutableRunner(executable).ExecuteCommandAsync(new TestDataResolver().ResolvePath(arguments), TimeSpan.FromSeconds(60));

    static JsonElement Wasm(ExecutableExecutionResult result)
    {
        using var document = JsonDocument.Parse(result.StandardOutput);
        document.RootElement.GetProperty("host").GetString().Should().Be(ExecutableLocator.HostFor(ExecutableLocator.CurrentType));
        return document.RootElement.GetProperty("runtimes").EnumerateArray().Should().ContainSingle().Subject.Clone();
    }

    internal static bool IsWindowsX64(string report)
    {
        using var document = JsonDocument.Parse(report);
        return document.RootElement.GetProperty("platform").GetString() == "win-x64";
    }

    internal static async Task RequireGuestRuntimeAsync(string executable)
    {
        var report = await Run(executable, "extension runtimes --json");
        report.ExitCode.Should().Be(0, report.CombinedOutput);
        var wasm = Wasm(report);
        if (IsWindowsX64(report.StandardOutput))
        {
            wasm.GetProperty("available").GetBoolean().Should().BeFalse(report.StandardOutput);
            wasm.GetProperty("reason").GetString().Should().Be("win_x64_trap_unsupported");
            Skip.Test("win_x64_trap_unsupported: the executable cannot run guests; #609.");
        }
        wasm.GetProperty("available").GetBoolean().Should().BeTrue(report.StandardOutput);
    }

    /// <summary>The version of the Wasmtime package that <c>Directory.Packages.props</c> pins.</summary>
    internal static string PackageVersion()
    {
        var props = XDocument.Load(Path.Combine(WorkerCorpus.Fixtures, "..", "..", "..", "Directory.Packages.props"));
        return props.Descendants("PackageVersion").Single(element => (string?)element.Attribute("Include") == "Wasmtime").Attribute("Version")!.Value;
    }

    static async Task<EngineFreeCopy> EngineFree()
    {
        var copy = await Copy.Value;
        if (copy.Deleted.Count == 0)
            Skip.Test($"{copy.Source} carries no engine library file to delete (the engine is linked into the Native AOT executable, or a single-file bundle embeds it); run this test against a build that loads the engine from a file");
        return copy;
    }

    // --- The unmodified executable ------------------------------------------------------------------------------

    [Test]
    public async Task The_executable_reports_its_platforms_wasm_availability_with_the_engine_of_its_package()
    {
        var result = await Run(ExecutableExtensionWorkerSteps.Executable, "extension runtimes --json");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var wasm = Wasm(result);
        wasm.GetProperty("available").GetBoolean().Should().Be(!IsWindowsX64(result.StandardOutput), result.StandardOutput);
        if (IsWindowsX64(result.StandardOutput))
            wasm.GetProperty("reason").GetString().Should().Be("win_x64_trap_unsupported");
        wasm.GetProperty("engine").GetProperty("name").GetString().Should().Be("wasmtime");
        wasm.GetProperty("engine").GetProperty("version").GetString().Should().Be(PackageVersion());
        wasm.GetProperty("profiles").EnumerateArray().Select(profile => profile.GetString()).Should().Equal("extism-kernel-v1");
        wasm.GetProperty("bounds").GetProperty("memoryBytes").GetInt64().Should().Be(268435456);
    }

    // --- A copy without the engine library ----------------------------------------------------------------------

    [Test]
    public async Task Without_its_engine_library_the_executable_reports_the_applicable_unavailable_reason()
    {
        var copy = await EngineFree();

        var result = await Run(copy.Executable, "extension runtimes --json");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var wasm = Wasm(result);
        wasm.GetProperty("available").GetBoolean().Should().BeFalse(result.StandardOutput);
        wasm.GetProperty("reason").GetString().Should().Be(IsWindowsX64(result.StandardOutput)
            ? "win_x64_trap_unsupported" : "engine_missing");
        wasm.GetProperty("engine").GetProperty("version").GetString().Should().Be(PackageVersion());
        result.StandardError.Should().NotContain("DllNotFoundException");
    }

    [Test]
    public async Task Without_its_engine_library_a_worker_is_refused_by_name_with_no_stdout_bytes()
    {
        var copy = await EngineFree();
        var report = await Run(copy.Executable, "extension runtimes --json");
        report.ExitCode.Should().Be(0, report.CombinedOutput);
        var identity = await ExecutableExtensionWorkerSteps.IdentityAsync(copy.Executable);
        await using var worker = await WorkerProcess.StartAsync(copy.Executable,
            ["--abi", "extism-kernel-v1", "--module", WorkerCorpus.ModulePath("extism-echo"), "--build-identity", identity, "--json"]);
        await worker.WriteAsync(WorkerProcess.Frame(WorkerCorpus.FrameBody("initialize")));

        var run = await worker.WaitForExitAsync(TimeSpan.FromSeconds(60));

        run.ExitCode.Should().Be(3, run.Describe());
        run.Stdout.Should().BeEmpty();
        run.EventNames.Should().Equal(["refused", "exited"], run.Describe());
        var refused = run.Events.Single(e => e.Name == "refused").Json.GetProperty("error");
        refused.GetProperty("code").GetString().Should().Be("extension.runtime_unavailable");
        refused.GetProperty("message").GetString().Should().Contain(IsWindowsX64(report.StandardOutput)
            ? "win_x64_trap_unsupported" : "engine_missing").And.NotContain("DllNotFoundException");
    }

    [Test]
    [Arguments("--version")]
    [Arguments("--help")]
    [Arguments("extension list --json")]
    [Arguments("ir verify <test-data>/valid-ir-v3.json --json")]
    public async Task Without_its_engine_library_the_commands_that_need_no_wasm_still_work(string arguments)
    {
        var copy = await EngineFree();

        var result = await Run(copy.Executable, arguments);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        // The help's stream depends on how the CLI's console is redirected, so it is read from either, as the
        // basic-commands scenario reads it; every other command here writes its result to stdout.
        if (arguments == "--help")
            result.CombinedOutput.Should().Contain("Commands:");
        else
            result.StandardOutput.Should().NotBeEmpty();
        result.CombinedOutput.Should().NotContain("DllNotFoundException").And.NotContain("wasmtime");
        if (arguments.EndsWith("--json", StringComparison.Ordinal))
        {
            var parse = () => JsonDocument.Parse(result.StandardOutput).Dispose();
            parse.Should().NotThrow(result.StandardOutput);
        }
    }

    /// <summary>The output folder of an executable, copied into a sandbox with every engine library file deleted.</summary>
    sealed class EngineFreeCopy : IAsyncDisposable
    {
        readonly TestSandbox sandbox;

        EngineFreeCopy(TestSandbox sandbox, string source, string executable, IReadOnlyList<string> deleted)
        {
            this.sandbox = sandbox;
            Source = source;
            Executable = executable;
            Deleted = deleted;
        }

        public string Source { get; }

        public string Executable { get; }

        /// <summary>The engine library files deleted from the copy, relative to its folder.</summary>
        public IReadOnlyList<string> Deleted { get; }

        public static Task<EngineFreeCopy> CreateAsync(string executable) => Task.Run(() =>
        {
            var sandbox = TestSandbox.Create("morphir-engine-free-");
            var from = Path.GetDirectoryName(Path.GetFullPath(executable))!;
            var to = Directory.CreateDirectory(sandbox.PathOf("host")).FullName;
            foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));

            var deleted = new List<string>();
            foreach (var file in Directory.EnumerateFiles(to, "*", SearchOption.AllDirectories)
                         .Where(file => EngineLibraries.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)))
            {
                File.Delete(file);
                deleted.Add(Path.GetRelativePath(to, file));
            }

            return new EngineFreeCopy(sandbox, executable, Path.Combine(to, Path.GetFileName(executable)), deleted);
        });

        public ValueTask DisposeAsync() => sandbox.DisposeAsync();
    }
}
