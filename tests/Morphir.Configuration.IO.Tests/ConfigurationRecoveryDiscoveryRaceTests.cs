// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

[Category("PlatformSpecific")]
public sealed class ConfigurationRecoveryDiscoveryRaceTests
{
    [Test]
    public void RecoveryDiscoveryNeverEnumeratesLinkedAncestors()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = ProjectPath.join(Path.GetTempPath(), "recovery-discovery-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root + "/physical");
        var alias = root + "/alias";
        Directory.CreateSymbolicLink(alias, root + "/physical");
        var journal = ".morphir-edit-" + new string('a', 32) + "-fixture-journal";
        File.WriteAllText(root + "/physical/" + journal, "must not inspect through link");
        try
        {
            new NativeConfigurationTransactionHost().FindRecoveryJournals([alias + "/morphir.toml"])
                .Should().NotContain(p => p.StartsWith(alias + "/", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task UnrelatedAncestorEntriesCanChangeDuringRecoveryDiscovery()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "recovery-discovery-race-" + Guid.NewGuid().ToString("N"));
        var workspace = root + "/workspace";
        Directory.CreateDirectory(workspace);
        var target = workspace + "/morphir.toml";
        await File.WriteAllTextAsync(target, "[workspace]\nmembers=[]\n");
        using var cancel = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var churn = Task.Run(() =>
        {
            started.Set();
            while (!cancel.IsCancellationRequested)
                for (var i = 0; i < 8; i++)
                {
                    var path = root + "/unrelated-" + i;
                    File.WriteAllText(path, "unrelated");
                    Thread.Yield();
                    File.Delete(path);
                }
        });
        try
        {
            started.Wait();
            var host = new NativeConfigurationTransactionHost();
            for (var i = 0; i < 300; i++)
                _ = host.FindRecoveryJournals([target]);
            (await File.ReadAllTextAsync(target)).Should().Be("[workspace]\nmembers=[]\n");
        }
        finally
        {
            cancel.Cancel();
            await churn;
            Directory.Delete(root, true);
        }
    }
}
