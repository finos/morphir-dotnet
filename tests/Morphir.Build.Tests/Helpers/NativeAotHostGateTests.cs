// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// The gate of the Native AOT <c>morphir</c> after its publish: no IDE-only assembly in the input of ILC, and an
/// executable that is not larger than the limit.
/// </summary>
public class NativeAotHostGateTests
{
    private static readonly string[] Response =
    [
        "obj/Release/net11.0/linux-x64/morphir.dll",
        "-o:obj/Release/net11.0/linux-x64/native/morphir.o",
        "-r:/nuget/microsoft.codeanalysis.csharp/5.12.0/lib/net9.0/Microsoft.CodeAnalysis.CSharp.dll",
        "-r:/repo/src/Morphir.Lang.CSharp/bin/Release/net11.0/Morphir.Lang.CSharp.Analyzers.dll",
        "--targetos:linux",
    ];

    [Test]
    public void TheAssembliesAreTheInputAndTheReferencesOfTheResponseFile() =>
        NativeAotHostGate.AssembliesOf(Response).Should().Equal(
            "morphir", "Microsoft.CodeAnalysis.CSharp", "Morphir.Lang.CSharp.Analyzers");

    [Test]
    public void AHostWithNoIdeAssemblyUnderTheLimitPasses() =>
        NativeAotHostGate.Problems(Response, executableBytes: 90, limitBytes: 100).Should().BeEmpty();

    [Test]
    [Arguments("Microsoft.CodeAnalysis.Workspaces")]
    [Arguments("Microsoft.CodeAnalysis.CSharp.Workspaces")]
    [Arguments("Morphir.Lang.CSharp.CodeFixes")]
    public void AnIdeAssemblyInTheClosureFails(string assembly)
    {
        var problems = NativeAotHostGate.Problems([.. Response, $"-r:/somewhere/{assembly}.dll"], executableBytes: 90, limitBytes: 100);
        problems.Should().ContainSingle().Which.Should().Contain(assembly);
    }

    [Test]
    public void AnExecutableLargerThanTheLimitFails()
    {
        var problems = NativeAotHostGate.Problems(Response, executableBytes: 101, limitBytes: 100);
        problems.Should().ContainSingle().Which.Should().Contain("101").And.Contain("100");
    }

    [Test]
    public void TheLimitIsTheValueOfTheAotGuide()
    {
        var guide = File.ReadAllText(Path.Combine(TestFixture.GetRepositoryRoot(), ".agents", "aot-optimization.md"));
        guide.Should().Contain(NativeAotHostGate.SizeLimitBytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " bytes");
    }
}
