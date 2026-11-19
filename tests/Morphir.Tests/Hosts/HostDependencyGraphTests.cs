// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;

namespace Morphir.Tests.Hosts;

/// <summary>
/// DEP-1: the AOT-compatible <c>morphir</c> and the shared command layer never depend on the F# compiler service. The
/// closure tests read the references that the compiler emitted, so a project reference with no used type would slip
/// past them. These tests read the resolved dependency graph instead: the committed <c>packages.lock.json</c> and,
/// when the project has been restored, <c>obj/project.assets.json</c>.
/// </summary>
[Category("PlatformSpecific")]
public sealed class HostDependencyGraphTests
{
    private static readonly string[] ManagedOnly = ["FSharp.Compiler.Service", "Morphir.Frontend.FSharp", "Morphir.Tooling.Managed"];

    [Test]
    [Arguments("src/Morphir")]
    [Arguments("src/Morphir.Tooling")]
    public void TheResolvedGraphHasNoManagedOnlyDependency(string project)
    {
        foreach (var (source, names) in Graphs(project))
            names.Should().NotContain(name => ManagedOnly.Contains(name, StringComparer.OrdinalIgnoreCase),
                $"{source} must not resolve the F# compiler service or the managed composition");
    }

    [Test]
    public void TheReaderFindsTheManagedDependenciesOfTheTool()
    {
        // A positive control: the same reader sees the managed-only dependencies where they belong.
        foreach (var (source, names) in Graphs("src/Morphir.Tool"))
            foreach (var name in ManagedOnly)
                names.Should().Contain(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase), $"{source} resolves {name}");
    }

    /// <summary>The package and project names that a project's lock file and assets file resolve, for every target.</summary>
    private static IEnumerable<(string Source, HashSet<string> Names)> Graphs(string project)
    {
        var directory = Path.Combine(RepositoryRoot(), project);
        var lockFile = Path.Combine(directory, "packages.lock.json");
        File.Exists(lockFile).Should().BeTrue($"{lockFile} is committed");
        yield return (lockFile, LockNames(lockFile));

        var assets = Path.Combine(directory, "obj", "project.assets.json");
        if (File.Exists(assets)) yield return (assets, AssetNames(assets));
    }

    private static HashSet<string> LockNames(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var entry in target.Value.EnumerateObject())
            {
                names.Add(entry.Name);
                if (entry.Value.TryGetProperty("dependencies", out var dependencies))
                    foreach (var dependency in dependencies.EnumerateObject()) names.Add(dependency.Name);
            }
        }
        names.Should().NotBeEmpty($"{path} lists dependencies");
        return names;
    }

    private static HashSet<string> AssetNames(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
            names.Add(library.Name.Split('/')[0]);
        foreach (var target in document.RootElement.GetProperty("targets").EnumerateObject())
            foreach (var library in target.Value.EnumerateObject())
                names.Add(library.Name.Split('/')[0]);
        names.Should().NotBeEmpty($"{path} lists libraries");
        return names;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Morphir.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Morphir.slnx not found above the test output folder.");
    }
}
