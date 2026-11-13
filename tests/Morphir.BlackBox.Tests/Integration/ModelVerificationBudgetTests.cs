// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace Morphir.BlackBox.Tests.Integration;

/// <summary>
/// <c>ir verify</c> of a real distribution within a time budget, and of models at the depth limit. A test with a time
/// budget is a framework test (design FORM-1). The greeting example of finos/morphir is the file that its release
/// qualification verifies on each offering and platform; see <c>tests/Fixtures/MorphirIR/PROVENANCE.md</c>.
/// </summary>
[Category("integration")]
[Category("needs-model-verification")]
public sealed class ModelVerificationBudgetTests : BlackBoxTest
{
    /// <summary>
    /// The budget of one verification. A healthy run takes well under a second on the development build and on Native
    /// AOT. Schema evaluation that grows with the nesting of the model took more than ten minutes for this file.
    /// </summary>
    static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>The deepest nesting of a model that <c>ir verify</c> reads.</summary>
    const int DepthLimit = 512;

    [Test]
    public async Task The_greeting_example_of_finos_morphir_is_valid_within_the_budget()
    {
        var model = Workspace.CopyFixture("Models/greeting-example-v3.json");

        var result = await Drivers.Verification.VerifyAsync(model, new VerificationOptions { Json = true });

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Describe());
        VerificationReport.Of(result).ShouldBeValid();
        await Assert.That(result.Duration).IsLessThan(Budget).Because(result.Describe());
    }

    /// <summary>
    /// The executable holds a model just within the depth limit: a stack overflow would end the process with no
    /// report. Native AOT and each operating system have frames of their own sizes, so each run of the target checks it.
    /// </summary>
    [Test]
    public async Task A_model_just_within_the_depth_limit_is_valid()
    {
        var json = ModelNestedTo(DepthLimit - 2);
        await Assert.That(NestingOf(json)).IsEqualTo(DepthLimit - 2);
        var model = Workspace.File("deep.json");
        Workspace.WriteText(model, json);

        var result = await Drivers.Verification.VerifyAsync(model, new VerificationOptions { Json = true });

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Describe());
        VerificationReport.Of(result).ShouldBeValid();
        await Assert.That(result.Duration).IsLessThan(Budget).Because(result.Describe());
    }

    [Test]
    public async Task A_model_deeper_than_the_depth_limit_is_refused_with_a_report()
    {
        var json = ModelNestedTo(DepthLimit + 8);
        await Assert.That(NestingOf(json)).IsEqualTo(DepthLimit + 8);
        var model = Workspace.File("too-deep.json");
        Workspace.WriteText(model, json);

        var result = await Drivers.Verification.VerifyAsync(model, new VerificationOptions { Json = true });

        await Assert.That(result.ExitCode).IsEqualTo(1).Because(result.Describe());
        var report = VerificationReport.Of(result).ShouldBeInvalid();
        await Assert.That(report.Errors.Any(error => error.Message.Contains($"depth of {DepthLimit}", StringComparison.Ordinal)))
            .IsTrue().Because(result.Describe());
    }

    /// <summary>A v3 library whose one value nests <c>Apply</c> until the model has the nesting <paramref name="nesting"/>.</summary>
    static string ModelNestedTo(int nesting)
    {
        const string prefix = """{"formatVersion":3,"distribution":["Library",[["test"],["package"]],[],{"modules":[[[["deep"]],{"access":"Public","value":{"types":[],"values":[[["deep","value"],{"access":"Public","value":{"doc":"","value":{"inputTypes":[],"outputType":["Unit",{}],"body":""";
        const string suffix = "}}}]],\"doc\":null}}]]}]}";
        const string unit = """["Unit",{}]""";
        // The prefix leaves 12 arrays and objects open at the body; a Unit value adds 2 and each Apply adds 1.
        var applies = nesting - 12 - 2;
        var text = new StringBuilder(prefix);
        for (var i = 0; i < applies; i++) text.Append("""["Apply",{},""");
        text.Append(unit);
        for (var i = 0; i < applies; i++) text.Append(',').Append(unit).Append(']');
        return text.Append(suffix).ToString();
    }

    /// <summary>The deepest nesting of arrays and objects in <paramref name="json"/>, which has no bracket in a string.</summary>
    static int NestingOf(string json)
    {
        int depth = 0, deepest = 0;
        foreach (var c in json)
        {
            if (c is '[' or '{') deepest = Math.Max(deepest, ++depth);
            else if (c is ']' or '}') depth--;
        }

        return deepest;
    }
}
