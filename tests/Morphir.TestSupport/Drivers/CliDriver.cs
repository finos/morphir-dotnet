// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Reflection;

namespace Morphir.TestSupport.Drivers;

/// <summary>The options of one command.</summary>
public sealed record CliRunOptions
{
    /// <summary>The time limit. It starts when the command has a place of the process gate. Default: <see cref="CliDriver.DefaultTimeout"/>.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Text for standard input. Standard input is closed after it.</summary>
    public string? StandardInput { get; init; }

    /// <summary>Variables for this command, added after the workspace contained the process. A null value removes a variable.</summary>
    public IReadOnlyDictionary<string, string?>? Environment { get; init; }

    /// <summary>
    /// The directory in which the command starts, as a full path. The default is the work area of the workspace.
    /// For a test that has not moved to a workspace yet and has its own folder; a black-box test uses the default.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Runs the host with the dynamic-code feature switch of the runtime off, the switch that is off in a Native AOT
    /// executable, so that a test of the development build sees what a host without dynamic code does. Only a host
    /// that is an assembly can run so: <see cref="DriverCapability.HostWithoutDynamicCode"/>.
    /// </summary>
    public bool WithoutDynamicCode { get; init; }
}

/// <summary>
/// Runs one command line of the product on one host of the selected target. It contains the process in the
/// workspace, gives it a time limit, and counts it against the process limit of the test process. Only this
/// driver and the workspace know the target and the machine.
/// </summary>
public interface ICliDriver
{
    TestTarget Target { get; }

    MorphirHost Host { get; }

    /// <summary>Runs the host with <paramref name="arguments"/>. Each argument is one argument of the process, also with a space in it.</summary>
    Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default);

    /// <summary>
    /// Runs the host with a command line that is one string, split as the operating system splits it. For a caller
    /// that has the command as text, for example a step with the command in a feature file.
    /// </summary>
    Task<CommandResult> RunCommandLineAsync(string commandLine, CliRunOptions? options = null, CancellationToken cancellation = default);
}

/// <summary>
/// The files of the hosts of the selected target, resolved once before the first test. A host with no file is an
/// error with the command that builds it: no other form and no other configuration is used.
/// </summary>
public sealed class CliArtifacts
{
    private const string MetadataPrefix = "Morphir.TestSupport.Cli.";

    private static readonly (MorphirHost Host, string Key, string Project)[] DevelopmentHosts =
    [
        (MorphirHost.Native, MetadataPrefix + "Native", "src/Morphir/Morphir.csproj"),
        (MorphirHost.Managed, MetadataPrefix + "Managed", "src/Morphir.Managed/Morphir.Managed.csproj"),
        (MorphirHost.DotnetTool, MetadataPrefix + "Tool", "src/Morphir.Tool/Morphir.Tool.csproj"),
    ];

    private readonly IReadOnlyDictionary<MorphirHost, string> _paths;

    private CliArtifacts(TestTarget target, IReadOnlyDictionary<MorphirHost, string> paths, string? configuration)
    {
        Target = target;
        _paths = paths;
        Configuration = configuration;
    }

    public TestTarget Target { get; }

    /// <summary>The build configuration of the development build, or null for another target.</summary>
    public string? Configuration { get; }

    /// <summary>The file that starts <paramref name="host"/>: an assembly (started with <c>dotnet</c>) or an executable.</summary>
    public string PathOf(MorphirHost host) =>
        _paths.TryGetValue(host, out var path) ? path : throw new TestSessionException(Target.MissingHostReason(host));

    /// <summary>The development build of <paramref name="testAssembly"/>, which imports Morphir.CliArtifacts.targets.</summary>
    public static CliArtifacts OfDevelopmentBuild(Assembly testAssembly) =>
        OfDevelopmentBuild(
            testAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(attribute => attribute.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal) && attribute.Value is not null)
                .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!),
            testAssembly.GetName().Name ?? "the test assembly",
            File.Exists);

    /// <summary>The development build from the assembly metadata that Morphir.CliArtifacts.targets writes.</summary>
    public static CliArtifacts OfDevelopmentBuild(
        IReadOnlyDictionary<string, string> metadata, string testAssembly, Func<string, bool> fileExists)
    {
        if (DevelopmentHosts.Any(host => !metadata.ContainsKey(host.Key)) || !metadata.TryGetValue(MetadataPrefix + "Configuration", out var configuration))
        {
            throw new TestSessionException(
                $"{testAssembly} has no path of the CLI entry points. Import tests/Morphir.TestSupport/build/Morphir.CliArtifacts.targets in its project.");
        }

        var missing = DevelopmentHosts.Where(host => !fileExists(metadata[host.Key])).ToArray();
        if (missing.Length > 0)
        {
            throw new TestSessionException(
                "The development build has no file for:" + Environment.NewLine + string.Join(Environment.NewLine, missing.Select(host =>
                    $"  the {host.Host.Name()} host, at {metadata[host.Key]}. Build it: dotnet build {host.Project} -c {configuration}")));
        }

        return new CliArtifacts(
            new TestTarget.DevelopmentBuild(),
            DevelopmentHosts.ToDictionary(host => host.Host, host => metadata[host.Key]),
            configuration);
    }

    /// <summary>The one executable of the target <see cref="TestTarget.Executable"/>.</summary>
    public static CliArtifacts Of(TestTarget.Executable executable) =>
        new(executable, new Dictionary<MorphirHost, string> { [executable.Host] = executable.Path }, configuration: null);
}

/// <summary>The CLI driver for a host that is one file: an assembly, which it starts with <c>dotnet</c>, or an executable.</summary>
public sealed class CliDriver : ICliDriver
{
    /// <summary>The time limit of a command with no other limit.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private readonly string _artifact;
    private readonly IScenarioWorkspace _workspace;
    private readonly ProcessGate? _gate;

    /// <param name="artifact">The file of the host, from <see cref="CliArtifacts.PathOf"/>.</param>
    /// <param name="gate">The process gate of each command. The default is <see cref="ProcessGate.Shared"/>.</param>
    public CliDriver(TestTarget target, MorphirHost host, string artifact, IScenarioWorkspace workspace, ProcessGate? gate = null)
    {
        Target = target;
        Host = host;
        _artifact = artifact;
        _workspace = workspace;
        _gate = gate;
    }

    public TestTarget Target { get; }

    public MorphirHost Host { get; }

    private bool IsAssembly => _artifact.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    public Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default)
    {
        // The built assembly starts in 0.02 s with dotnet. dotnet run takes 0.35 s for an MSBuild evaluation.
        var info = new ProcessStartInfo(IsAssembly ? DotnetHost.Path : _artifact);
        if (options?.WithoutDynamicCode == true)
        {
            if (!IsAssembly) throw new NotSupportedException($"The host {_artifact} is not an assembly, so it cannot run with the dynamic-code switch off.");
            foreach (var argument in new[] { "exec", "--runtimeconfig", DynamicCode.RuntimeConfigurationWithoutDynamicCode(_artifact, _workspace) })
                info.ArgumentList.Add(argument);
        }

        if (IsAssembly) info.ArgumentList.Add(_artifact);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return RunAsync(info, CommandLineOf(info), options, cancellation);
    }

    public Task<CommandResult> RunCommandLineAsync(string commandLine, CliRunOptions? options = null, CancellationToken cancellation = default)
    {
        if (options?.WithoutDynamicCode == true)
            throw new NotSupportedException("A command line that is one string cannot run with the dynamic-code switch off. Give each argument apart (RunAsync).");
        var info = new ProcessStartInfo(IsAssembly ? DotnetHost.Path : _artifact)
        {
            Arguments = IsAssembly ? $"\"{_artifact}\" {commandLine}" : commandLine,
        };
        return RunAsync(info, $"{Quoted(info.FileName)} {info.Arguments}", options, cancellation);
    }

    private Task<CommandResult> RunAsync(ProcessStartInfo info, string commandLine, CliRunOptions? options, CancellationToken cancellation) =>
        ProductProcess.RunAsync(info, commandLine, _workspace, _gate, options, DefaultTimeout, cancellation);

    internal static string CommandLineOf(ProcessStartInfo info) =>
        string.Join(' ', new[] { info.FileName }.Concat(info.ArgumentList).Select(Quoted));

    private static string Quoted(string argument) =>
        argument.Length > 0 && !argument.Any(char.IsWhiteSpace) && !argument.Contains('"') ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
}

/// <summary>Runs one product process in a workspace: the one way in which a driver starts a program of the product.</summary>
internal static class ProductProcess
{
    private static readonly System.Text.UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<CommandResult> RunAsync(
        ProcessStartInfo info, string commandLine, IScenarioWorkspace workspace, ProcessGate? gate, CliRunOptions? options,
        TimeSpan defaultTimeout, CancellationToken cancellation)
    {
        // On Linux the runner starts the child through setsid, which looks up its program in the PATH of the child,
        // and a scenario can give the product a PATH with no dotnet: start the dotnet host by its full path.
        if (info.FileName == "dotnet") info.FileName = DotnetHost.Path;
        workspace.Contain(info);
        if (options?.Environment is { } environment)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null) info.Environment.Remove(name);
                else info.Environment[name] = value;
            }
        }

        if (options?.WorkingDirectory is { } directory) info.WorkingDirectory = directory;

        // The product writes UTF-8 on each operating system. Without this, Windows reads a redirected stream in the
        // code page of the console, and a character such as ✓ or a box line arrives as another character.
        info.StandardOutputEncoding = Utf8;
        info.StandardErrorEncoding = Utf8;

        var mark = workspace.Mark();
        // The runner holds a place of the gate, reads both streams, and stops the process tree at the time limit.
        var result = await ChildProcess.RunAsync(
            info, options?.Timeout ?? defaultTimeout, cancellation, gate, options?.StandardInput).ConfigureAwait(false);
        return new CommandResult(
            commandLine,
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.ExitedAt - result.StartedAt,
            workspace.FilesWrittenSince(mark));
    }
}
