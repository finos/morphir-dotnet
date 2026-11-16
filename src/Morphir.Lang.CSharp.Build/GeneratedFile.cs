// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Build.Framework;

namespace Morphir.Lang.CSharp.Build;

/// <summary>
/// A generated file as an MSBuild item, with custom metadata only. The task has only the contract of
/// Microsoft.Build.Framework, so it has its own item type; MSBuild adds the well-known metadata.
/// </summary>
internal sealed class GeneratedFile(string path) : ITaskItem
{
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);

    public string ItemSpec { get; set; } = path;

    public ICollection MetadataNames => new List<string>(_metadata.Keys);

    public int MetadataCount => _metadata.Count;

    public string GetMetadata(string metadataName) =>
        string.Equals(metadataName, "FullPath", StringComparison.OrdinalIgnoreCase) ? ItemSpec
        : _metadata.TryGetValue(metadataName, out var value) ? value : "";

    public void SetMetadata(string metadataName, string metadataValue) => _metadata[metadataName] = metadataValue;

    public void RemoveMetadata(string metadataName) => _metadata.Remove(metadataName);

    public void CopyMetadataTo(ITaskItem destinationItem)
    {
        foreach (var entry in _metadata) destinationItem.SetMetadata(entry.Key, entry.Value);
    }

    public IDictionary CloneCustomMetadata() => new Dictionary<string, string>(_metadata, StringComparer.OrdinalIgnoreCase);
}
