// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Integration;

/// <summary>
/// <c>ir verify</c> of a real distribution within a time budget. A test with a time budget is a framework test
/// (design FORM-1). The greeting example of finos/morphir is the file that its release qualification verifies on
/// each offering and platform; see <c>tests/Fixtures/MorphirIR/PROVENANCE.md</c>.
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

    [Test]
    public async Task The_greeting_example_of_finos_morphir_is_valid_within_the_budget()
    {
        var model = Workspace.CopyFixture("Models/greeting-example-v3.json");

        var result = await Drivers.Verification.VerifyAsync(model, new VerificationOptions { Json = true });

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Describe());
        VerificationReport.Of(result).ShouldBeValid();
        await Assert.That(result.Duration).IsLessThan(Budget).Because(result.Describe());
    }
}
