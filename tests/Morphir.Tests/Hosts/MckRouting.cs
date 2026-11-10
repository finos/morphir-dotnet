// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Morphir.TestSupport;

namespace Morphir.Tests.Hosts;

/// <summary>What the stub morphir-mck (tests/Morphir.Mck.Stub) reported on its standard output.</summary>
public sealed record MckStubReport(IReadOnlyList<string> Arguments, string WorkingDirectory, string? StandardInput)
{
    public static MckStubReport Parse(string standardOutput)
    {
        using var json = JsonDocument.Parse(standardOutput);
        var root = json.RootElement;
        return new MckStubReport(
            [.. root.GetProperty("args").EnumerateArray().Select(argument => argument.GetString()!)],
            root.GetProperty("cwd").GetString()!,
            root.GetProperty("stdin").GetString());
    }
}

/// <summary>
/// A sandbox for routing tests: a <c>tools</c> folder with a copy of the stub morphir-mck, and start infos for an
/// entry point's produced executable (its apphost, not <c>dotnet exec</c>) and for the stub itself. Children run in
/// <see cref="TestSandbox.Work"/>, with PATH set to the folders a test chooses, and the runtime found through
/// DOTNET_ROOT. The test output folder, which is the entry point's own folder, also holds a stub, so a test that
/// leaves it off PATH also checks that the executable's folder is not searched.
/// </summary>
public sealed class MckRouting : IAsyncDisposable
{
    public static readonly string Suffix = OperatingSystem.IsWindows() ? ".exe" : "";

    private static readonly string[] StubFiles =
        ["morphir-mck" + Suffix, "morphir-mck.dll", "morphir-mck.runtimeconfig.json", "morphir-mck.deps.json"];

    private MckRouting(TestSandbox sandbox)
    {
        Sandbox = sandbox;
        Tools = Directory.CreateDirectory(sandbox.PathOf("tools")).FullName;
        Empty = Directory.CreateDirectory(sandbox.PathOf("empty")).FullName;
        Stub = CopyStub(Tools);
    }

    public TestSandbox Sandbox { get; }

    /// <summary>A folder with the stub.</summary>
    public string Tools { get; }

    /// <summary>An empty folder, for a PATH with no tool.</summary>
    public string Empty { get; }

    /// <summary>The stub's executable in <see cref="Tools"/>.</summary>
    public string Stub { get; }

    public static MckRouting Create() => new(TestSandbox.Create("morphir-mck-routing-"));

    /// <summary>Copies the stub into <paramref name="directory"/> as <paramref name="name"/>, and returns its executable.</summary>
    public static string CopyStub(string directory, string name = "morphir-mck")
    {
        Directory.CreateDirectory(directory);
        foreach (var file in StubFiles)
        {
            var source = Path.Combine(AppContext.BaseDirectory, file);
            if (File.Exists(source)) File.Copy(source, Path.Combine(directory, file.Replace("morphir-mck", name)), overwrite: true);
        }

        var executable = Path.Combine(directory, name + Suffix);
        if (!File.Exists(executable)) throw new FileNotFoundException("The stub morphir-mck was not built into the test output.", executable);
        return executable;
    }

    /// <summary>The produced executable of an entry point in the test output folder, with <paramref name="arguments"/>.</summary>
    public ProcessStartInfo EntryPoint(string name, string path, params string[] arguments) =>
        Contained(ChildProcess.StartInfo(Path.Combine(AppContext.BaseDirectory, name + Suffix), arguments), path);

    /// <summary>The stub itself, as a user would run the standalone tool.</summary>
    public ProcessStartInfo Standalone(string path, params string[] arguments) =>
        Contained(ChildProcess.StartInfo(Stub, arguments), path);

    private ProcessStartInfo Contained(ProcessStartInfo start, string path)
    {
        start.WorkingDirectory = Sandbox.Work;
        Sandbox.Apply(start);
        start.Environment["PATH"] = path;
        start.Environment["DOTNET_ROOT"] = DotnetRoot;
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("MCK_STUB_", StringComparison.Ordinal)).ToArray())
            start.Environment.Remove(key);
        return start;
    }

    /// <summary>The root of the runtime that runs this test, for the apphosts.</summary>
    public static string DotnetRoot { get; } =
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));

    /// <summary>
    /// Waits for the stub to write the process ids of itself and its grandchild to <paramref name="file"/>.
    /// </summary>
    public static async Task<(int Tool, int Grandchild)> WaitForPidsAsync(string file, CancellationToken cancellationToken)
    {
        while (!File.Exists(file)) await Task.Delay(100, cancellationToken);
        var pids = (await File.ReadAllTextAsync(file, cancellationToken)).Split(' ');
        return (int.Parse(pids[0]), int.Parse(pids[1]));
    }

    /// <summary>Whether the process with <paramref name="pid"/> ends within <paramref name="timeout"/>.</summary>
    public static async Task<bool> EndsWithinAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        (await ObserveEndAsync(pid, timeout, cancellationToken)).Ended;

    /// <summary>
    /// Whether the process with <paramref name="pid"/> ends within <paramref name="timeout"/>, and, when it does not,
    /// what the operating system reports about it and its ancestors (state, parent, command), taken before it is
    /// killed for cleanup.
    /// </summary>
    public static async Task<(bool Ended, string Evidence)> ObserveEndAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return (true, $"pid {pid}: not found");
        }

        using (process)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(wait.Token);
                return (true, $"pid {pid}: exited");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var evidence = await DescribeAsync(pid, cancellationToken);
                process.Kill(entireProcessTree: true);
                return (false, evidence);
            }
            catch (OperationCanceledException)
            {
                // The caller's deadline: stop the process, then let the cancellation through.
                process.Kill(entireProcessTree: true);
                throw;
            }
        }
    }

    /// <summary>
    /// The process and its ancestors as the operating system reports them: on Linux from <c>/proc</c>, on macOS from
    /// <c>/bin/ps</c>. Each entry has the process id, its state (Z is a zombie: exited, not yet reaped by its parent),
    /// its parent and its command.
    /// </summary>
    public static async Task<string> DescribeAsync(int pid, CancellationToken cancellationToken = default)
    {
        var entries = new List<string>();
        var current = pid;
        for (var depth = 0; depth < 16 && current > 0; depth++)
        {
            var (entry, parent) = OperatingSystem.IsLinux() ? DescribeLinux(current)
                : OperatingSystem.IsMacOS() ? await DescribePsAsync(current, cancellationToken)
                : ($"pid {current}: still running", 0);
            entries.Add(entry);
            if (parent <= 0 || current == 1) break;
            current = parent;
        }

        return string.Join(" <- ", entries);
    }

    private static (string Entry, int Parent) DescribeLinux(int pid)
    {
        string stat;
        try
        {
            stat = File.ReadAllText($"/proc/{pid}/stat");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ($"pid {pid}: no /proc entry ({exception.GetType().Name})", 0);
        }

        // comm is in parentheses and can hold spaces; the fields after it start with state and ppid.
        var close = stat.LastIndexOf(')');
        var comm = stat[(stat.IndexOf('(') + 1)..close];
        var fields = stat[(close + 2)..].Split(' ');
        var parent = int.Parse(fields[1]);
        string command, descriptors;
        try
        {
            command = File.ReadAllText($"/proc/{pid}/cmdline").Replace('\0', ' ').Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            command = "?";
        }

        try
        {
            descriptors = Directory.GetFileSystemEntries($"/proc/{pid}/fd").Length.ToString();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            descriptors = "?";
        }

        if (command.Length > 160) command = command[..160] + "...";
        return ($"pid {pid} state {fields[0]} ppid {parent} fds {descriptors} comm {comm} cmd [{command}]", parent);
    }

    /// <summary>One entry of <see cref="DescribeAsync"/> from <c>/bin/ps</c>, as on macOS, which has no <c>/proc</c>.</summary>
    public static async Task<(string Entry, int Parent)> DescribePsAsync(int pid, CancellationToken cancellationToken = default)
    {
        var result = await RunDiagnosticAsync(ChildProcess.StartInfo("/bin/ps", "-o", "pid=,ppid=,stat=,command=", "-p", pid.ToString()),
            cancellationToken);
        var line = result.StandardOutput.Trim();
        if (line.Length == 0) return ($"pid {pid}: not listed by ps", 0);
        var parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        var parent = parts.Length > 1 && int.TryParse(parts[1], out var ppid) ? ppid : 0;
        if (line.Length > 200) line = line[..200] + "...";
        return ($"ps [{line}]", parent);
    }

    /// <summary>
    /// The gate of diagnostic children: one at a time, apart from <see cref="ProcessGate.Shared"/>, whose slots the
    /// routing tests hold while they describe a process.
    /// </summary>
    private static readonly ProcessGate DiagnosticGate = new(1);

    /// <summary>Holds the diagnostic slot until disposed, for tests of the cancellation of a waiting diagnostic.</summary>
    public static Task<IDisposable> HoldDiagnosticSlotAsync() => DiagnosticGate.EnterAsync();

    /// <summary>Runs a short diagnostic child, such as <c>/bin/ps</c>, for the description of a process.</summary>
    public static Task<ChildProcessResult> RunDiagnosticAsync(ProcessStartInfo start, CancellationToken cancellationToken) =>
        ChildProcess.RunAsync(start, TimeSpan.FromSeconds(10), cancellationToken, DiagnosticGate);

    public async ValueTask DisposeAsync() => await Sandbox.DisposeAsync();
}
