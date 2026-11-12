// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Journeys;

/// <summary>
/// The opt-in local candidate journey (docs/content/docs/getting-started/local-morphirup-candidate.md): morphirup
/// installs a self-contained candidate, selects it, and then compiles and generates with it, online and offline with
/// no system .NET. It uses real executables and the frozen workflow, never a fake manager. A test with a time budget
/// (each child must start within three minutes of the start of the journey), so a framework test (design FORM-1).
/// </summary>
[Category("journey")]
[Category("lane")]
[Category("published")]
[Category("LocalCandidate")]
public sealed class LocalCandidateJourneyTests : BlackBoxTest
{
    [Test]
    public async Task InstallsSelectsCompilesAndGeneratesOfflineWithoutSystemDotnet()
    {
        if (LocalCandidateInputs.FromEnvironment() is not { } inputs)
        {
            Skip.Test("Opt-in actual-artifact test: supply MORPHIR_LOCAL_MANAGER_EXE and the candidate inputs in the runbook.");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This milestone exercises the Windows x64 managed offering only.");
            return;
        }

        await LocalCandidateJourney.RunAsync(inputs, Workspace, Workspace.CopyFixtureDirectory("Toolchain/workflows/fsharp-compile-generate"));
    }
}
