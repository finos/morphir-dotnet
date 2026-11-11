// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// Writes a minimal library assembly with embedded manifest resources, a stand-in for a project such as
/// Morphir.Extensions.Wasm that embeds a file with <c>&lt;EmbeddedResource Include="…" LogicalName="…" /&gt;</c>.
/// </summary>
internal static class StandInAssembly
{
    public static void Write(string path, string name, params (string LogicalName, byte[] Content)[] resources)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString($"{name}.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var managedResources = new BlobBuilder();
        foreach (var (logicalName, content) in resources)
        {
            metadata.AddManifestResource(ManifestResourceAttributes.Public, metadata.GetOrAddString(logicalName), default, (uint)managedResources.Count);
            managedResources.WriteInt32(content.Length);
            managedResources.WriteBytes(content);
        }

        var builder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder(),
            managedResources: resources.Length > 0 ? managedResources : null);
        var image = new BlobBuilder();
        builder.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }
}
