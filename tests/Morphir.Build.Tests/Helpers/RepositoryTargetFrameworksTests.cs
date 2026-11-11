// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The build finds test assemblies and publish outputs below bin/ and obj/ folders that carry the target framework.
/// It reads the target frameworks from Directory.Build.props, the file that the projects use, so the two cannot
/// disagree.
/// </summary>
public class RepositoryTargetFrameworksTests
{
    [Test]
    public void TheRepositoryTargetsDotNet11AndShipsLibrariesForDotNet10()
    {
        var frameworks = RepositoryTargetFrameworks.Load(Path.Combine(TestFixture.GetRepositoryRoot(), "Directory.Build.props"));

        frameworks.Current.Should().Be("net11.0");
        frameworks.Library.Should().Equal("net11.0", "net10.0");
        frameworks.Hosted.Should().Be("net10.0", "MSBuild and Myriad must load the tasks and plugins on the .NET 10 SDK");
    }

    [Test]
    public async Task APropertyThatIsMissingIsAnError()
    {
        var file = Path.Combine(Path.GetTempPath(), $"tfm-{Guid.NewGuid():N}.props");
        await File.WriteAllTextAsync(file, "<Project><PropertyGroup><MorphirCurrentTargetFramework>net11.0</MorphirCurrentTargetFramework></PropertyGroup></Project>");
        try
        {
            var load = () => RepositoryTargetFrameworks.Load(file);
            load.Should().Throw<InvalidOperationException>().WithMessage("*MorphirLibraryTargetFrameworks*");
        }
        finally
        {
            File.Delete(file);
        }
    }
}
