// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Compile;

public sealed class InstalledProviderCommandTests
{
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
    }}
