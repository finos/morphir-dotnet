// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Configuration;
using Morphir.Tooling.Features.Configuration;

namespace Morphir.Tooling.Tests.Features.Configuration;

public sealed class ConfigurationCommandTests
{
    private sealed class ObservingHost : IConfigurationInspectionHost
    {
        public int Loads { get; private set; }
        public ResolutionDetails Load(ConfigurationInvocation invocation)
        {
            Loads++;
            throw new InvalidOperationException("Configuration must not load");
        }
    }

    [Test]
    [Arguments("get")]
    [Arguments("list")]
    [Arguments("show")]
    [Arguments("path")]
    [Arguments("explain")]
    public void RegistrationAndHelpNeverInvokeLoading(string operation)
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        new ConfigurationCommandProvider(host).RegisterCommands(root, new ServiceCollection());
        host.Loads.Should().Be(0);
        root.Parse(["config", operation, "--help"]).Invoke().Should().Be(0);
        host.Loads.Should().Be(0);
    }

    [Test]
    [Arguments("--profiles", "INVALID")]
    [Arguments("--profiles", "good,,bad")]
    [Arguments("--profiles", "ci", "--no-profiles")]
    [Arguments("--workspace", "--user")]
    [Arguments("--project", "")]
    [Arguments("--file", "")]
    [Arguments("--config", "")]
    [Arguments("--discovery-boundary", "")]
    public void InvalidControlsFailBeforeAnyDiscovery(params string[] controls)
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        new ConfigurationCommandProvider(host).RegisterCommands(root, new ServiceCollection());
        root.Parse(["config", "get", "custom.key", .. controls]).Invoke().Should().Be(2);
        host.Loads.Should().Be(0);
    }
    [Test]
    [Arguments("custom..key")]
    [Arguments("custom.\"unterminated")]
    [Arguments("custom.items[0]")]
    public void InvalidKeyPathsFailBeforeAnyDiscovery(string key)
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        new ConfigurationCommandProvider(host).RegisterCommands(root, new ServiceCollection());
        root.Parse(["config", "get", key]).Invoke().Should().Be(2);
        host.Loads.Should().Be(0);
    }
}
