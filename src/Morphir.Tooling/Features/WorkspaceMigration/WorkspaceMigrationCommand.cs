// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.WorkspaceMigration;

/// <summary>Registers workspace operations without discovery or file access.</summary>
public sealed class WorkspaceCommandProvider : ICommandProvider, IMorphirHostInitializationErrorHandler
{
    private readonly IWorkspaceMigrationHost _host;
    private bool _initializationFailed;

    public WorkspaceCommandProvider() : this(new NativeWorkspaceMigrationHost()) { }
    public WorkspaceCommandProvider(IWorkspaceMigrationHost host) => _host = host;
    void IMorphirHostInitializationErrorHandler.HandleHostInitializationFailure() => _initializationFailed = true;

    public void RegisterCommands(RootCommand rootCommand, IServiceCollection services)
    {
        var workspace = new Command("workspace", "Manage a bounded Morphir workspace");
        workspace.Subcommands.Add(new WorkspaceMigrationCommand(_host, _initializationFailed));
        rootCommand.Subcommands.Add(workspace);
    }
}

/// <summary>Thin argument binding and safe presentation over shared aggregate migration.</summary>
public sealed class WorkspaceMigrationCommand : Command, IMorphirParseErrorHandler
{
    private readonly IWorkspaceMigrationHost _host;
    private readonly bool _initializationFailed;
    private readonly Argument<string?> _directory = new("directory") { Arity = ArgumentArity.ZeroOrOne, Description = "Bounded root; defaults to the current directory" };
    private readonly Option<string[]> _include = new("--include") { Description = "Root-relative candidate pattern; repeatable", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false };
    private readonly Option<string[]> _exclude = new("--exclude") { Description = "Root-relative exclusion pattern; repeatable", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false };
    private readonly Option<bool> _dryRun = new("--dry-run") { Description = "Preview the complete aggregate without writing" };
    private readonly Option<bool> _json = new("--json") { Description = "Emit one redacted structured JSON result" };

    public WorkspaceMigrationCommand(IWorkspaceMigrationHost host, bool initializationFailed = false) : base("migrate", "Adopt classic projects within one bounded workspace")
    {
        _host = host;
        _initializationFailed = initializationFailed;
        Arguments.Add(_directory);
        foreach (var option in new Option[] { _include, _exclude, _dryRun, _json }) Options.Add(option);
        SetAction(Execute);
    }

    public int HandleParseErrors(ParseResult parsed) => Render(WorkspaceMigrationReport.Failure("workspace.migrate_usage",
        "Expected one bounded directory and supported workspace migration options."), WantsJson(parsed));

    private static bool WantsJson(ParseResult parsed) => parsed.Tokens.Any(token => token.Value is "--json" or "--json=true");

    private int Execute(ParseResult parsed)
    {
        var json = parsed.GetValue(_json) || WantsJson(parsed);
        var dryRun = parsed.GetValue(_dryRun);
        var directory = parsed.GetValue(_directory);
        var includes = parsed.GetValue(_include) ?? [];
        var excludes = parsed.GetValue(_exclude) ?? [];
        // The parser accepts an option-looking token as a required string-array value.
        // Reject that ambiguity before any host call; './--name' expresses a literal leading dash.
        if (directory is not null && string.IsNullOrWhiteSpace(directory)
            || includes.Concat(excludes).Any(pattern => string.IsNullOrWhiteSpace(pattern) || pattern.StartsWith('-')))
            return Render(WorkspaceMigrationReport.Failure("workspace.migrate_usage", "Directory and filter patterns must be nonempty. Prefix a literal leading-dash pattern with './'.", dryRun), json);
        if (_initializationFailed)
            return Render(WorkspaceMigrationReport.Failure("workspace.migrate_initialization", "Host configuration initialization failed. Check appsettings and access permissions.", dryRun), json);
        try { return Render(_host.Execute(new(directory, includes, excludes, dryRun)), json); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Render(WorkspaceMigrationReport.Failure("workspace.migrate_io", "Workspace migration could not read or write the bounded root. Check paths and permissions.", dryRun), json); }
    }

    private static int Render(WorkspaceMigrationReport report, bool json)
    {
        foreach (var diagnostic in report.Diagnostics)
            Console.Error.WriteLine($"{diagnostic.Path ?? "workspace"}: {diagnostic.Code}: {diagnostic.Message}");
        foreach (var artifact in report.RecoveryArtifacts)
            Console.Error.WriteLine($"recovery\t{artifact.Kind}\t{artifact.Path}\t{artifact.TransactionId}");
        foreach (var path in report.UnresolvedPaths) Console.Error.WriteLine($"unresolved\t{path}");
        foreach (var directory in report.UncertainDirectories) Console.Error.WriteLine($"uncertain-directory\t{directory}");
        if (json) ConsoleOutputHelper.WriteLineToStdout(report.ToJson());
        else
        {
            ConsoleOutputHelper.WriteLineToStdout($"{report.Outcome}\t{report.RootPath ?? "workspace"}\t{report.WorkspaceTarget ?? ""}");
            foreach (var entry in report.Inventory)
                ConsoleOutputHelper.WriteLineToStdout($"{entry.Kind}\t{entry.RelativePath}\t{entry.Reason ?? ""}");
            foreach (var member in report.Members) ConsoleOutputHelper.WriteLineToStdout($"member\t{member}");
            foreach (var project in report.Projects)
            {
                foreach (var field in project.Ledger)
                    ConsoleOutputHelper.WriteLineToStdout($"{field.Disposition}\t{project.ProjectPath}\t{field.RawPath}\t{field.CanonicalPath ?? ""}\t{field.Value.GetRawText()}");
                foreach (var edit in project.Edits)
                    ConsoleOutputHelper.WriteLineToStdout($"set\t{project.Target?.Path ?? ""}\t{edit.Key}\t{edit.Value.GetRawText()}");
                foreach (var conflict in project.Conflicts)
                    ConsoleOutputHelper.WriteLineToStdout($"conflict\t{project.ProjectPath}\t{conflict.Key}\t{conflict.SourceValue.GetRawText()}\t{conflict.TargetValue.GetRawText()}");
                ConsoleOutputHelper.WriteLineToStdout($"verification\t{project.ProjectPath}\t{(project.Verification?.Verified == true ? "verified" : "unverified")}");
                if (project.Verification is { } verification)
                {
                    foreach (var difference in verification.Differences)
                        ConsoleOutputHelper.WriteLineToStdout($"difference\t{project.ProjectPath}\t{difference.Key}\t{difference.Before?.GetRawText() ?? "<missing>"}\t{difference.After?.GetRawText() ?? "<missing>"}\t{difference.BeforePath ?? ""}\t{difference.AfterPath ?? ""}");
                    foreach (var origin in verification.OriginChanges)
                        ConsoleOutputHelper.WriteLineToStdout($"origin\t{project.ProjectPath}\t{origin.Key}\t{origin.Before?.Source.Path ?? "<missing>"}\t{origin.After?.Source.Path ?? "<missing>"}");
                }
            }
            foreach (var edit in report.WorkspaceEdits)
                ConsoleOutputHelper.WriteLineToStdout($"set\t{report.WorkspaceTarget ?? ""}\t{edit.Key}\t{edit.Value.GetRawText()}");
            foreach (var effect in report.AdoptionEffects)
                ConsoleOutputHelper.WriteLineToStdout($"adoption\t{effect.ProjectPath}\t{effect.Kind}\t{effect.Before ?? "<missing>"}\t{effect.After ?? "<missing>"}");
            foreach (var path in report.RestoredPaths) ConsoleOutputHelper.WriteLineToStdout($"restored\t{path}");
            foreach (var directory in report.PreservedDirectories) ConsoleOutputHelper.WriteLineToStdout($"preserved-directory\t{directory}");
            ConsoleOutputHelper.WriteLineToStdout($"verification\t{(report.Verified ? "verified" : "unverified")}");
        }
        return report.ExitCode;
    }
}
