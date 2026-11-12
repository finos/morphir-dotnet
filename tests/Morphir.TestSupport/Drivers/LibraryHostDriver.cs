// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Morphir.TestSupport.Drivers;

/// <summary>The form in which a program is published.</summary>
public enum ProgramForm
{
    /// <summary>A framework-dependent publish, for no runtime identifier.</summary>
    Managed,

    /// <summary>A self-contained and trimmed publish for the runtime identifier of the machine.</summary>
    Trimmed,

    /// <summary>A Native AOT publish for the runtime identifier of the machine.</summary>
    NativeAot,
}

/// <summary>A trim or AOT warning of a publish (<c>ILnnnn</c>). <paramref name="Assembly"/> is the assembly of a summary warning, or null.</summary>
public sealed record PublishWarning(string Code, string? Assembly, bool IsError, string Line)
{
    private static readonly Regex Diagnostic = new(@"\b(?<kind>warning|error) (?<code>IL\d{4})\s*:\s*(?<text>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex SummaryAssembly = new(@"^Assembly '(?<assembly>[^']+)' produced", RegexOptions.CultureInvariant);

    /// <summary>The trim and AOT diagnostics of the output of a publish, each once.</summary>
    public static IReadOnlyList<PublishWarning> Parse(string publishOutput)
    {
        var warnings = new List<PublishWarning>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in publishOutput.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (Diagnostic.Match(line) is not { Success: true } match || !seen.Add(line)) continue;
            var assembly = SummaryAssembly.Match(match.Groups["text"].Value) is { Success: true } summary ? summary.Groups["assembly"].Value : null;
            warnings.Add(new PublishWarning(match.Groups["code"].Value, assembly, match.Groups["kind"].Value == "error", line));
        }

        return warnings;
    }

    /// <summary>The warnings that no item of <paramref name="allowed"/> permits.</summary>
    public static IReadOnlyList<PublishWarning> Unexpected(IEnumerable<PublishWarning> warnings, IEnumerable<AllowedWarning> allowed)
    {
        var permitted = allowed.ToArray();
        return [.. warnings.Where(warning => !permitted.Any(item => item.Permits(warning)))];
    }
}

/// <summary>A trim or AOT warning that a test permits: the summary warning <paramref name="Code"/> of one assembly.</summary>
public sealed record AllowedWarning(string Code, string Assembly)
{
    public bool Permits(PublishWarning warning) =>
        !warning.IsError && warning.Code == Code && string.Equals(warning.Assembly, Assembly, StringComparison.Ordinal);

    public override string ToString() => $"{Code} of {Assembly}";
}

/// <summary>A program that a test published into its workspace.</summary>
/// <param name="Executable">The file that starts the program: an executable, or an assembly for <see cref="ProgramForm.Managed"/>.</param>
/// <param name="Publish">The result of the publish command.</param>
public sealed record PublishedProgram(string Name, ProgramForm Form, string Executable, IReadOnlyList<PublishWarning> Warnings, CommandResult Publish)
{
    public PublishedProgram ShouldHaveSucceeded() =>
        Publish.ExitCode == 0 ? this : throw new CommandAssertionException($"The publish of {Name} as {Form} failed.", Publish);

    /// <summary>The publish has no trim or AOT warning and no error other than those in <paramref name="allowed"/>.</summary>
    public PublishedProgram ShouldHaveOnlyTheWarnings(IReadOnlyCollection<AllowedWarning> allowed)
    {
        var unexpected = PublishWarning.Unexpected(Warnings, allowed);
        return unexpected.Count == 0
            ? this
            : throw new CommandAssertionException(
                $"The publish of {Name} as {Form} has {unexpected.Count} trim or AOT warnings that are not allowed (allowed: {(allowed.Count == 0 ? "none" : string.Join(", ", allowed))}):"
                + Environment.NewLine + string.Join(Environment.NewLine, unexpected.Select(warning => "  " + warning.Line)), Publish);
    }
}

/// <summary>
/// Runs a program of the repository that is not the CLI: a library host (a program that calls a shipped library
/// that the CLI does not reach), a probe, or a product program such as <c>morphirup</c>. It runs the built assembly,
/// or publishes the program into the workspace as managed, trimmed or Native AOT and runs the publish. A publish
/// writes each output, also those of the projects that the program references, to the workspace, so it replaces no
/// assembly of the solution build.
/// </summary>
public interface ILibraryHostDriver
{
    /// <summary>Runs the built assembly of the program with <c>dotnet</c>, in the workspace.</summary>
    Task<CommandResult> RunBuiltAsync(string program, IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default);

    /// <summary>Publishes the program into the workspace. The result has the warnings of the publish; a failed publish is not an exception.</summary>
    Task<PublishedProgram> PublishAsync(string program, ProgramForm form, CancellationToken cancellation = default);

    /// <summary>Runs a published program, in the workspace.</summary>
    Task<CommandResult> RunAsync(PublishedProgram program, IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default);
}

/// <summary>The library host driver on the build output and the project files that the test assembly names.</summary>
public sealed class LibraryHostDriver(TestPrograms programs, IScenarioWorkspace workspace, ProcessGate? gate = null) : ILibraryHostDriver
{
    /// <summary>The time limit of a run with no other limit.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    /// <summary>The time limit of a publish. A Native AOT publish compiles the complete program.</summary>
    public static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(15);

    public Task<CommandResult> RunBuiltAsync(string program, IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default) =>
        Run(programs.Of(program).Assembly, arguments, options, cancellation);

    public async Task<PublishedProgram> PublishAsync(string program, ProgramForm form, CancellationToken cancellation = default)
    {
        var subject = programs.Of(program);
        var folder = workspace.Directory("publish", $"{program}-{form.ToString().ToLowerInvariant()}");
        var output = folder.Directory("out");
        var configuration = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(subject.Assembly)))!;
        var info = new ProcessStartInfo("dotnet");
        foreach (var argument in PublishArguments(subject.Project, form, configuration, RuntimeInformation.RuntimeIdentifier, folder.Directory("artifacts").Path, output.Path))
            info.ArgumentList.Add(argument);

        // The publish restores and builds with the SDK of the machine, so it needs the real NuGet cache; the workspace
        // gives it its own home and temporary directory.
        var result = await ProductProcess.RunAsync(
            info, CliDriver.CommandLineOf(info), workspace, gate, new CliRunOptions { Timeout = PublishTimeout }, PublishTimeout, cancellation).ConfigureAwait(false);
        var name = Path.GetFileNameWithoutExtension(subject.Assembly);
        var executable = form == ProgramForm.Managed
            ? Path.Combine(output.Path, name + ".dll")
            : Path.Combine(output.Path, OperatingSystem.IsWindows() ? name + ".exe" : name);
        return new PublishedProgram(program, form, executable, PublishWarning.Parse(result.StandardOutput + "\n" + result.StandardError), result);
    }

    public Task<CommandResult> RunAsync(PublishedProgram program, IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default)
    {
        program.ShouldHaveSucceeded();
        return Run(program.Executable, arguments, options, cancellation);
    }

    /// <summary>The arguments of <c>dotnet</c> for the publish of <paramref name="project"/> in <paramref name="form"/>.</summary>
    public static IReadOnlyList<string> PublishArguments(
        string project, ProgramForm form, string configuration, string runtimeIdentifier, string artifacts, string output)
    {
        List<string> arguments = ["publish", project, "-c", configuration, "--artifacts-path", artifacts, "-o", output, "-nologo"];
        switch (form)
        {
            case ProgramForm.Trimmed:
                arguments.AddRange(["-r", runtimeIdentifier, "-p:SelfContained=true", "-p:PublishTrimmed=true"]);
                break;
            case ProgramForm.NativeAot:
                // MorphirProbeAot switches on PublishAot in the probes and in morphirup.
                arguments.AddRange(["-r", runtimeIdentifier, "-p:PublishAot=true", "-p:MorphirProbeAot=true"]);
                break;
        }

        if (form != ProgramForm.Managed)
        {
            // A restore for one runtime identifier adds a section to each lock file. The relative path puts the lock
            // file of each project below its own obj/, so the committed lock files stay as they are. src/ does the
            // same with MorphirRidSpecificPublish; tests/ has no such setting.
            arguments.Add("-p:MorphirRidSpecificPublish=true");
            arguments.Add($"-p:NuGetLockFilePath=obj/publish-lock/{runtimeIdentifier}/packages.lock.json");
        }

        // The warnings are a result of the test, not a failure of the publish.
        arguments.Add("-p:ILLinkTreatWarningsAsErrors=false");
        return arguments;
    }

    private Task<CommandResult> Run(string file, IReadOnlyList<string> arguments, CliRunOptions? options, CancellationToken cancellation)
    {
        var assembly = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo(assembly ? "dotnet" : file);
        if (assembly) info.ArgumentList.Add(file);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return ProductProcess.RunAsync(info, CliDriver.CommandLineOf(info), workspace, gate, options, DefaultTimeout, cancellation);
    }
}
