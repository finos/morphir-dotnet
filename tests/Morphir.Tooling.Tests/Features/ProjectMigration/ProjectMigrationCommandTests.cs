// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Features.ProjectMigration;

using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Features.ProjectMigration;

[Category("ProjectMigration")]
public sealed class ProjectMigrationCommandTests
{
    private sealed class ObservingHost : IProjectMigrationHost
    {
        public int Calls { get; private set; }
        public ProjectMigrationInvocation? Invocation { get; private set; }
        public bool Fail { get; init; }
        public ProjectMigrationReport Execute(ProjectMigrationInvocation invocation)
        {
            Calls++;
            Invocation = invocation;
            if (Fail) throw new IOException("never-print-host-details");
            return ProjectMigrationReport.Failure("test.blocked", "Controlled test host result.");
        }
    }

    [Test]
    public void RegistrationAndHelpNeverInvokeMigration()
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        new ProjectCommandProvider(host).RegisterCommands(root, new ServiceCollection());
        host.Calls.Should().Be(0);
        root.Parse(["project", "migrate", "--help"]).Invoke().Should().Be(0);
        host.Calls.Should().Be(0);
        root.Subcommands.Should().ContainSingle(command => command.Name == "project");
        root.Subcommands.Should().NotContain(command => command.Name == "migrate");
    }

    [Test]
    [Arguments("--output", "")]
    [Arguments("--discovery-boundary", "")]
    [Arguments("")]
    public void EmptyControlsFailBeforeHostAccess(params string[] controls)
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        root.Subcommands.Add(new ProjectMigrationCommand(host));
        root.Parse(["migrate", "--json", .. controls]).Invoke().Should().Be(2);
        host.Calls.Should().Be(0);
    }

    [Test]
    public void ArgumentsBindWithoutReadingAmbientConfiguration()
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        root.Subcommands.Add(new ProjectMigrationCommand(host));
        // The boundary must contain the current directory (discovery-containment), and reaches the host resolved.
        var boundary = Path.GetFullPath(Environment.CurrentDirectory);
        root.Parse(["migrate", "orders/morphir.json", "--output", "orders/.config/morphir/config.toml", "--dry-run", "--json", "--discovery-boundary", boundary]).Invoke().Should().Be(2);
        host.Calls.Should().Be(1);
        host.Invocation.Should().Be(new ProjectMigrationInvocation("orders/morphir.json", "orders/.config/morphir/config.toml",
            Path.GetFullPath(Containment.Resolve(boundary, true, new Dictionary<string, string>(), Environment.CurrentDirectory).Value!.Boundary), true));
    }

    [Test]
    public void BoundaryThatExcludesTheCurrentDirectoryFailsBeforeHostAccess()
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        root.Subcommands.Add(new ProjectMigrationCommand(host));
        var elsewhere = Path.Combine(Path.GetTempPath(), "morphir-elsewhere-" + Guid.NewGuid().ToString("N"));
        root.Parse(["migrate", "--dry-run", "--json", "--discovery-boundary", elsewhere]).Invoke().Should().Be(2);
        host.Calls.Should().Be(0);
    }

    [Test]
    public void InitializationFailureNeverInvokesTheProjectHost()
    {
        var host = new ObservingHost();
        var root = new RootCommand();
        var provider = new ProjectCommandProvider(host);
        ((IMorphirHostInitializationErrorHandler)provider).HandleHostInitializationFailure();
        provider.RegisterCommands(root, new ServiceCollection());
        root.Parse(["project", "migrate", "--json"]).Invoke().Should().Be(2);
        host.Calls.Should().Be(0);
    }

    [Test]
    public void HostIoFailureReturnsAControlledResult()
    {
        var host = new ObservingHost { Fail = true };
        var root = new RootCommand();
        root.Subcommands.Add(new ProjectMigrationCommand(host));
        root.Parse(["migrate", "--json"]).Invoke().Should().Be(2);
        host.Calls.Should().Be(1);
    }
}
