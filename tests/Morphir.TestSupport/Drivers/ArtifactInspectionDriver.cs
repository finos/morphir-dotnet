// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>The references in the metadata of a built assembly.</summary>
public sealed class AssemblyFacts
{
    private readonly ImmutableDictionary<(string Namespace, string Type), ImmutableHashSet<string>> _members;

    internal AssemblyFacts(
        ImmutableHashSet<string> assemblies, ImmutableHashSet<string> namespaces,
        ImmutableDictionary<(string Namespace, string Type), ImmutableHashSet<string>> members)
    {
        AssemblyReferences = assemblies;
        TypeReferenceNamespaces = namespaces;
        _members = members;
    }

    /// <summary>The names of the assemblies that the assembly references.</summary>
    public IReadOnlySet<string> AssemblyReferences { get; }

    /// <summary>The namespaces of the types that the assembly references.</summary>
    public IReadOnlySet<string> TypeReferenceNamespaces { get; }

    /// <summary>The names of the members of one referenced type that the assembly references.</summary>
    public IReadOnlySet<string> MembersOf(string @namespace, string type) =>
        _members.TryGetValue((@namespace, type), out var members) ? members : ImmutableHashSet<string>.Empty;

    /// <summary>True when the assembly references a type of <paramref name="namespace"/> or of a namespace below it.</summary>
    public bool UsesNamespace(string @namespace) =>
        TypeReferenceNamespaces.Any(name => name == @namespace || name.StartsWith(@namespace + ".", StringComparison.Ordinal));

    internal static AssemblyFacts Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var assemblies = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToImmutableHashSet();
        var namespaces = metadata.TypeReferences.Select(handle => metadata.GetString(metadata.GetTypeReference(handle).Namespace)).ToImmutableHashSet();
        var members = new Dictionary<(string, string), HashSet<string>>();
        foreach (var handle in metadata.MemberReferences)
        {
            var reference = metadata.GetMemberReference(handle);
            if (reference.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)reference.Parent);
            var key = (metadata.GetString(type.Namespace), metadata.GetString(type.Name));
            if (!members.TryGetValue(key, out var names)) members[key] = names = [];
            names.Add(metadata.GetString(reference.Name));
        }

        return new AssemblyFacts(assemblies, namespaces, members.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToImmutableHashSet()));
    }
}

/// <summary>A file of the build output of a program.</summary>
public sealed class BuiltFile
{
    private readonly string _program;

    internal BuiltFile(string program, string path)
    {
        _program = program;
        Path = path;
    }

    /// <summary>The full path, for a message.</summary>
    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public bool Exists => File.Exists(Path);

    /// <summary>The references in the metadata of the file, which is an assembly.</summary>
    public AssemblyFacts ReadAssembly() => AssemblyFacts.Read(Existing());

    /// <summary>The file as one JSON document.</summary>
    public JsonElement ReadJson()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Existing()));
        return document.RootElement.Clone();
    }

    private string Existing() =>
        File.Exists(Path) ? Path : throw new TestSessionException($"The build output of the program {_program} has no file {Name} ({Path}).");
}

/// <summary>
/// Gives a built file of a program of the selected target and its metadata, so a test checks a property of the
/// delivered form with no path of the machine.
/// </summary>
public interface IArtifactInspectionDriver
{
    /// <summary>The file <paramref name="relativePath"/> below the build output folder of <paramref name="program"/>.</summary>
    BuiltFile BuiltFile(string program, string relativePath);
}

/// <summary>The inspection driver on the build output of the programs that the test assembly names.</summary>
public sealed class ArtifactInspectionDriver(TestPrograms programs) : IArtifactInspectionDriver
{
    public BuiltFile BuiltFile(string program, string relativePath)
    {
        var folder = programs.Of(program).OutputDirectory;
        return new BuiltFile(program, ScenarioWorkspace.Below(folder, [relativePath]));
    }
}
