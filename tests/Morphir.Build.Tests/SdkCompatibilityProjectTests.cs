// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// Morphir.SDK is the compatibility package that has only a dependency on Morphir.SDK.FSharp. The build of its project
/// must not emit an empty compatibility assembly anywhere. TestBuild runs after the solution build and the pack step.
/// </summary>
[Category("PlatformSpecific")]
public class SdkCompatibilityProjectTests
{
    [Test]
    public void TheCompatibilityProjectOfTheSdkHasNoAssemblyInItsBuildOutput()
    {
        var project = Path.Combine(TestFixture.GetRepositoryRoot(), "src", "Morphir.SDK");
        var assemblies = new[] { "bin", "obj" }
            .Select(folder => Path.Combine(project, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories))
            .ToArray();

        assemblies.Should().BeEmpty("the compatibility project emits no assembly");
    }
}
