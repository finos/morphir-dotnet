// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Cli;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.ProjectMigration;

/// <summary>Registers project-owned operations without accessing any project files.</summary>
public sealed class ProjectCommandProvider : ICommandProvider, IMorphirHostInitializationErrorHandler
{
    private readonly IProjectMigrationHost _host;
    private bool _initializationFailed;

    public ProjectCommandProvider() : this(new NativeProjectMigrationHost()) { }
    public ProjectCommandProvider(IProjectMigrationHost host) => _host = host;
    void IMorphirHostInitializationErrorHandler.HandleHostInitializationFailure() => _initializationFailed = true;

    public void RegisterCommands(RootCommand rootCommand, IServiceCollection services)
    {
        var project = new Command("project", "Manage one Morphir project");
        project.Subcommands.Add(new ProjectMigrationCommand(_host, _initializationFailed));
        rootCommand.Subcommands.Add(project);
    }
}

/// <summary>Argument binding and redacted presentation over shared single-project migration.</summary>
public sealed class ProjectMigrationCommand : Command, IMorphirParseErrorHandler
{
    private readonly IProjectMigrationHost _host;
    private readonly bool _initializationFailed;
    private readonly Argument<string?> _path = new("path") { Arity = ArgumentArity.ZeroOrOne, Description = "One project directory or classic morphir.json" };
    private readonly Option<string?> _output = new("--output") { Description = "Same-project TOML primary output" };
    private readonly Option<string?> _boundary = new("--discovery-boundary") { Description = "Bound discovery to this directory (default: MORPHIR_DISCOVERY_BOUNDARY, else the filesystem root)" };
    private readonly Option<bool> _dryRun = new("--dry-run") { Description = "Preview without writing" };
    private readonly Option<string> _json = JsonOutputOption.Create();

    public ProjectMigrationCommand(IProjectMigrationHost host, bool initializationFailed = false) : base("migrate", "Migrate one classic project configuration to TOML")
    {
        _host = host;
        _initializationFailed = initializationFailed;
        Arguments.Add(_path);
        foreach (var option in new Option[] { _output, _boundary, _dryRun, _json }) Options.Add(option);
        SetAction(Execute);
    }

    /// <summary>
    /// Renders the parse errors as one <c>project.migrate_usage</c> failure with the messages of the host that quote no
    /// value (<see cref="OptionValueRewrite.RedactedErrorMessages"/>), for example an unknown <c>--json</c> mode or the
    /// hint <c>--json=document</c>: the redacted report repeats no raw input. A parse error has no valid parse result, so the output mode comes from the tokens (JSON-4).
    /// </summary>
    public int HandleParseErrors(ParseResult parsed, IReadOnlyList<string> messages) => Render(ProjectMigrationReport.Failure("project.migrate_usage",
        string.Join(" ", ["Expected one project directory or classic morphir.json and supported migration options.", .. OptionValueRewrite.RedactedErrorMessages(parsed)])),
        JsonOutputOption.ScanTokens([.. parsed.Tokens.Select(token => token.Value)]));

    private int Execute(ParseResult parsed)
    {
        var json = JsonOutputOption.ModeOf(parsed);
        var dryRun = parsed.GetValue(_dryRun);
        if (new[] { parsed.GetValue(_path), parsed.GetValue(_output), parsed.GetValue(_boundary) }.Any(value => value is not null && string.IsNullOrWhiteSpace(value)))
            return Render(ProjectMigrationReport.Failure("project.migrate_usage", "Path, output and discovery boundary must be nonempty.", dryRun), json);
        if (_initializationFailed)
            return Render(ProjectMigrationReport.Failure("project.migrate_initialization", "Host configuration initialization failed. Check appsettings and access permissions.", dryRun), json);
        // Migration never reads machine or personal sources, so only the boundary applies; MORPHIR_ISOLATED is not consulted.
        var containment = Containment.Resolve(parsed.GetValue(_boundary), flagIsolated: true, Containment.CurrentEnvironment(),
            System.Environment.CurrentDirectory).Value!;
        var outside = containment.RequireInside(System.Environment.CurrentDirectory)
            ?? (parsed.GetValue(_path) is { } path ? containment.RequireInside(Path.GetFullPath(path)) : null);
        if (outside is not null) return Render(ProjectMigrationReport.Failure(outside.Code, outside.Message, dryRun), json);
        try { return Render(_host.Execute(new(parsed.GetValue(_path), parsed.GetValue(_output), containment.NativeBoundary, dryRun)), json); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Render(ProjectMigrationReport.Failure("project.migrate_io", "Migration could not read or write the selected project. Check paths and permissions.", dryRun), json); }
    }

    private static int Render(ProjectMigrationReport report, JsonOutputMode json)
    {
        foreach (var diagnostic in report.Diagnostics)
            Console.Error.WriteLine($"{diagnostic.Path ?? "project"}: {diagnostic.Code}: {diagnostic.Message}");
        foreach (var artifact in report.RecoveryArtifacts)
            Console.Error.WriteLine($"recovery\t{artifact.Kind}\t{artifact.Path}\t{artifact.TransactionId}");
        if (json != JsonOutputMode.None) JsonReportOutput.Write(ConsoleOutputHelper.Stdout, json, report, ProjectMigrationReport.JsonInfo);
        else
        {
            ConsoleOutputHelper.WriteLineToStdout($"{report.Outcome}\t{report.Source?.Path ?? "project"}\t{report.Target?.Path ?? ""}");
            foreach (var field in report.Ledger)
                ConsoleOutputHelper.WriteLineToStdout($"{field.Disposition}\t{field.RawPath}\t{field.CanonicalPath ?? ""}\t{field.Value.GetRawText()}");
            foreach (var conflict in report.Conflicts)
                ConsoleOutputHelper.WriteLineToStdout($"conflict\t{conflict.Key}\t{conflict.SourceValue.GetRawText()}\t{conflict.TargetValue.GetRawText()}");
            foreach (var edit in report.Edits)
                ConsoleOutputHelper.WriteLineToStdout($"set\t{edit.Key}\t{edit.Value.GetRawText()}");
            ConsoleOutputHelper.WriteLineToStdout($"verification\t{(report.Verification?.Verified == true ? "verified" : "unverified")}");
            if (report.Verification is { } verification)
            {
                foreach (var difference in verification.Differences)
                    ConsoleOutputHelper.WriteLineToStdout($"difference\t{difference.Key}\t{difference.Before?.GetRawText() ?? "<missing>"}\t{difference.After?.GetRawText() ?? "<missing>"}\t{difference.BeforePath ?? ""}\t{difference.AfterPath ?? ""}");
                foreach (var origin in verification.OriginChanges)
                    ConsoleOutputHelper.WriteLineToStdout($"origin\t{origin.Key}\t{origin.Before?.Source.Path ?? "<missing>"}\t{origin.After?.Source.Path ?? "<missing>"}");
            }
        }
        return report.ExitCode;
    }
}
