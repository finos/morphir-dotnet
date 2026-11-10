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
    public static async Task<bool> EndsWithinAsync(int pid, TimeSpan timeout)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return true;
        }

        using (process)
        {
            using var wait = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(wait.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return false;
            }
        }
    }

    public async ValueTask DisposeAsync() => await Sandbox.DisposeAsync();
}
