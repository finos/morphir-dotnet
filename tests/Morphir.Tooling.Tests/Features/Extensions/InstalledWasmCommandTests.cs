// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.Extensions.Wasm.Tests;
using Morphir.Testing;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Tests.Features.Compile;
using Morphir.Tooling.Tests.Features.Generate;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// An installed WASM extension (design WASM-2 of add-nuget-extension-source): the pinned published Python guest,
/// installed from a NuGet folder feed with <c>extension install nuget:</c>, then run by <c>compile</c> and
/// <c>generate</c> with <c>--extension-id</c> through the loader that configured modules use.
/// </summary>
[Category("PlatformSpecific")]
public sealed class InstalledWasmCommandTests
{
    private const string Id = "morphir-python";

    /// <summary>The claims of the published release, in the extension family's embedded form.</summary>
    private static string Claims(string name) =>
        "{\"protocolVersions\":[\"0.2.0-draft.1\"],\"extension\":{\"id\":\"morphir-python\",\"name\":\"" + name
        + "\",\"version\":\"0.6.0\",\"types\":[\"frontend\",\"backend\"]},\"capabilities\":" + Capabilities + "}";

    /// <summary>The capabilities of <c>morphir-python-extension-0.6.0.release.json</c> of the same release.</summary>
    private const string Capabilities = "{\"frontend\":{\"languages\":[{\"id\":\"python\",\"fileExtensions\":[\".py\"]}],\"irVersions\":[\"3\",\"4\"],\"compile\":true,\"incremental\":false,\"fragments\":false,\"multiDocument\":true},\"backend\":{\"targets\":[\"python\"],\"irVersions\":[\"3\",\"4\"],\"generate\":true},\"modelTransport\":{\"tuples\":[{\"operation\":\"morphir.frontend.compileModel\",\"profile\":\"morphir-ir\",\"codec\":\"morphir-json\",\"envelopeVersion\":\"0.1.0-draft.1\",\"irVersion\":\"3.0.0\"},{\"operation\":\"morphir.frontend.compileModel\",\"profile\":\"morphir-ir\",\"codec\":\"morphir-json\",\"envelopeVersion\":\"0.1.0-draft.1\",\"irVersion\":\"4.0.0\"},{\"operation\":\"morphir.backend.generateModel\",\"profile\":\"morphir-ir\",\"codec\":\"morphir-json\",\"envelopeVersion\":\"0.1.0-draft.1\",\"irVersion\":\"3.0.0\"},{\"operation\":\"morphir.backend.generateModel\",\"profile\":\"morphir-ir\",\"codec\":\"morphir-json\",\"envelopeVersion\":\"0.1.0-draft.1\",\"irVersion\":\"4.0.0\"}],\"limits\":{\"maxEncodedBytes\":12582912,\"maxDecodedBytes\":8388608,\"maxDepth\":128,\"maxNodes\":100000},\"guarantees\":[]},\"streaming\":false,\"incremental\":false,\"cancellation\":false,\"progress\":false}";

    private static byte[] Package(byte[] module, string name)
    {
        var release = "{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/release.schema.json\",\"extensionId\":\"morphir-python\",\"shortId\":\"morphir-python\",\"version\":\"0.6.0\",\"platformDifferences\":\"none\","
            + "\"artifacts\":[{\"runtime\":\"wasm\",\"filename\":\"" + PublishedGuest.FileName + "\",\"sha256\":\"" + PublishedGuest.Sha256 + "\",\"claims\":" + Claims(name) + "}]}";
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string entry, byte[] bytes)
            {
                using var output = zip.CreateEntry(entry).Open();
                output.Write(bytes);
            }

            Add("Morphir.Python.nuspec", Encoding.UTF8.GetBytes(
                """<?xml version="1.0" encoding="utf-8"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>Morphir.Python</id><version>0.6.0</version><authors>FINOS</authors><description>Morphir Python.</description><packageTypes><packageType name="MorphirExtension" /></packageTypes></metadata></package>"""));
            Add("morphir/release.json", Encoding.UTF8.GetBytes(release));
            Add($"morphir/{PublishedGuest.FileName}", module);
        }

        return memory.ToArray();
    }

    /// <summary>A workspace with the guest installed in its own Home from a folder feed.</summary>
    private static async Task<(CompileWorkspace Workspace, string Home)> InstalledAsync(string name = "Morphir Python")
    {
        if (WasmRuntime.Process.Platform == "win-x64")
            Skip.Test("win_x64_trap_unsupported: published guest execution is unavailable; #609.");
        var module = await PublishedGuest.BytesAsync();
        var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/demo\"\n");
        var feed = workspace.PathOf("feed");
        Directory.CreateDirectory(feed);
        await File.WriteAllBytesAsync(Path.Combine(feed, "morphir.python.0.6.0.nupkg"), Package(module, name));
        var home = workspace.PathOf("home");
        var installed = await CliTestHelper.ForTarget().ExecuteCommandAsync(
            $"extension install nuget:Morphir.Python@0.6.0 --source \"{feed}\" --home \"{home}\" --no-probe");
        installed.ExitCode.Should().Be(0, installed.CombinedOutput);
        return (workspace, home);
    }

    private static Dictionary<string, string?> HomeOf(string home) => new() { ["MORPHIR_HOME"] = home };

    [Test]
    public async Task CompileRunsAnInstalledWasmFrontendAndChecksItsClaims()
    {
        var (workspace, home) = await InstalledAsync();
        using var _ = workspace;
        using (var lockDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(home, "locks", "extensions", $"{Id}.json"))))
            lockDocument.RootElement.GetProperty("runtime").GetString().Should().Be("wasm");
        workspace.Write("identity.py", "def identity(value: int) -> int:\n    return value\n");
        var result = await CliTestHelper.ForTarget().ExecuteCommandAsync(
            $"compile --project \"{workspace.Root}\" --language python --input \"{workspace.PathOf("identity.py")}\" --ir-version 4 --extension-id {Id} --json",
            environment: HomeOf(home));
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        File.Exists(report.RootElement.GetProperty("output").GetString()).Should().BeTrue();
    }

    [Test]
    public async Task GenerateRunsAnInstalledWasmBackend()
    {
        var (workspace, home) = await InstalledAsync();
        using var _ = workspace;
        var input = GenerationInputFixtures.DailyRate(workspace.Root, "4", "json");
        var output = workspace.PathOf("generated");
        var result = await CliTestHelper.ForTarget().ExecuteCommandAsync(
            $"generate --project \"{workspace.Root}\" --input \"{input}\" --target python --output \"{output}\" --extension-id {Id} --json",
            environment: HomeOf(home));
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        report.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        Directory.GetFiles(output, "*.py", SearchOption.AllDirectories).Should().NotBeEmpty();
    }

    [Test]
    public async Task InstalledClaimsThatDifferFromTheSessionAreRefused()
    {
        var (workspace, home) = await InstalledAsync(name: "Another Python");
        using var _ = workspace;
        workspace.Write("identity.py", "def identity(value: int) -> int:\n    return value\n");
        var result = await CliTestHelper.ForTarget().ExecuteCommandAsync(
            $"compile --project \"{workspace.Root}\" --language python --input \"{workspace.PathOf("identity.py")}\" --ir-version 4 --extension-id {Id} --json",
            environment: HomeOf(home));
        result.ExitCode.Should().NotBe(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("extension.claims_mismatch");
    }

    [Test]
    public async Task AChangedStoredModuleFailsBeforeTheGuestRuns()
    {
        var (workspace, home) = await InstalledAsync();
        using var _ = workspace;
        var stored = Path.Combine(home, "store", "extensions", "sha256", PublishedGuest.Sha256, PublishedGuest.FileName);
        File.SetAttributes(stored, FileAttributes.Normal);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stored, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await File.AppendAllTextAsync(stored, "changed");
        workspace.Write("identity.py", "def identity(value: int) -> int:\n    return value\n");
        var result = await CliTestHelper.ForTarget().ExecuteCommandAsync(
            $"compile --project \"{workspace.Root}\" --language python --input \"{workspace.PathOf("identity.py")}\" --ir-version 4 --extension-id {Id} --json",
            environment: HomeOf(home));
        result.ExitCode.Should().NotBe(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("extension.digest_mismatch");
    }

    [Test]
    public async Task AHostWithoutAWasmRuntimeRefusesAnInstalledWasmExtension()
    {
        var (workspace, home) = await InstalledAsync();
        using var _ = workspace;
        var installed = Morphir.Extensions.ExtensionId.Create(Id);
        var extension = new Setting<string>(Id, new SettingOrigin.CommandLine("--extension-id"));
        var environment = new Dictionary<string, string> { ["MORPHIR_HOME"] = home };
        // The parameterless connectors have no WASM module loader, as a host without a WASM runtime.
        var frontend = await new MepFrontendExtensionConnector().ConnectAsync(new(extension, null, workspace.Root)
        {
            InstalledId = installed,
            Environment = environment,
        }, default);
        frontend.Diagnostics.Select(diagnostic => diagnostic.CodeOrNull?.Value).Should().Equal("extension.runtime_unavailable");
        var backend = await new MepBackendExtensionConnector().ConnectAsync(new(extension, Morphir.Extensions.TargetId.Create("python"), workspace.Root)
        {
            InstalledId = installed,
            Environment = environment,
        }, default);
        backend.Should().BeOfType<Outcome<IExternalGenerationBackend>.Failure>().Subject.Error.Diagnostics
            .Select(diagnostic => diagnostic.Code).Should().Equal("extension.runtime_unavailable");
    }
}
