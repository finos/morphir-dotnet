// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;

namespace Morphir.TestSupport.Tests;

public class MachineTripwireTests
{
    [Test]
    public async Task UnchangedRoot_Passes()
    {
        using var sandbox = TestSandbox.Create();
        var root = Directory.CreateDirectory(sandbox.PathOf("fake-morphir-home")).FullName;
        await File.WriteAllTextAsync(Path.Combine(root, "existing.toml"), "x = 1");

        var tripwire = MachineTripwire.Capture([root]);

        tripwire.Changes().Should().BeEmpty();
        tripwire.Invoking(t => t.AssertUnchanged()).Should().NotThrow();
    }

    [Test]
    public async Task FileWrittenDuringRun_FailsAndNamesThePath()
    {
        using var sandbox = TestSandbox.Create();
        var root = Directory.CreateDirectory(sandbox.PathOf("fake-morphir-home")).FullName;
        var tripwire = MachineTripwire.Capture([root]);

        var leaked = Path.Combine(root, "dist", "active");
        Directory.CreateDirectory(Path.GetDirectoryName(leaked)!);
        await File.WriteAllTextAsync(leaked, "1.0.0");

        tripwire.Changes().Should().Contain(change => change.Contains(leaked));
        tripwire.Invoking(t => t.AssertUnchanged())
            .Should().Throw<InvalidOperationException>().WithMessage($"*{leaked}*");
    }

    [Test]
    public async Task FileModifiedDuringRun_IsReported()
    {
        using var sandbox = TestSandbox.Create();
        var root = Directory.CreateDirectory(sandbox.PathOf("fake-morphir-home")).FullName;
        var file = Path.Combine(root, "morphir.toml");
        await File.WriteAllTextAsync(file, "a = 1");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));
        var tripwire = MachineTripwire.Capture([root]);

        await File.WriteAllTextAsync(file, "a = 22");

        tripwire.Changes().Should().ContainSingle().Which.Should().Contain(file);
    }

    [Test]
    public async Task RootCreatedDuringRun_IsReported()
    {
        using var sandbox = TestSandbox.Create();
        var root = sandbox.PathOf("absent-morphir-home");
        var tripwire = MachineTripwire.Capture([root]);

        Directory.CreateDirectory(root);

        tripwire.Changes().Should().ContainSingle().Which.Should().Contain(root);
        await Task.CompletedTask;
    }

    [Test]
    public async Task DefaultRoots_CoverTheMachineMorphirLocations()
    {
        var roots = MachineTripwire.DefaultRoots();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        roots.Should().Contain(Path.Combine(home, ".morphir"));
        roots.Should().OnlyHaveUniqueItems();
        if (OperatingSystem.IsWindows())
        {
            roots.Should().Contain(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Morphir"));
            roots.Should().Contain(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Morphir"));
        }

        await Task.CompletedTask;
    }
}
