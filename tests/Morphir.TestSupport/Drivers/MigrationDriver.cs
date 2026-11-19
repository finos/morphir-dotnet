// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The migration commands as intents: <c>project migrate</c> adopts one classic project, and
/// <c>workspace migrate</c> adopts the classic projects of a workspace.
/// </summary>
public interface IMigrationDriver
{
    /// <summary>Runs <c>project migrate</c> with <paramref name="arguments"/> (its options) in the machine of the scenario.</summary>
    Task<CommandResult> MigrateProjectAsync(IReadOnlyList<string> arguments, ScenarioMachine machine, CancellationToken cancellation = default);

    /// <summary>Runs <c>workspace migrate</c> with <paramref name="arguments"/> (its options) in the machine of the scenario.</summary>
    Task<CommandResult> MigrateWorkspaceAsync(IReadOnlyList<string> arguments, ScenarioMachine machine, CancellationToken cancellation = default);

    /// <summary>
    /// Runs <c>project migrate</c> with the <c>--json</c> argument <paramref name="json"/> and then
    /// <paramref name="arguments"/>, so a path among them is the argument after the <c>--json</c> argument.
    /// </summary>
    Task<CommandResult> MigrateProjectAsync(JsonOutputArgument json, IReadOnlyList<string> arguments, ScenarioMachine machine, CancellationToken cancellation = default);

    /// <summary>
    /// Runs <c>workspace migrate</c> with the <c>--json</c> argument <paramref name="json"/> and then
    /// <paramref name="arguments"/>, so a directory among them is the argument after the <c>--json</c> argument.
    /// </summary>
    Task<CommandResult> MigrateWorkspaceAsync(JsonOutputArgument json, IReadOnlyList<string> arguments, ScenarioMachine machine, CancellationToken cancellation = default);
}

/// <summary>Builds the command lines of the migrations and gives them to the CLI driver.</summary>
public sealed class MigrationDriver(ICliDriver cli) : IMigrationDriver
{
    public Task<CommandResult> MigrateProjectAsync(IReadOnlyList<string> arguments, ScenarioMachine machine, CancellationToken cancellation = default) =>
        cli.RunAsync(["project", "migrate", .. arguments], machine.RunOptions(), cancellation);

    public Task<CommandResult> MigrateWorkspaceAsync(IReadOnlyList<string> arguments, ScenarioMachine machine, CancellationToken cancellation = default) =>
        cli.RunAsync(["workspace", "migrate", .. arguments], machine.RunOptions(), cancellation);

    public Task<CommandResult> MigrateProjectAsync(JsonOutputArgument json, IReadOnlyList<string> arguments, ScenarioMachine machine, CancellationToken cancellation = default) =>
        MigrateProjectAsync([.. json.Words, .. arguments, .. json.Trailing], machine, cancellation);

    public Task<CommandResult> MigrateWorkspaceAsync(JsonOutputArgument json, IReadOnlyList<string> arguments, ScenarioMachine machine, CancellationToken cancellation = default) =>
        MigrateWorkspaceAsync([.. json.Words, .. arguments, .. json.Trailing], machine, cancellation);
}

/// <summary>
/// The report of <c>project migrate --json</c> and <c>workspace migrate --json</c>, in either form. It states the result in
/// the field <c>outcome</c>, and its <c>exitCode</c> must agree with the exit code of the command.
/// </summary>
public sealed class MigrationReport : JsonCommandReport
{
    private MigrationReport(CommandResult result) : base(result, "migration")
    {
    }

    /// <summary><c>written</c>, <c>preview</c>, <c>unchanged</c>, <c>blocked</c> or <c>failed</c>.</summary>
    public string? Outcome => Text("outcome");

    /// <summary>Reads the report from standard output, in either form of <see cref="JsonOutputAssertions"/>.</summary>
    public static MigrationReport Of(CommandResult result) => new(result);

    public MigrationReport ShouldHaveOutcome(string outcome)
    {
        Check(Outcome == outcome, $"The outcome of the migration report is {Outcome ?? "null"}, not {outcome}.");
        return this;
    }
}
