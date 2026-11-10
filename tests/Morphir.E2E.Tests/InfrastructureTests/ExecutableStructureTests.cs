// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Morphir.TestSupport;

namespace Morphir.E2E.Tests.InfrastructureTests;

/// <summary>
/// A .NET single-file bundle also passes a "one file, no runtime files" layout check, so the native-aot scenarios
/// check the executable itself. An apphost or bundle carries the bundle signature; a Native AOT executable does not,
/// and it exports <c>DotNetRuntimeDebugHeader</c>.
/// </summary>
public sealed class ExecutableStructureTests
{
    [Test]
    public void TheTestHostApphostIsAnApphost()
    {
        var apphost = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Morphir.E2E.Tests.exe" : "Morphir.E2E.Tests");
        if (!File.Exists(apphost)) Skip.Test($"No apphost at {apphost}.");
        ExecutableStructure.Of(apphost).Should().Be(ExecutableKind.AppHost);
    }

    [Test]
    public void TheMarkersDecideTheKind()
    {
        using var sandbox = TestSandbox.Create("morphir executable ");
        var root = sandbox.Root;
        string Write(string name, params byte[][] parts)
        {
            var path = Path.Combine(root, name);
            File.WriteAllBytes(path, [.. parts.SelectMany(part => part)]);
            return path;
        }

        byte[] padding = new byte[64];
        byte[] Offset(long value) => BitConverter.GetBytes(value);
        var debugHeader = "\0DotNetRuntimeDebugHeader\0"u8.ToArray();

        ExecutableStructure.Of(Write("aot", padding, debugHeader, padding)).Should().Be(ExecutableKind.NativeAot);
        ExecutableStructure.Of(Write("bundle", padding, Offset(4096), ExecutableStructure.BundleSignature, padding)).Should().Be(ExecutableKind.SingleFileBundle);
        ExecutableStructure.Of(Write("apphost", padding, Offset(0), ExecutableStructure.BundleSignature, padding)).Should().Be(ExecutableKind.AppHost);
        ExecutableStructure.Of(Write("both", padding, Offset(4096), ExecutableStructure.BundleSignature, debugHeader)).Should().Be(ExecutableKind.SingleFileBundle,
            "a bundle signature wins over the export name");
        ExecutableStructure.Of(Write("other", padding)).Should().Be(ExecutableKind.Other);
    }
}
