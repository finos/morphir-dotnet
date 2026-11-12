// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps for the programs of the repository that are not the CLI: the hosts of shipped libraries, the Native AOT
/// probes and morphirup. Each program has its arguments here, so a scenario names only the program.
/// </summary>
[Binding]
public sealed class LibraryHostSteps(ILibraryHostDriver hosts, IScenarioWorkspace workspace, ScenarioRun run)
{
    private string? _program;
    private CommandResult? _built;
    private PublishedProgram? _published;

    private string Program => _program ?? throw new InvalidOperationException("The scenario named no program before this step.");

    private PublishedProgram Published => _published ?? throw new InvalidOperationException("The scenario published no program before this step.");

    // The arguments of each program. A fixture directory is copied into the workspace first.
    private IReadOnlyList<string> ArgumentsOf(string program) => program switch
    {
        "configuration-io-smoke" => ["--fixtures", workspace.CopyFixtureDirectory("Configuration").Path],
        "conformance-smoke" => [workspace.CopyFixtureDirectory("Conformance").Path],
        "morphirup" => MorphirupPlan(),
        _ => [],
    };

    // A plan-only setup with the recorded digest. The recipe has a decomposed character, so a morphirup that does
    // not normalize to NFC gives another digest.
    private IReadOnlyList<string> MorphirupPlan()
    {
        var probe = workspace.CopyFixtureDirectory("Morphirup");
        return
        [
            "setup", "--plan", "--platform", "linux-x64",
            "--request", probe.File("toolchain.toml").Path,
            "--recipe", probe.File("recipe.json").Path,
            "--repository-id", new string('b', 64),
            "--artifact-sizes", probe.File("artifact-sizes.json").Path,
            "--expect-plan", workspace.ReadText(probe.File("plan-sha256.txt")).Trim(),
        ];
    }

    [When("I run the program {string}")]
    public async Task RunTheProgram(string program)
    {
        _program = program;
        run.Result = _built = await hosts.RunBuiltAsync(program, ArgumentsOf(program));
    }

    [When("I plan the morphirup setup of the probe with no ICU")]
    public async Task PlanWithNoIcu()
    {
        // An app-local ICU version that does not exist: a morphirup that used ICU fails at its first globalization
        // call. In invariant mode the setting is never read.
        _program = "morphirup";
        run.Result = await hosts.RunBuiltAsync("morphirup", MorphirupPlan(), new CliRunOptions
        {
            Environment = new Dictionary<string, string?>
            {
                ["DOTNET_SYSTEM_GLOBALIZATION_APPLOCALICU"] = "0.0.1",
                ["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = null,
            },
        });
    }

    [When("I publish the program with Native AOT")]
    public async Task PublishTheProgramWithNativeAot()
    {
        _published = await hosts.PublishAsync(Program, ProgramForm.NativeAot);
        run.Result = _published.Publish;
        _published.ShouldHaveSucceeded();
    }

    [When("I run the published program")]
    public async Task RunThePublishedProgram() => run.Result = await hosts.RunAsync(Published, ArgumentsOf(Program));

    [Then("the published program writes the same output as the built program")]
    public void TheSameOutput()
    {
        var built = _built ?? throw new InvalidOperationException("The scenario did not run the built program before this step.");
        if (run.Result.StandardOutput != built.StandardOutput)
        {
            throw new CommandAssertionException(
                "The published program writes another output than the built program.\noutput of the built program:\n" + built.StandardOutput, run.Result);
        }
    }

    [Then("the publish has no trim or AOT warning except the summary warnings of {string}")]
    public void OnlyTheSummaryWarningsOf(string assemblies) =>
        Published.ShouldHaveOnlyTheWarnings(
        [
            .. assemblies.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(assembly => new[] { new AllowedWarning("IL2104", assembly), new AllowedWarning("IL3053", assembly) }),
        ]);
}
