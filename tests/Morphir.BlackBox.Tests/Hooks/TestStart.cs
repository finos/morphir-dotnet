// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Hooks;

/// <summary>
/// The start and the end of one test, for a Gherkin scenario and for a framework test. Both kinds get the same
/// decision from the same tags.
/// </summary>
internal static class TestStart
{
    /// <summary>
    /// Makes the one eligibility decision of a test from its tags or categories: the host of the test, or the one
    /// reason for which the run does not execute it.
    /// </summary>
    public static Eligibility Decide(IEnumerable<string> tags) => RunHooks.Session.Decide(TestNeeds.FromTags(tags));

    /// <summary>Ends the test, and prints the path of a workspace that the run keeps as evidence.</summary>
    public static async Task EndAsync(DriverSet drivers, bool failed)
    {
        if (await drivers.EndAsync(failed) is { } kept)
            Console.Error.WriteLine($"The workspace of the failed test is kept: {kept}");
    }
}
