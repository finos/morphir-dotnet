// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.TestSupport;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>Explicit-input JIT product journey. Standalone morphirup and generation authority require their own evidence.</summary>
public sealed class InstalledProviderJourneyTests
{
    [Test]
    public async Task RegisteredInstalledProcessProvidersCompileGenerateAndRepeatWithWithdrawnSources()
    {
        var host = Environment.GetEnvironmentVariable("MORPHIR_INSTALLED_PROVIDER_HOST_DLL");
        var runtime = Environment.GetEnvironmentVariable("MORPHIR_INSTALLED_PROVIDER_DOTNET");
        var inputs = Environment.GetEnvironmentVariable("MORPHIR_INSTALLED_PROVIDER_INPUTS");
        var fixedCase = Environment.GetEnvironmentVariable("MORPHIR_INSTALLED_PROVIDER_CASE");
        var evidence = Environment.GetEnvironmentVariable("MORPHIR_INSTALLED_PROVIDER_EVIDENCE");
        if (host is null || runtime is null || inputs is null || fixedCase is null || evidence is null)
            Skip.Test("Explicit fresh product DLL, verified runtime, actual provider inputs, frozen case and evidence directory are required; no build/download fallback.");
        using var sandbox = TestSandbox.Create("installed-provider-journey-");
        Directory.CreateDirectory(evidence!);
        var sequence = 0;
        async Task<ChildProcessResult> Run(params string[] arguments)
        {
            var vector = new[] { host! }.Concat(arguments).ToArray();
            var start = ChildProcess.StartInfo(runtime!, vector);
            sandbox.Apply(start);
            start.WorkingDirectory = sandbox.Work;
            start.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(runtime!)!;
            start.Environment["DOTNET_ROOT_X64"] = Path.GetDirectoryName(runtime!)!;
            start.Environment["DOTNET_HOST_PATH"] = runtime!;
            start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            start.Environment["PATH"] = sandbox.PathOf("empty-path");
            var label = $"{++sequence:D2}-{arguments[0]}";
            File.WriteAllText(Path.Combine(evidence!, label + ".argv.json"), JsonSerializer.Serialize(vector));
            var result = await ChildProcess.RunAsync(start, TimeSpan.FromSeconds(90));
            File.WriteAllText(Path.Combine(evidence!, label + ".stdout.log"), result.StandardOutput);
            File.WriteAllText(Path.Combine(evidence!, label + ".stderr.log"), result.StandardError);
            result.ExitCode.Should().Be(0, result.CombinedOutput);
            return result;
        }
        string[] State() => Directory.Exists(sandbox.MorphirHome)
            ? Directory.GetFiles(sandbox.MorphirHome, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(path => Path.GetRelativePath(sandbox.MorphirHome, path) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))).ToArray()
            : [];
        foreach (var role in new[] { "frontend", "backend" })
        {
            var repository = sandbox.PathOf("source-" + role);
            Directory.CreateDirectory(repository);
            var archive = $"morphir-fsharp-{role}-0.4.0-alpha.4-win-x64.zip";
            File.Copy(Path.Combine(inputs!, "providers", archive), Path.Combine(repository, archive));
            File.Copy(Path.Combine(inputs!, "prepared", role, "release.json"), Path.Combine(repository, "release.json"));
            File.Copy(Path.Combine(inputs!, "prepared", role, "manifest"), Path.Combine(repository, "manifest"));
            await Run("extension", "repository", "add", "local-" + role, "--directory", repository, "--home", sandbox.MorphirHome, "--local-developer");
            var install = new[] { "extension", "install", "org.finos.morphir.fsharp-" + role, "--repository", "local-" + role, "--local-release", Path.Combine(repository, "release.json"), "--local-archive", Path.Combine(repository, archive), "--home", sandbox.MorphirHome, "--local-developer", "--no-probe" };
            var before = State();
            await Run(install.Append("--dry-run").ToArray());
            State().Should().Equal(before, "install dry-run cannot change persistent bytes or create a lock");
            await Run(install);
        }
        var source = Path.Combine(fixedCase!, "source", "Pricing.fs");
        if (!File.Exists(source)) source = Path.Combine(fixedCase!, "input", "Pricing.fs");
        File.Exists(source).Should().BeTrue("the frozen source is supplied, never synthesized");
        var pricing = sandbox.PathOf("Pricing.fs");
        File.Copy(source, pricing);
        var frozen = Path.Combine(fixedCase!, "expected", "generated", "Pricing.fs");
        var expected = File.ReadAllBytes(frozen);
        expected.Length.Should().Be(112);
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(expected)).Should().Be("64e08313f3e57be2c18995a2e8b275db9d921341958da2c8cd960c601c066de0");
        async Task CompileGenerate(string label)
        {
            var compiled = sandbox.PathOf(label + "-compiled");
            var generated = sandbox.PathOf(label + "-generated");
            var compile = await Run("compile", "--language", "fsharp", "--input", pricing, "--package-name", "acme/demo", "--ir-version", "4", "--output", compiled, "--out-dir", sandbox.PathOf(label + "-out"), "--extension-id", "org.finos.morphir.fsharp-frontend", "--json");
            using var compileReport = JsonDocument.Parse(compile.StandardOutput);
            var frontend = compileReport.RootElement.GetProperty("provider");
            frontend.GetProperty("role").GetString().Should().Be("frontend");
            frontend.GetProperty("extensionId").GetString().Should().Be("org.finos.morphir.fsharp-frontend");
            frontend.GetProperty("modelTuple").GetProperty("operation").GetString().Should().Be("morphir.frontend.compileModel");
            var ir = Path.Combine(compiled, "morphir-ir.json");
            JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllBytes(ir)), JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixedCase!, "expected", "ir-4.0.0.json")))).Should().BeTrue("semantic IR equals the frozen expectation");
            var generate = await Run("generate", "--target", "fsharp", "--input", ir, "--output", generated, "--option", "format=false", "--extension-id", "org.finos.morphir.fsharp-backend", "--json");
            using var generateReport = JsonDocument.Parse(generate.StandardOutput);
            var backend = generateReport.RootElement.GetProperty("provider");
            backend.GetProperty("role").GetString().Should().Be("backend");
            backend.GetProperty("extensionId").GetString().Should().Be("org.finos.morphir.fsharp-backend");
            backend.GetProperty("modelTuple").GetProperty("operation").GetString().Should().Be("morphir.backend.generateModel");
            File.ReadAllBytes(Path.Combine(generated, "Pricing.fs")).Should().Equal(expected);
        }
        await CompileGenerate("online");
        foreach (var role in new[] { "frontend", "backend" })
            Directory.Move(sandbox.PathOf("source-" + role), sandbox.PathOf("withdrawn-" + role));
        var catalogBefore = File.ReadAllBytes(Path.Combine(sandbox.MorphirHome, "catalog", "extensions.json"));
        await CompileGenerate("offline");
        File.ReadAllBytes(Path.Combine(sandbox.MorphirHome, "catalog", "extensions.json")).Should().Equal(catalogBefore);
    }
}
