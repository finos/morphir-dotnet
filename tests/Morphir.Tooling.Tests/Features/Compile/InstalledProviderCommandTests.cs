// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Tests.Features.Compile;

[Category("PlatformSpecific")]
public sealed class InstalledProviderCommandTests
{
    [Test]
    [Arguments("org.finos.python", true)]
    [Arguments("", false)]
    [Arguments("invalid/id", false)]
    public void ExplicitInstalledCompileIdentityBypassesMalformedConfiguredModule(string rawId, bool valid)
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/demo\"\n[extensions.python]\npath = \"\"\n");
        var result = new ProjectCompileSettingsResolver().Resolve(new CompileOverrides
        {
            Project = workspace.Root,
            Language = "python",
            Inputs = ["input.py"],
            ExtensionId = rawId,
        }, workspace.Context());
        if (valid)
        {
            result.TryGetValue(out var settings).Should().BeTrue(string.Join("; ", result.Diagnostics));
            settings!.InstalledExtensionId!.Value.Value.Should().Be(rawId);
            settings.Extension.Should().BeNull();
            settings.WasmModule.Should().BeNull();
        }
        else
        {
            result.TryGetValue(out _).Should().BeFalse();
            result.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message)
                .Should().ContainSingle().Which.Should().Contain("--extension-id").And.NotContain("extensions.python");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitInstalledGenerateIdentityBypassesConfiguredModuleAndExplicitInputConfiguration(bool brokenDocument)
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", brokenDocument ? "[broken" :
            "[project]\nname = \"acme/demo\"\n[extensions.python]\npath = \"\"\n");
        var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(), new FileSystemCompileConfigurationLoader());
        var result = await resolver.ResolveAsync(new("input.json", "python", workspace.PathOf("generated"))
        {
            Project = workspace.Root,
            ExtensionId = "org.finos.python",
        }, new InvocationContext(workspace.Root) { Environment = workspace.Context().Environment }, default);
        var settings = result.Should().BeOfType<Outcome<GenerationSettings>.Success>().Subject.Value;
        settings.InstalledExtensionId!.Value.Should().Be("org.finos.python");
        settings.Extension.Should().BeNull();
        settings.WasmModule.Should().BeNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ExplicitLegacyExtensionStillResolvesAConfiguredAlias(bool compile)
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/demo\"\n[extensions.python]\npath = \"python.wasm\"\n");
        if (compile)
        {
            var result = new ProjectCompileSettingsResolver().Resolve(new CompileOverrides
            {
                Project = workspace.Root,
                Language = "python",
                Inputs = ["input.py"],
                Extension = "python",
            }, workspace.Context());
            result.TryGetValue(out var settings).Should().BeTrue(string.Join("; ", result.Diagnostics));
            settings!.WasmModule.Should().NotBeNull();
            settings.WasmModule!.Path.Should().Be(workspace.PathOf("python.wasm"));
            settings.InstalledExtensionId.Should().BeNull();
        }
        else
        {
            var resolver = new RecordGenerationSettingsResolver(new CliGenerationSettingsResolver(), new FileSystemCompileConfigurationLoader());
            var result = await resolver.ResolveAsync(new("input.json", "python", workspace.PathOf("generated"))
            {
                Project = workspace.Root,
                Extension = "python",
            }, new InvocationContext(workspace.Root) { Environment = workspace.Context().Environment }, default);
            var settings = result.Should().BeOfType<Outcome<GenerationSettings>.Success>().Subject.Value;
            settings.WasmModule!.Path.Should().Be(workspace.PathOf("python.wasm"));
            settings.InstalledExtensionId.Should().BeNull();
        }
    }

    [Test]
    public async Task InstalledConnectorRefusalIsTerminalBeforeConfiguredModuleOrProcessFallback()
    {
        using var workspace = new CompileWorkspace();
        var installed = Morphir.Extensions.ExtensionId.Create("org.finos.python");
        var extension = new Setting<string>("python", new SettingOrigin.CommandLine("--extension-id"));
        var module = new WasmModuleSource("python", workspace.PathOf("must-not-open.wasm"));
        var environment = new Dictionary<string, string>();
        var frontend = await new MepFrontendExtensionConnector().ConnectAsync(new(extension, null, workspace.Root)
        {
            InstalledId = installed,
            Environment = environment,
            WasmModule = module,
        }, default);
        frontend.Diagnostics.Select(diagnostic => diagnostic.CodeOrNull?.Value).Should().Equal("extension.home_required");
        var backend = await new MepBackendExtensionConnector().ConnectAsync(new(extension, Morphir.Extensions.TargetId.Create("python"), workspace.Root)
        {
            InstalledId = installed,
            Environment = environment,
            WasmModule = module,
        }, default);
        backend.Should().BeOfType<Outcome<IExternalGenerationBackend>.Failure>().Subject.Error.Diagnostics
            .Select(diagnostic => diagnostic.Code).Should().Equal("extension.home_required");
    }

    [Test]
    public void CompileHasAnExplicitInstalledIdentityDistinctFromAPath()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        new CompileCommand(services).Options.Select(option => option.Name).Should().Contain("--extension-id");
    }

    [Test]
    public void GenerateHasAnExplicitInstalledIdentityDistinctFromAPath()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        new GenerateCommand(services).Options.Select(option => option.Name).Should().Contain("--extension-id");
    }
    [Test]
    public async Task RepositoryRegistrationDryRunCreatesNoHomeOrLocks()
    {
        using var sandbox = Morphir.TestSupport.TestSandbox.Create();
        var source = sandbox.PathOf("repository");
        Directory.CreateDirectory(source);
        await using var app = MorphirCli.CreateBuilder(["extension","repository","add","local-dev","--directory",source,
            "--home",sandbox.MorphirHome,"--local-developer","--dry-run"])
            .AddScenario<Morphir.Tooling.Scenarios.ManagementScenario>().Build();
        (await app.RunAsync()).Should().Be(0);
        Directory.Exists(sandbox.MorphirHome).Should().BeFalse("a planning command cannot acquire a persistent lock");
    }
}
