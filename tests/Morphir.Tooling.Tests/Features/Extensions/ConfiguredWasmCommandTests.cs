// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.Extensions.Wasm.Tests;
using Morphir.Testing;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Tests.Features.Compile;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Extensions;

public sealed class ConfiguredWasmCommandTests
{
    private static CompileWorkspace Workspace(string module)
    {
        var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", $"[project]\nname = \"acme/demo\"\n[extensions.python]\npath = {JsonSerializer.Serialize(module)}\nabi = \"extism-kernel-v1\"\nsha256 = \"{PublishedGuest.Sha256}\"\n");
        return workspace;
    }

    [Test]
    public async Task ThePythonTargetSelectsTheConfiguredModuleWithoutAnExtensionOption()
    {
        using var workspace = Workspace("python.wasm");
        var input = GenerationInputFixtures.DailyRate(workspace.Root, "4", "json");
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(), new FileSystemCompileConfigurationLoader());
        var result = await resolver.ResolveAsync(new(input, "python", workspace.PathOf("generated")) { Project = workspace.Root },
            new InvocationContext(workspace.Root) { Environment = workspace.Context().Environment }, default);
        var settings = result.Should().BeOfType<Outcome<GenerationSettings>.Success>().Subject.Value;
        settings.Extension.Should().NotBeNull();
        settings.Extension!.Value.Should().Be("python");
    }

    [Test]
    public void ThePythonLanguageSelectsTheConfiguredModuleWithoutAnExtensionOption()
    {
        using var workspace = Workspace("python.wasm");
        workspace.Write("add.py", "def add(a: int, b: int) -> int:\n    return a + b\n");
        var result = new ProjectCompileSettingsResolver().Resolve(new CompileOverrides
        {
            Project = workspace.Root, Language = "python", Inputs = ["add.py"], IrVersion = "4",
        }, workspace.Context());
        var settings = result.Match((value, _) => value, failure => throw new InvalidOperationException(string.Join("; ", failure)));
        settings.Extension.Should().NotBeNull();
        settings.Extension!.Value.Should().Be("python");
    }

    private static async Task<CompileWorkspace> PublishedWorkspace()
    {
        if (WasmRuntime.Process.Platform == "win-x64")
            Skip.Test("win_x64_trap_unsupported: published guest execution is unavailable; #609.");
        var bytes = await PublishedGuest.BytesAsync();
        var workspace = Workspace("python.wasm");
        await File.WriteAllBytesAsync(workspace.PathOf("python.wasm"), bytes);
        return workspace;
    }

    [Test]
    public async Task GenerateRunsTheRealPinnedPythonBundleThroughTheProductCli()
    {
        using var workspace = await PublishedWorkspace();
        var input = GenerationInputFixtures.DailyRate(workspace.Root, "4", "json");
        var output = workspace.PathOf("generated");
        var result = await new CliTestHelper().ExecuteCommandAsync(
            $"generate --project \"{workspace.Root}\" --input \"{input}\" --target python --output \"{output}\" --json");
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        report.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        report.RootElement.GetProperty("backendTransport").GetString().Should().Be("wasm");
        Directory.GetFiles(output, "*.py", SearchOption.AllDirectories).Should().NotBeEmpty();
    }

    [Test]
    public async Task CompileRunsTheRealPinnedPythonBundleThroughTheProductCli()
    {
        using var workspace = await PublishedWorkspace();
        workspace.Write("add.py", "def add(a: int, b: int) -> int:\n    return a + b\n");
        var result = await new CliTestHelper().ExecuteCommandAsync(
            $"compile --project \"{workspace.Root}\" --language python --input \"{workspace.PathOf("add.py")}\" --ir-version 4 --json");
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        report.RootElement.GetProperty("frontend").GetString().Should().Contain("morphir-python").And.Contain("wasm");
        File.Exists(report.RootElement.GetProperty("output").GetString()).Should().BeTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WindowsX64RefusesTheSelectedModuleByNameBeforeReadingIt(bool compile)
    {
        if (WasmRuntime.Process.Platform != "win-x64")
            Skip.Test("This control exercises the Windows x64 product refusal.");
        using var workspace = Workspace("missing-python.wasm");
        var input = GenerationInputFixtures.DailyRate(workspace.Root, "4", "json");
        var source = workspace.Write("add.py", "def add(a: int, b: int) -> int:\n    return a + b\n");
        var output = workspace.PathOf("generated");
        var command = compile
            ? $"compile --project \"{workspace.Root}\" --language python --input \"{source}\" --ir-version 4 --json"
            : $"generate --project \"{workspace.Root}\" --input \"{input}\" --target python --output \"{output}\" --json";
        var result = await new CliTestHelper().ExecuteCommandAsync(command);
        result.ExitCode.Should().Be(2, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("extension.runtime_unavailable").And.Contain("win_x64_trap_unsupported");
        Directory.Exists(output).Should().BeFalse();
    }
}
