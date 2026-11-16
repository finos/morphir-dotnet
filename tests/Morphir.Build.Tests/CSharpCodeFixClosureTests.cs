// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// The C# code fixes need the Workspaces layer of Roslyn, which does not run under Native AOT, so only an IDE loads
/// Morphir.Lang.CSharp.CodeFixes (design.html, PKG-1). The committed packages.lock.json of each host and of the libraries
/// that the hosts use is the restored closure of that project. This test reads it: no host has the code fix assembly or
/// a Workspaces package.
/// </summary>
public class CSharpCodeFixClosureTests
{
    private const string CodeFixes = "morphir.lang.csharp.codefixes";

    /// <summary>The hosts (the Native AOT morphir, the managed morphir, dotnet-morphir) and the C# libraries in them.</summary>
    public static IEnumerable<string> Projects() =>
    [
        "src/Morphir",
        "src/Morphir.Managed",
        "src/Morphir.Tool",
        "src/Morphir.Tooling",
        "src/Morphir.Lang.CSharp",
        "src/Morphir.Lang.CSharp.Analyzers",
    ];

    [Test]
    [MethodDataSource(nameof(Projects))]
    public void TheRestoredClosureHasNoCodeFixAndNoWorkspacesAssembly(string project)
    {
        var names = DependencyNames(project);

        names.Should().Contain("microsoft.codeanalysis.csharp", "the lock file has the Roslyn compiler, so the closure is read");
        names.Should().NotContain(CodeFixes, $"{project} must not load the code fixes");
        names.Where(name => name.Contains("workspaces")).Should().BeEmpty($"{project} must not load the Workspaces layer");
    }

    [Test]
    public void TheCodeFixProjectHasTheWorkspacesLayer()
    {
        // The check above has a meaning only when the lock file of the code fix project names Workspaces.
        DependencyNames("src/Morphir.Lang.CSharp.CodeFixes").Should().Contain("microsoft.codeanalysis.csharp.workspaces");
    }

    /// <summary>The lowercase names of the packages and projects of each target framework of a lock file.</summary>
    private static HashSet<string> DependencyNames(string project)
    {
        var path = Path.Combine(TestFixture.GetRepositoryRoot(), project, "packages.lock.json");
        using var lockFile = JsonDocument.Parse(File.ReadAllText(path));
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var framework in lockFile.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var dependency in framework.Value.EnumerateObject())
            {
                names.Add(dependency.Name.ToLowerInvariant());
            }
        }

        return names;
    }
}
