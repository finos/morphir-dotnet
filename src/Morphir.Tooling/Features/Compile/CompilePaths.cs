// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Tooling.Features.Compile;

/// <summary>Path helpers of the compile command.</summary>
public static class CompilePaths
{
    /// <summary>
    /// The absolute path with every existing linked directory segment replaced by its final target. Project
    /// discovery does not follow links, so the command gives it real paths. A missing segment stays as written.
    /// </summary>
    public static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";
        var current = root;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0) continue;
            var next = Path.Combine(current, segment);
            var directory = new DirectoryInfo(next);
            if (directory.Exists && directory.LinkTarget is not null && directory.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                next = target.FullName;
            current = next;
        }
        return current;
    }
}
