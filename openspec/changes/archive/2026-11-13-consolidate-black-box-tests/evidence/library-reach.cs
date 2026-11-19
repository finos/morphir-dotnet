#!/usr/bin/env dotnet run
// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// Task 9.1 of consolidate-black-box-tests: which members of a library the CLI reaches, and which members a smoke
// host uses that the CLI does not reach. A member is reached when an assembly of the closure of the CLI (the files in
// the build output of src/Morphir, except the library itself) has a reference to it in its metadata. The count is of
// direct references: a call through a member of the library itself is not a reference of the CLI.
//
// Usage, from the repository root, after ./build.sh --target Compile:
//   dotnet run openspec/changes/consolidate-black-box-tests/evidence/library-reach.cs
#:property RestorePackagesWithLockFile=false
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

var cli = "src/Morphir/bin/Release/net11.0";
(string Library, string Host, string HostAssembly)[] rows =
[
    ("Morphir.Compilation", "tests/Morphir.Compilation.Smoke/bin/Release/net11.0", "Morphir.Compilation.Smoke.dll"),
    ("Morphir.Configuration", "tests/Morphir.Configuration.Inspection.Smoke/bin/Release/net11.0", "Morphir.Configuration.Inspection.Smoke.dll"),
    ("Morphir.Configuration.IO", "tests/Morphir.Configuration.IO.Smoke/bin/Release/net11.0", "Morphir.Configuration.IO.Smoke.dll"),
    ("Morphir.Configuration.Microsoft", "tests/Morphir.Configuration.Microsoft.Smoke/bin/Release/net11.0", "Morphir.Configuration.Microsoft.Smoke.dll"),
    ("Morphir.Conformance", "tests/Morphir.Conformance.Smoke/bin/Release/net11.0", "Morphir.Conformance.Smoke.dll"),
];

foreach (var (library, hostFolder, hostAssembly) in rows)
{
    var closure = Directory.EnumerateFiles(cli, "*.dll")
        .Where(file => Path.GetFileNameWithoutExtension(file) != library && IsManaged(file))
        .ToArray();
    var reached = closure.SelectMany(file => References(file, library)).ToHashSet(StringComparer.Ordinal);
    var inCli = File.Exists(Path.Combine(cli, library + ".dll"));
    var host = References(Path.Combine(hostFolder, hostAssembly), library).ToHashSet(StringComparer.Ordinal);
    var notReached = host.Where(member => !reached.Contains(member)).Order(StringComparer.Ordinal).ToArray();
    Console.WriteLine($"## {library}");
    Console.WriteLine($"in the CLI output: {(inCli ? "yes" : "no")}; members the CLI reaches: {reached.Count}; members the host uses: {host.Count}; members of the host that the CLI does not reach: {notReached.Length}");
    foreach (var member in notReached) Console.WriteLine("  " + member);
}

static bool IsManaged(string file)
{
    using var stream = File.OpenRead(file);
    using var pe = new PEReader(stream);
    return pe.HasMetadata;
}

// The type and member references of an assembly that resolve to the assembly named library.
static IEnumerable<string> References(string file, string library)
{
    using var stream = File.OpenRead(file);
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    var names = new List<string>();

    bool FromLibrary(TypeReference type, out string name)
    {
        name = metadata.GetString(type.Namespace) is { Length: > 0 } ns ? $"{ns}.{metadata.GetString(type.Name)}" : metadata.GetString(type.Name);
        var scope = type.ResolutionScope;
        if (scope.Kind == HandleKind.TypeReference)
        {
            var outer = metadata.GetTypeReference((TypeReferenceHandle)scope);
            if (!FromLibrary(outer, out var outerName)) return false;
            name = $"{outerName}+{metadata.GetString(type.Name)}";
            return true;
        }

        return scope.Kind == HandleKind.AssemblyReference
            && metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)scope).Name) == library;
    }

    foreach (var handle in metadata.TypeReferences)
        if (FromLibrary(metadata.GetTypeReference(handle), out var name)) names.Add("type " + name);

    foreach (var handle in metadata.MemberReferences)
    {
        var member = metadata.GetMemberReference(handle);
        var parent = member.Parent;
        if (parent.Kind == HandleKind.TypeSpecification)
        {
            // A member of a generic instance: read the generic type from the blob is not needed for the count; use
            // the name only, with the mark of an instance.
            continue;
        }

        if (parent.Kind == HandleKind.TypeReference && FromLibrary(metadata.GetTypeReference((TypeReferenceHandle)parent), out var owner))
            names.Add($"member {owner}::{metadata.GetString(member.Name)}");
    }

    return names.Distinct();
}
