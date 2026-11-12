// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using FluentAssertions;
using Morphir.Configuration;
using Morphir.Tooling.Features.Configuration;

namespace Morphir.Tooling.Tests.Features.Configuration;

public sealed class ConfigurationEditingCommandTests
{
    private sealed class ObservingHost : IConfigurationEditingHost
    {
        public int Calls { get; private set; }
        public ConfigurationInvocation? Invocation { get; private set; }
        public ConfigurationEditReport Execute(ConfigurationInvocation invocation, EditRequest request, bool dryRun)
        {
            Calls++; Invocation = invocation;
            return new(request.Operation.IsUnset ? "unset" : "set", "unchanged", null, request.Key, null, null, false, dryRun, null, [], [], 0, []);
        }
    }

    [Test]
    [Arguments("set")]
    [Arguments("unset")]
    public void HelpDoesNotInvokeTheEditingHost(string operation)
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        root.Subcommands.Add(new ConfigurationEditingCommand(operation, host));
        root.Parse([operation, "--help"]).Invoke().Should().Be(0);
        host.Calls.Should().Be(0);
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments("--type", "unsupported")]
    [Arguments("--type", "bool")]
    [Arguments("--type", "array")]
    [Arguments("--profiles", "INVALID")]
    [Arguments("--profiles", "ci", "--no-profiles")]
    [Arguments("--workspace", "--user")]
    [Arguments("--file", "settings.yaml")]
    [Arguments("--file", "a.toml", "--config", "b.toml")]
    [Arguments("--project", "")]
    [Arguments("--file", "")]
    [Arguments("--config", "")]
    [Arguments("--discovery-boundary", "")]
    public void InvalidControlsFailBeforeTheEditingHost(params string[] controls)
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        root.Subcommands.Add(new ConfigurationEditingCommand("set", host));
        root.Parse(["set", "custom.value", "value", "--json", .. controls]).Invoke().Should().Be(2);
        host.Calls.Should().Be(0);
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments("--workspace")]
    [Arguments("--user")]
    [Arguments("--global")]
    [Arguments("--system")]
    public void ScopeBindingReachesOnlyTheSharedHost(string selector)
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        root.Subcommands.Add(new ConfigurationEditingCommand("set", host));
        root.Parse(["set", "custom.value", "value", selector, "--json", "--no-profiles"]).Invoke().Should().Be(0);
        host.Calls.Should().Be(1);
        var scope = host.Invocation!.Scope;
        (selector switch { "--workspace" => scope.IsWorkspace, "--user" => scope.IsUser, "--global" => scope.IsGlobal, _ => scope.IsSystem }).Should().BeTrue();
    }
}
