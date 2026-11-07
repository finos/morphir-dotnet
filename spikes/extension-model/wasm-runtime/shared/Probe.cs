// Shared harness for the WASM runtime spike. Each probe project supplies one
// IGuestRuntime. The harness loads guest.wasm (a morphir-bytes-v1 guest), calls
// handle and checks the limits. Results go to stdout, one check per line.
using System.Diagnostics;
using System.Text;

namespace Spike;

/// <summary>The limits that the host applies to one guest instance.</summary>
/// <param name="MemoryBytes">The memory ceiling, in bytes.</param>
/// <param name="Fuel">The fuel (instruction budget) for each call, or null for none.</param>
/// <param name="Deadline">The wall-clock deadline for each call, or null for none.</param>
public sealed record Limits(long MemoryBytes, ulong? Fuel, TimeSpan? Deadline);

/// <summary>One instantiated morphir-bytes-v1 guest.</summary>
public interface IBytesGuest : IDisposable
{
    /// <summary>Calls handle with the input. Returns the result of handle and the bytes from output_byte.</summary>
    int Handle(byte[] input, out byte[] output);
}

/// <summary>A WASM runtime under test.</summary>
public interface IGuestRuntime
{
    string Name { get; }
    /// <summary>Compiles and instantiates the module with the limits.</summary>
    IBytesGuest Load(byte[] wasm, Limits limits);
}

/// <summary>A guest fault, mapped from the runtime's own exception.</summary>
public sealed class GuestFaultException(string kind, Exception inner) : Exception(kind, inner);

/// <summary>Thrown by a runtime that cannot apply a limit.</summary>
public sealed class LimitNotSupportedException(string message) : Exception(message);

/// <summary>The byte exchange of one call, shared by the three host imports.</summary>
public sealed class Exchange
{
    private byte[] _input = [];
    private readonly MemoryStream _output = new();

    public void Begin(byte[] input) { _input = input; _output.SetLength(0); }
    public byte[] End() => _output.ToArray();

    public int InputLen() => _input.Length;

    public int InputByte(int index) =>
        index >= 0 && index < _input.Length
            ? _input[index]
            : throw new InvalidOperationException($"input_byte index {index} out of range");

    public void OutputByte(int value)
    {
        if (value is < 0 or > 255) throw new InvalidOperationException($"output_byte value {value} is not a byte");
        _output.WriteByte((byte)value);
    }
}

public static class Probe
{
    public static readonly long MemoryCeiling =
        long.TryParse(Environment.GetEnvironmentVariable("SPIKE_MEMORY_BYTES"), out var m) ? m : 256L * 1024 * 1024;
    public const ulong FuelBudget = 100_000_000;
    public static readonly TimeSpan SpikeDeadline = TimeSpan.FromSeconds(2);

    public static int Run(IGuestRuntime runtime, string[] args)
    {
        var processStart = Process.GetCurrentProcess().StartTime;
        var mainEntered = DateTime.Now;
        var path = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "guest.wasm");
        var wasm = File.ReadAllBytes(path);
        var failures = 0;

        void Report(string check, bool ok, string detail)
        {
            if (!ok) failures++;
            Console.WriteLine($"{runtime.Name}\t{check}\t{(ok ? "ok" : "FAIL")}\t{detail}");
        }

        // 1. Cold start: engine, compile, instantiate and one echo call.
        // The limits are the PRD limits: 256 MiB, fuel and a 30 s deadline.
        const string request = """{"jsonrpc":"2.0","id":1,"method":"morphir.extension.describe"}""";
        var full = new Limits(MemoryCeiling, FuelBudget, TimeSpan.FromSeconds(30));
        var sw = Stopwatch.StartNew();
        using (var guest = runtime.Load(wasm, full))
        {
            var loaded = sw.Elapsed;
            var rc = guest.Handle(Encoding.UTF8.GetBytes(request), out var output);
            var first = sw.Elapsed;
            var text = Encoding.UTF8.GetString(output);
            Report("echo", rc == request.Length && text == request,
                $"rc={rc} out={output.Length}B load={loaded.TotalMilliseconds:F2}ms load+call={first.TotalMilliseconds:F2}ms " +
                $"processStart->firstResponse={(DateTime.Now - processStart).TotalMilliseconds:F0}ms " +
                $"processStart->main={(mainEntered - processStart).TotalMilliseconds:F0}ms");

            if (Environment.GetEnvironmentVariable("SPIKE_ONLY_ECHO") == "1")
            {
                // Cold start runs: one call, then stop.
                Console.Out.Flush();
                Environment.Exit(failures == 0 ? 0 : 1);
            }

            sw.Restart();
            rc = guest.Handle(Encoding.UTF8.GetBytes(request), out _);
            Report("warm-call", rc == request.Length, $"rc={rc} {sw.Elapsed.TotalMilliseconds:F3}ms");

            // Throughput: the ABI moves one byte per host call, so a 1 MiB echo
            // makes about 2 million host calls.
            var big = new byte[1024 * 1024];
            Array.Fill(big, (byte)'a');
            sw.Restart();
            rc = guest.Handle(big, out output);
            Report("echo-1MiB", rc == big.Length && output.Length == big.Length, $"rc={rc} {sw.Elapsed.TotalMilliseconds:F1}ms");

            // 2. Memory below the ceiling grows.
            rc = guest.Handle("+"u8.ToArray(), out output);
            Report("memory-below-ceiling", rc == 1 && output is [(byte)'Y'], $"rc={rc} out={Encoding.ASCII.GetString(output)}");

            // 3. Exit.
            rc = guest.Handle([], out _);
            Report("exit", rc == 0, $"rc={rc}");
        }

        // 4. Memory above the ceiling is refused. A fresh instance, because some
        // runtimes trap on a refused growth instead of returning -1 from memory.grow.
        // Upstream fails the call in both cases (extension.memory_limit).
        using (var guest = runtime.Load(wasm, full))
        {
            try
            {
                var rc = guest.Handle("^"u8.ToArray(), out var output);
                Report("memory-above-ceiling", rc == 1 && output is [(byte)'N'],
                    $"rc={rc} out={Encoding.ASCII.GetString(output)} (N = memory.grow returned -1)");
            }
            catch (GuestFaultException e)
            {
                Report("memory-above-ceiling", true, $"refused as a fault: {e.Message}");
            }
        }

        // 5. Fuel: an endless loop with a fuel budget and no deadline.
        Report("fuel", Stops(runtime, wasm, new Limits(MemoryCeiling, FuelBudget, null), out var fuelDetail), fuelDetail);

        // 6. Deadline: an endless loop with a deadline and no fuel.
        Report("deadline", Stops(runtime, wasm, new Limits(MemoryCeiling, null, SpikeDeadline), out var deadlineDetail), deadlineDetail);

        Console.Out.Flush();
        // A runtime that cannot stop the loop leaves a busy thread; end the process.
        Environment.Exit(failures == 0 ? 0 : 1);
        return 0;
    }

    private static bool Stops(IGuestRuntime runtime, byte[] wasm, Limits limits, out string detail)
    {
        IBytesGuest guest;
        try
        {
            guest = runtime.Load(wasm, limits);
        }
        catch (LimitNotSupportedException e)
        {
            detail = $"not supported: {e.Message}";
            return false;
        }

        Exception? error = null;
        int? rc = null;
        var sw = Stopwatch.StartNew();
        var thread = new Thread(() =>
        {
            try { rc = guest.Handle("!"u8.ToArray(), out _); }
            catch (Exception e) { error = e; }
        }, 16 * 1024 * 1024) { IsBackground = true };
        thread.Start();
        var watchdog = TimeSpan.FromSeconds(20);
        if (!thread.Join(watchdog))
        {
            detail = $"not stopped after {watchdog.TotalSeconds:F0}s (watchdog)";
            return false;
        }
        guest.Dispose();
        var message = error switch
        {
            null => $"returned rc={rc}",
            GuestFaultException f => $"fault {f.Message} ({f.InnerException?.GetType().Name})",
            _ => $"{error.GetType().Name}: {FirstLine(error.Message)}",
        };
        detail = $"stopped after {sw.Elapsed.TotalMilliseconds:F0}ms: {message}";
        return error is not null;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 160 ? line[..160] + "..." : line;
    }
}
