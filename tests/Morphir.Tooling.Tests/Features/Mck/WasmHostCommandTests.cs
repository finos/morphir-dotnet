// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Mck;

/// <summary>Both product hosts forward unchanged to the separately built development runner.</summary>
public sealed class WasmHostCommandTests
{
    private static string Root => FindRoot(AppContext.BaseDirectory);
    private static string FindRoot(string path) => File.Exists(Path.Combine(path, "Morphir.slnx"))
        ? path : FindRoot(Directory.GetParent(path)!.FullName);

    private static async Task<ChildProcessResult> InvokeAsync(CliHost host, string runner, params string[] arguments)
    {
        await using var sandbox = TestSandbox.Create("mck-forwarding-");
        var product = host == CliHost.Morphir ? "Morphir" : "Morphir.Tool";
        var configuration = typeof(WasmHostCommandTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var dotnet = Environment.ProcessPath is { } path && Path.GetFileNameWithoutExtension(path) == "dotnet" ? path : "dotnet";
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "run", "--project", Path.Combine(Root, "src", product, product + ".csproj"),
                     "--configuration", configuration, "--no-build", "--", "mck" }.Concat(arguments)) start.ArgumentList.Add(argument);
        sandbox.Apply(start);
        start.WorkingDirectory = sandbox.Work;
        start.Environment["MORPHIR_MCK"] = runner;
        return await ChildProcess.RunAsync(start, TimeSpan.FromSeconds(30));
    }

    [Test]
    [Arguments(CliHost.Morphir)]
    [Arguments(CliHost.DotnetTool)]
    public async Task BothHostsHaveTheExperimentalCommands(CliHost host)
    {
        var runner = Path.Combine(AppContext.BaseDirectory, "DevelopmentMck", OperatingSystem.IsWindows() ? "morphir-mck.exe" : "morphir-mck");
        File.Exists(runner).Should().BeTrue("the complete development output is copied from GetTargetPath in the active configuration");
        var help = await InvokeAsync(host, runner, "wasm-host", "run", "--help");
        help.ExitCode.Should().Be(0, help.StandardError);
        (help.StandardOutput + help.StandardError).Should().Contain("--kit").And.Contain("--host");
        var usage = await InvokeAsync(host, runner, "wasm-host", "report-check");
        usage.ExitCode.Should().Be(2, usage.StandardError);
        using var document = JsonDocument.Parse(usage.StandardOutput);
        document.RootElement.GetProperty("command").GetString().Should().Be("mck wasm-host report-check");
    }

    [Test]
    [Arguments(CliHost.Morphir)]
    [Arguments(CliHost.DotnetTool)]
    public async Task BothHostsRefuseWhenTheSeparateRunnerIsMissing(CliHost host)
    {
        var result = await InvokeAsync(host, Path.Combine(Root, "no-such-development-runner"), "wasm-host", "run", "--help");
        result.ExitCode.Should().Be(127, result.StandardError);
        result.StandardOutput.Should().BeEmpty();
        result.StandardError.Should().Contain("MORPHIR_MCK");
    }
}
