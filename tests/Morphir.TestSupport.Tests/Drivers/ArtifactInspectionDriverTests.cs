// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The driver that gives a built file of a program and its metadata (design DRV-1 of the change
/// consolidate-black-box-tests). The fixtures are the build output of <c>Morphir.Compilation.Smoke</c>: an F#
/// assembly, the assemblies that it references, and its runtime configuration file.
/// </summary>
[Category("PlatformSpecific")]
public class ArtifactInspectionDriverTests
{
    private const string Program = "compilation-smoke";

    private static ArtifactInspectionDriver Driver() =>
        new(TestPrograms.OfTestAssembly(typeof(ArtifactInspectionDriverTests).Assembly));

    [Test]
    public void AFileOfTheBuildOutputOfAProgramIsGiven()
    {
        var file = Driver().BuiltFile(Program, "Morphir.Compilation.dll");

        file.Name.Should().Be("Morphir.Compilation.dll");
        file.Exists.Should().BeTrue();
        file.Path.Should().StartWith(TestPrograms.OfTestAssembly(typeof(ArtifactInspectionDriverTests).Assembly).Of(Program).OutputDirectory);
    }

    [Test]
    [Arguments("../outside.dll")]
    [Arguments("/etc/hosts")]
    public void AFileOutsideTheBuildOutputIsRefused(string relative)
    {
        var file = () => Driver().BuiltFile(Program, relative);

        file.Should().Throw<ArgumentException>().WithMessage("*outside*");
    }

    [Test]
    public void TheMetadataOfAnAssemblyHasItsReferences()
    {
        var assembly = Driver().BuiltFile(Program, "Morphir.Compilation.Smoke.dll").ReadAssembly();

        assembly.AssemblyReferences.Should().Contain(["Morphir.Compilation", "FSharp.Core"]);
        assembly.TypeReferenceNamespaces.Should().Contain("Morphir.Compilation");
        assembly.MembersOf("System", "String").Should().NotBeNull();
        assembly.UsesNamespace("System.Text.RegularExpressions").Should().BeFalse();
        assembly.UsesNamespace("Morphir").Should().BeTrue("a namespace below Morphir is used");
    }

    [Test]
    public void TheMemberReferencesOfATypeAreGivenByName()
    {
        var assembly = Driver().BuiltFile(Program, "Morphir.Compilation.Smoke.dll").ReadAssembly();

        assembly.MembersOf("Morphir.Compilation", "Checked").Should().Contain("pass");
        assembly.MembersOf("No.Such", "Type").Should().BeEmpty();
    }

    [Test]
    public void TheRuntimeConfigurationIsReadAsJson()
    {
        var configuration = Driver().BuiltFile(Program, "Morphir.Compilation.Smoke.runtimeconfig.json").ReadJson();

        configuration.GetProperty("runtimeOptions").GetProperty("tfm").GetString().Should().StartWith("net");
    }

    [Test]
    public void AFileThatIsNotThereIsAnErrorWithItsPath()
    {
        var read = () => Driver().BuiltFile(Program, "none.dll").ReadAssembly();

        read.Should().Throw<TestSessionException>().WithMessage("*none.dll*").WithMessage("*compilation-smoke*");
    }
}
