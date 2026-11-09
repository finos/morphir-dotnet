// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Compilation;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>Task 4.6: frontend selection by advertised capability (FE-2).</summary>
public sealed class FrontendResolverTests
{
    private static readonly LanguageId FSharp = LanguageId.Create("fsharp");

    private static string Code<T>(Checked<T> result) => result.Diagnostics.Single(d => d.IsError).CodeOrNull!.Value;

    private static string Message<T>(Checked<T> result) => result.Diagnostics.Single(d => d.IsError).Message;

    [Test]
    public void TheFrontendThatReadsTheLanguageAndServesTheTargetIsSelected()
    {
        var frontend = new FakeFrontend("fake.fsharp", FakeFrontend.FSharp());
        var resolved = new FrontendResolver([frontend], []).Resolve(FSharp, ["Rates.fs", "Pricing.fs"], FakeFrontend.V4);
        resolved.TryGetValue(out var chosen).Should().BeTrue(string.Join("\n", resolved.Diagnostics));
        chosen!.Frontend.Should().BeSameAs(frontend);
        chosen.Label.Should().Be("fake.fsharp (in-process)");
    }

    [Test]
    public void AnIrVersionThatNoFrontendServesNamesTheFrontendAndItsVersions()
    {
        var frontend = new FakeFrontend("fake.classic-only", FakeFrontend.FSharp(FakeFrontend.Classic("3.0.0")));
        var result = new FrontendResolver([frontend], []).Resolve(FSharp, ["Rates.fs"], FakeFrontend.V4);
        Code(result).Should().Be("compile.ir_version_not_served");
        Message(result).Should().Contain("fake.classic-only").And.Contain("classic 3.0.0").And.Contain("v4 4");
    }

    [Test]
    public void AMissingFrontendSaysWhereToGetOne()
    {
        var hint = new FrontendHint(FSharp, "The dotnet tool dotnet-morphir has a built-in F# frontend.");
        var result = new FrontendResolver([], [hint]).CheckAvailable(FSharp);
        Code(result).Should().Be("compile.frontend_unavailable");
        Message(result).Should().Contain("dotnet-morphir").And.Contain("[frontend.fsharp].extension").And.Contain("--extension");
    }

    [Test]
    public void ADocumentWhoseExtensionTheFrontendDoesNotClaimIsNamed()
    {
        var frontend = new FakeFrontend("fake.fsharp", FakeFrontend.FSharp());
        var result = new FrontendResolver([frontend], []).Resolve(FSharp, ["Rates.fs", "Script.fsx"], FakeFrontend.V4);
        Code(result).Should().Be("compile.frontend_unavailable");
        Message(result).Should().Contain("Script.fsx").And.Contain("fake.fsharp");
    }

    [Test]
    public void WithoutALanguageTheExtensionsOfEveryDocumentDecide()
    {
        var frontend = new FakeFrontend("fake.fsharp", FakeFrontend.FSharp());
        var resolver = new FrontendResolver([frontend], []);
        resolver.Resolve(null, ["A.fs", "B.fsi"], FakeFrontend.V4).TryGetValue(out var chosen).Should().BeTrue();
        chosen!.Language.Should().Be(FSharp);
        Code(resolver.Resolve(null, ["A.fs", "B.elm"], FakeFrontend.V4)).Should().Be("compile.language_required");
    }

    [Test]
    public void TheInProcessFrontendWinsAndTwoInProcessFrontendsAreAmbiguous()
    {
        var first = new FakeFrontend("fake.first", FakeFrontend.FSharp());
        var second = new FakeFrontend("fake.second", FakeFrontend.FSharp());
        var result = new FrontendResolver([first, second], []).Resolve(FSharp, ["A.fs"], FakeFrontend.V4);
        Code(result).Should().Be("compile.ambiguous_frontend");
        Message(result).Should().Contain("fake.first").And.Contain("fake.second");
    }

    [Test]
    public void AOneDocumentFrontendRefusesSeveralDocuments()
    {
        var capability = FakeFrontend.FSharp();
        var single = new FakeFrontend("fake.single", new FrontendCapability(capability.Languages, capability.Outputs, false, false));
        var resolver = new FrontendResolver([single], []);
        resolver.Resolve(FSharp, ["A.fs"], FakeFrontend.V4).IsPassed.Should().BeTrue();
        Message(resolver.Resolve(FSharp, ["A.fs", "B.fs"], FakeFrontend.V4)).Should().Contain("one document");
    }

    [Test]
    public void TheClaimedExtensionsFollowTheLanguage()
    {
        var resolver = new FrontendResolver([new FakeFrontend("fake.fsharp", FakeFrontend.FSharp())], []);
        resolver.ClaimedExtensions(FSharp).Should().BeEquivalentTo([".fs", ".fsi"]);
        resolver.ClaimedExtensions(LanguageId.Create("elm")).Should().BeEmpty();
        resolver.ClaimedExtensions(null).Should().BeEquivalentTo([".fs", ".fsi"]);
    }
}
