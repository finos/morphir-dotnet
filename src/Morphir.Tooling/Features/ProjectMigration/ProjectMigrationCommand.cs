// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly Option<string?> _boundary = new("--discovery-boundary") { Description = "Bound discovery to this directory" };
    private readonly Option<bool> _dryRun = new("--dry-run") { Description = "Preview without writing" };
    private readonly Option<bool> _json = new("--json") { Description = "Emit one redacted structured JSON result" };

    public ProjectMigrationCommand(IProjectMigrationHost host, bool initializationFailed = false) : base("migrate", "Migrate one classic project configuration to TOML")
    {
        _host = host;
        _initializationFailed = initializationFailed;
        Arguments.Add(_path);
        foreach (var option in new Option[] { _output, _boundary, _dryRun, _json }) Options.Add(option);
        SetAction(Execute);
    }

    public int HandleParseErrors(ParseResult parsed) => Render(ProjectMigrationReport.Failure("project.migrate_usage",
        "Expected one project directory or classic morphir.json and supported migration options."), parsed.Tokens.Any(token => token.Value is "--json" or "--json=true"));

    private int Execute(ParseResult parsed)
    {
        var json = parsed.GetValue(_json) || parsed.Tokens.Any(token => token.Value is "--json" or "--json=true");
        var dryRun = parsed.GetValue(_dryRun);
        if (new[] { parsed.GetValue(_path), parsed.GetValue(_output), parsed.GetValue(_boundary) }.Any(value => value is not null && string.IsNullOrWhiteSpace(value)))
            return Render(ProjectMigrationReport.Failure("project.migrate_usage", "Path, output and discovery boundary must be nonempty.", dryRun), json);
        if (_initializationFailed)
            return Render(ProjectMigrationReport.Failure("project.migrate_initialization", "Host configuration initialization failed. Check appsettings and access permissions.", dryRun), json);
        try { return Render(_host.Execute(new(parsed.GetValue(_path), parsed.GetValue(_output), parsed.GetValue(_boundary), dryRun)), json); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Render(ProjectMigrationReport.Failure("project.migrate_io", "Migration could not read or write the selected project. Check paths and permissions.", dryRun), json); }
    }

    private static int Render(ProjectMigrationReport report, bool json)
    {
        foreach (var diagnostic in report.Diagnostics)
            Console.Error.WriteLine($"{diagnostic.Path ?? "project"}: {diagnostic.Code}: {diagnostic.Message}");
        foreach (var artifact in report.RecoveryArtifacts)
            Console.Error.WriteLine($"recovery\t{artifact.Kind}\t{artifact.Path}\t{artifact.TransactionId}");
        if (json) ConsoleOutputHelper.WriteLineToStdout(report.ToJson());
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
