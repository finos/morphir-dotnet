using System.Diagnostics;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Infrastructure;

/// <summary>The bounded child-process runner. Every run holds a slot of the shared process gate.</summary>
public sealed class BoundedProcessTests
{
    private static readonly string Dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    private static async Task<ProcessRun> Run(ProcessStartInfo startInfo, ProcessLimits limits, CancellationToken ct = default)
    {
        using var slot = await ProcessGate.Shared.EnterAsync(ct);
        return await BoundedProcess.RunAsync(startInfo, limits, ct);
    }

    private static ProcessStartInfo Sleep() => OperatingSystem.IsWindows()
        ? ChildProcess.StartInfo("ping.exe", "-n", "60", "127.0.0.1")
        : ChildProcess.StartInfo("sleep", "60");

    [Test]
    public async Task ExitedRunKeepsItsOutputAndExitCode()
    {
        var run = await Run(ChildProcess.StartInfo(Dotnet, "--version"), new(TimeSpan.FromMinutes(1), 1024 * 1024));

        var exited = run.Should().BeOfType<ProcessRun.Exited>().Subject;
        exited.ExitCode.Should().Be(0);
        exited.StandardOutput.Trim().Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    [Test]
    public async Task DeadlineStopsTheProcess()
    {
        var startInfo = Sleep();
        var stopwatch = Stopwatch.StartNew();

        var run = await Run(startInfo, new(TimeSpan.FromMilliseconds(200), 1024 * 1024));

        run.Should().BeOfType<ProcessRun.DeadlineExceeded>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task OutputBudgetStopsTheProcess()
    {
        var run = await Run(ChildProcess.StartInfo(Dotnet, "--info"), new(TimeSpan.FromMinutes(1), 16));

        run.Should().BeOfType<ProcessRun.OutputLimitExceeded>().Which.MaxOutputBytes.Should().Be(16);
    }

    [Test]
    public async Task MissingExecutableDoesNotStart()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-tool");

        var run = await Run(ChildProcess.StartInfo(missing), new(TimeSpan.FromMinutes(1), 1024));

        run.Should().BeOfType<ProcessRun.NotStarted>();
    }

    [Test]
    public async Task CancellationStopsTheProcessAndThrows()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var run = () => Run(Sleep(), new(TimeSpan.FromMinutes(1), 1024), cancellation.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }
}
