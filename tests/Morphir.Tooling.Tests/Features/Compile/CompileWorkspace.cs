// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.TestSupport;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>A sandboxed directory for compile tests, with an isolated environment snapshot.</summary>
public sealed class CompileWorkspace : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create("morphir-compile-");

    /// <summary>The working directory of the invocation.</summary>
    public string Root => CompilePaths.RealPath(_sandbox.Work);

    /// <summary>A path under <see cref="Root"/>.</summary>
    public string PathOf(string relative) => Path.GetFullPath(Path.Combine(Root, relative));

    /// <summary>Writes a file under <see cref="Root"/> and returns its absolute path.</summary>
    public string Write(string relative, string text)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>The invocation context, in <paramref name="directory"/> (relative to the root) or the root.</summary>
    public CompileContext Context(string? directory = null) =>
        new(directory is null ? Root : PathOf(directory), _sandbox.EnvironmentSnapshot(new Dictionary<string, string>()));

    public void Dispose() => _sandbox.Dispose();
}
