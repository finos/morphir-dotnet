// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Reflection;

namespace Morphir.TestSupport.Drivers;

/// <summary>A program of the repository that a test runs, publishes or inspects: a library host, a probe or a product program.</summary>
/// <param name="Name">The name of the program in the item <c>MorphirTestProgram</c> of the test project.</param>
/// <param name="Assembly">The built assembly of the program, in the configuration of the test assembly.</param>
/// <param name="Project">The project file, for a publish.</param>
public sealed record TestProgram(string Name, string Assembly, string Project)
{
    /// <summary>The build output folder of the program.</summary>
    public string OutputDirectory => Path.GetDirectoryName(Assembly)!;
}

/// <summary>
/// The programs that the test assembly names (Morphir.TestPrograms.targets), resolved once before the first test.
/// A program with no built file is an error with the command that builds it.
/// </summary>
public sealed class TestPrograms
{
    private const string MetadataPrefix = "Morphir.TestSupport.Program.";
    private const string ProjectSuffix = ".Project";

    private readonly ImmutableSortedDictionary<string, TestProgram> _programs;

    private TestPrograms(ImmutableSortedDictionary<string, TestProgram> programs) => _programs = programs;

    /// <summary>The names of the programs, in ordinal order.</summary>
    public IReadOnlyList<string> Names => [.. _programs.Keys];

    /// <summary>No program, for a run on a target that has no build output of the repository.</summary>
    public static TestPrograms None { get; } = new(ImmutableSortedDictionary<string, TestProgram>.Empty.WithComparers(StringComparer.Ordinal));

    public TestProgram Of(string name) =>
        _programs.TryGetValue(name, out var program)
            ? program
            : throw new TestSessionException(
                $"The test assembly names no program '{name}'. It names: {(_programs.Count == 0 ? "none" : string.Join(", ", _programs.Keys))}. Add an item MorphirTestProgram with Name=\"{name}\" to its project.");

    public static TestPrograms OfTestAssembly(Assembly testAssembly) =>
        Of(
            testAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(attribute => attribute.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal) && attribute.Value is not null)
                .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!),
            testAssembly.GetName().Name ?? "the test assembly",
            File.Exists);

    /// <summary>The programs from the assembly metadata that Morphir.TestPrograms.targets writes.</summary>
    public static TestPrograms Of(IReadOnlyDictionary<string, string> metadata, string testAssembly, Func<string, bool> fileExists)
    {
        var programs = ImmutableSortedDictionary.CreateBuilder<string, TestProgram>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var (key, assembly) in metadata)
        {
            if (!key.StartsWith(MetadataPrefix, StringComparison.Ordinal) || key.EndsWith(ProjectSuffix, StringComparison.Ordinal)) continue;
            var name = key[MetadataPrefix.Length..];
            if (!metadata.TryGetValue(key + ProjectSuffix, out var project))
                throw new TestSessionException($"{testAssembly} has the path of the program '{name}' and not its project. Import Morphir.TestPrograms.targets.");

            if (!fileExists(assembly))
            {
                var configuration = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(assembly))) ?? "Release";
                missing.Add($"  the program {name}, at {assembly}. Build it: dotnet build {project} -c {configuration}");
            }

            programs[name] = new TestProgram(name, assembly, project);
        }

        if (missing.Count > 0)
            throw new TestSessionException("The development build has no file for:" + Environment.NewLine + string.Join(Environment.NewLine, missing));

        return new TestPrograms(programs.ToImmutable());
    }
}
