// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// Spike of the win-x64 trap diagnosis (ci/wasm-win-x64-trap, issue #563). See README.md.
//
//   TrapRepro info                                   what this process is: architecture, executable, CET policy
//   TrapRepro case <name> [--signals-based-traps false]   one trap in this process; prints how it ended
//   TrapRepro cases                                  the case names
//   TrapRepro bench [--signals-based-traps false]   time a memory-heavy guest loop
//   TrapRepro pe <file>...                           the CET and guard marks of PE files
//   TrapRepro pe-cet <file> on|off                   set or clear the CET-compatible bit of a PE file in place
//   TrapRepro dump <file.dmp>                        the exception record of a minidump, with the faulting module
//
// Exit codes of 'case': 0 the guest trapped and the host got an exception (or the control case completed), 3 a trap case
// completed, 4 an unexpected exception. A process that dies has the system's status instead (0xC0000409 = -1073740791).
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Morphir.Extensions.Wasm;
using Wasmtime;

namespace TrapRepro;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: TrapRepro info | cases | case <name> [--signals-based-traps false] | bench [...] | pe <file>... | pe-cet <file> on|off | dump <file>");
            return 64;
        }

        var signalsBasedTraps = !HasOption(args, "--signals-based-traps", "false");
        switch (args[0])
        {
            case "info":
                Info.Print();
                return 0;
            case "cases":
                foreach (var name in Cases.Names)
                {
                    Console.WriteLine(name);
                }

                return 0;
            case "case" when args.Length >= 2:
                Info.Print();
                return Cases.Run(args[1], signalsBasedTraps);
            case "bench":
                return Cases.Bench(signalsBasedTraps);
            case "pe" when args.Length >= 2:
                foreach (var file in args.Skip(1))
                {
                    Console.WriteLine(PeFile.Describe(file));
                }

                return 0;
            case "pe-cet" when args.Length == 3:
                return PeFile.SetCetCompat(args[1], args[2] == "on");
            case "dump" when args.Length == 2:
                return Minidump.Describe(args[1]);
            default:
                Console.Error.WriteLine($"unknown command: {string.Join(' ', args)}");
                return 64;
        }
    }

    static bool HasOption(string[] args, string name, string value)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == name && args[i + 1] == value)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>What this process is, printed before every case so a crash's output still says it.</summary>
static unsafe class Info
{
    const int ProcessUserShadowStackPolicy = 15;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern int GetProcessMitigationPolicy(nint process, int policy, uint* buffer, nuint length);

    [DllImport("kernel32.dll")]
    static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    static extern int IsUserCetAvailableInEnvironment(uint environment);

    public static void Print()
    {
        Console.WriteLine($"process: {Environment.ProcessPath}");
        Console.WriteLine($"architecture: {RuntimeInformation.ProcessArchitecture}; os: {RuntimeInformation.OSDescription}; runtime: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"native aot: {!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");
        if (!OperatingSystem.IsWindows())
        {
            Console.Out.Flush();
            return;
        }

        string available;
        try
        {
            available = IsUserCetAvailableInEnvironment(0) != 0 ? "yes" : "no";
        }
        catch (EntryPointNotFoundException)
        {
            available = "unknown (no IsUserCetAvailableInEnvironment)";
        }

        Console.WriteLine($"user CET available in this environment: {available}");
        var buffer = stackalloc uint[1];
        var read = GetProcessMitigationPolicy(GetCurrentProcess(), ProcessUserShadowStackPolicy, buffer, sizeof(uint));
        var flags = buffer[0];
        if (read == 0)
        {
            Console.WriteLine($"user shadow stack policy: unreadable (error {Marshal.GetLastPInvokeError()})");
        }
        else
        {
            string[] names =
            [
                "EnableUserShadowStack", "AuditUserShadowStack", "SetContextIpValidation", "AuditSetContextIpValidation",
                "EnableUserShadowStackStrictMode", "BlockNonCetBinaries", "BlockNonCetBinariesNonEhcont",
                "AuditBlockNonCetBinaries", "CetDynamicApisOutOfProcOnly", "SetContextIpValidationRelaxedMode",
            ];
            var on = names.Where((_, bit) => (flags & (1u << bit)) != 0).ToArray();
            Console.WriteLine($"user shadow stack policy: 0x{flags:X8} [{string.Join(", ", on)}]");
        }

        Console.Out.Flush();
    }
}

static class Cases
{
    /// <summary>Each case: its module text, and whether a trap is expected.</summary>
    static readonly (string Name, string Kind, string Text)[] Wat =
    [
        ("no-trap", "control", """(module (func (export "run")))"""),
        ("unreachable", "hardware: ud2", """(module (func (export "run") unreachable))"""),
        ("oob-load", "hardware: guard page access violation",
            """(module (memory 1) (func (export "run") (drop (i32.load (i32.const 0x7ffffff0)))))"""),
        ("div-zero", "hardware: divide error",
            """(module (global $z (mut i32) (i32.const 0)) (func (export "run") (drop (i32.div_u (i32.const 1) (global.get $z)))))"""),
        ("int-overflow", "hardware: divide error (INT_MIN / -1)",
            """(module (global $m (mut i32) (i32.const -1)) (func (export "run") (drop (i32.div_s (i32.const 0x80000000) (global.get $m)))))"""),
        ("call-indirect-null", "hardware: null funcref",
            """(module (type $t (func)) (table 1 funcref) (func (export "run") (call_indirect (type $t) (i32.const 0))))"""),
        ("stack-overflow", "hardware: stack limit check",
            """(module (func $f (export "run") (call $f)))"""),
        ("host-throws", "host: a host function throws",
            """(module (import "env" "boom" (func $boom)) (func (export "run") (call $boom)))"""),
        ("fuel", "host: fuel exhausted", """(module (func (export "run") (loop $l (br $l))))"""),
        ("epoch-interrupt", "host: epoch deadline without a callback", """(module (func (export "run") (loop $l (br $l))))"""),
        ("epoch-callback-throws", "host: the epoch callback throws", """(module (func (export "run") (loop $l (br $l))))"""),
    ];

    public static IEnumerable<string> Names =>
        Wat.Select(c => c.Name).Concat(["product-trap", "product-grow-then-trap"]);

    public static int Run(string name, bool signalsBasedTraps)
    {
        Console.WriteLine($"case: {name}; signals-based traps: {(signalsBasedTraps ? "on" : "off")}");
        if (name.StartsWith("product-", StringComparison.Ordinal))
        {
            return RunProduct(name, signalsBasedTraps);
        }

        var match = Wat.Where(c => c.Name == name).ToArray();
        if (match.Length == 0)
        {
            Console.Error.WriteLine($"unknown case {name}");
            return 64;
        }

        var (_, kind, text) = match[0];
        Console.WriteLine($"kind: {kind}");
        var config = new Config()
            .WithFuelConsumption(name == "fuel")
            .WithEpochInterruption(name.StartsWith("epoch-", StringComparison.Ordinal));
        if (!signalsBasedTraps)
        {
            config = config.WithSignalsBasedTraps(false);
        }

        using var engine = new Engine(config);
        using var module = Module.FromText(engine, name, text);
        using var store = new Store(engine);
        using var linker = new Linker(engine);
        Action boom = () => throw new InvalidOperationException("the host function stopped the call");
        if (name == "host-throws")
        {
            linker.DefineFunction("env", "boom", boom);
        }

        if (name == "fuel")
        {
            store.Fuel = 1_000_000;
        }

        Thread? ticker = null;
        if (name.StartsWith("epoch-", StringComparison.Ordinal))
        {
            store.SetEpochDeadline(1);
            if (name == "epoch-callback-throws")
            {
                store.SetEpochDeadlineCallback(_ => throw new TimeoutException("the epoch callback stopped the call"));
            }

            ticker = new Thread(() =>
            {
                Thread.Sleep(50);
                engine.IncrementEpoch();
            }) { IsBackground = true };
        }

        var instance = linker.Instantiate(store, module);
        var run = instance.GetFunction("run") ?? throw new InvalidOperationException("no run export");
        Console.WriteLine("calling the guest");
        Console.Out.Flush();
        ticker?.Start();
        try
        {
            run.Invoke();
            Console.WriteLine("result: completed");
            return name == "no-trap" ? 0 : 3;
        }
        catch (TrapException trap)
        {
            Console.WriteLine($"result: TrapException {trap.Type}: {FirstLine(trap.Message)}");
            return 0;
        }
        catch (WasmtimeException failure)
        {
            Console.WriteLine($"result: {failure.GetType().Name} (inner {failure.InnerException?.GetType().Name ?? "none"}): {FirstLine(failure.Message)}");
            return 0;
        }
        catch (Exception other)
        {
            Console.WriteLine($"result: unexpected {other.GetType().Name}: {FirstLine(other.Message)}");
            return 4;
        }
    }

    /// <summary>The product's own path: WasmEngine, classification and SessionStore.CallHandle on a corpus module.</summary>
    static int RunProduct(string name, bool signalsBasedTraps)
    {
        if (!signalsBasedTraps)
        {
            Environment.SetEnvironmentVariable("MORPHIR_WASM_EXPERIMENT_SIGNALS_BASED_TRAPS", "false");
        }

        var resource = name == "product-trap" ? "extism-trap.wasm" : "extism-grow-then-trap.wasm";
        Console.WriteLine($"kind: product SessionStore.CallHandle on {resource}");
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"no resource {resource}");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var guest = WasmEngine.Shared.Load(buffer.ToArray()).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(
                admitted => admitted,
                refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            invalid => throw new InvalidOperationException(invalid));
        var limits = SessionLimits.Default with { MemoryCeilingBytes = 16 * 1024 * 1024 };
        using var session = SessionStore.Open(guest, limits).Match(
            opened => opened,
            failure => throw new InvalidOperationException(failure.Message));
        Console.WriteLine("calling the guest");
        Console.Out.Flush();
        var outcome = session.CallHandle("{}"u8);
        var text = outcome.Match(
            completed => $"completed with return code {completed.Result.ReturnCode}",
            failed => $"failed {failed.Kind} (signal {failed.Signal}): {failed.Message}",
            notRun => $"not run: {notRun.Message}");
        Console.WriteLine($"result: {text}");
        return outcome is HandleOutcome.Failed ? 0 : 3;
    }

    /// <summary>A memory-heavy loop with the product's fuel and epoch settings, to price explicit trap checks.</summary>
    public static int Bench(bool signalsBasedTraps)
    {
        const string text = """
            (module (memory 16)
              (func (export "run") (param $n i32) (result i32)
                (local $i i32) (local $acc i32)
                (loop $l
                  (i32.store (i32.and (i32.mul (local.get $i) (i32.const 4)) (i32.const 0xffffc)) (local.get $acc))
                  (local.set $acc (i32.add (local.get $acc)
                    (i32.load (i32.and (i32.mul (local.get $i) (i32.const 8)) (i32.const 0xffffc)))))
                  (local.set $acc (i32.add (local.get $acc)
                    (i32.div_u (local.get $i) (i32.or (local.get $acc) (i32.const 1)))))
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br_if $l (i32.lt_u (local.get $i) (local.get $n))))
                (local.get $acc)))
            """;
        var config = new Config().WithFuelConsumption(true).WithEpochInterruption(true);
        if (!signalsBasedTraps)
        {
            config = config.WithSignalsBasedTraps(false);
        }

        using var engine = new Engine(config);
        using var module = Module.FromText(engine, "bench", text);
        const int iterations = 100_000_000;
        var best = TimeSpan.MaxValue;
        object? result = null;
        for (var round = 0; round < 5; round++)
        {
            using var store = new Store(engine);
            store.Fuel = ulong.MaxValue / 4;
            store.SetEpochDeadline(ulong.MaxValue / 4);
            using var linker = new Linker(engine);
            var run = linker.Instantiate(store, module).GetFunction("run")!;
            var clock = Stopwatch.StartNew();
            result = run.Invoke(iterations);
            clock.Stop();
            if (clock.Elapsed < best)
            {
                best = clock.Elapsed;
            }
        }

        Console.WriteLine($"bench: signals-based traps {(signalsBasedTraps ? "on" : "off")}; {iterations} iterations; best of 5 {best.TotalMilliseconds:0} ms; result {result}");
        return 0;
    }

    static string FirstLine(string message) => message.Split('\n', 2)[0].TrimEnd('\r');
}

/// <summary>The marks of a PE file that decide CET: the extended DLL characteristics in the debug directory, and the
/// load configuration's guard flags and EH continuation table.</summary>
static class PeFile
{
    const int DebugTypeExDllCharacteristics = 20;

    public static string Describe(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var pe = Parse(bytes);
        var builder = new StringBuilder();
        builder.Append($"{path}: machine 0x{pe.Machine:X4}; DllCharacteristics 0x{pe.DllCharacteristics:X4}");
        builder.Append(pe.ExDllCharacteristics is { } ex
            ? $"; ExDllCharacteristics 0x{ex:X8} (CET compatible {(ex & 1) != 0}, strict {(ex & 2) != 0}, relaxed context IP {(ex & 4) != 0}, dynamic APIs in process {(ex & 8) != 0})"
            : "; no ExDllCharacteristics entry (not CET compatible)");
        builder.Append(pe.GuardFlags is { } guard ? $"; GuardFlags 0x{guard:X8} (CF instrumented {(guard & 0x100) != 0}, EH continuation table {(guard & 0x400000) != 0})" : "; no load configuration guard flags");
        builder.Append(pe.EhContinuationCount is { } count ? $"; EH continuation targets {count}" : "; no EH continuation count");
        return builder.ToString();
    }

    public static int SetCetCompat(string path, bool on)
    {
        var bytes = File.ReadAllBytes(path);
        var pe = Parse(bytes);
        if (pe.ExDllCharacteristicsOffset is not { } offset)
        {
            Console.WriteLine($"{path}: no ExDllCharacteristics debug entry to change");
            return 2;
        }

        var value = BitConverter.ToUInt32(bytes, offset);
        var changed = on ? value | 1u : value & ~1u;
        BitConverter.GetBytes(changed).CopyTo(bytes, offset);
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"{path}: ExDllCharacteristics 0x{value:X8} -> 0x{changed:X8}");
        return 0;
    }

    record Parsed(ushort Machine, ushort DllCharacteristics, uint? ExDllCharacteristics, int? ExDllCharacteristicsOffset, uint? GuardFlags, ulong? EhContinuationCount);

    static Parsed Parse(byte[] b)
    {
        var peOffset = BitConverter.ToInt32(b, 0x3C);
        if (BitConverter.ToUInt32(b, peOffset) != 0x00004550)
        {
            throw new InvalidDataException("not a PE file");
        }

        var fileHeader = peOffset + 4;
        var machine = BitConverter.ToUInt16(b, fileHeader);
        var sections = BitConverter.ToUInt16(b, fileHeader + 2);
        var optionalSize = BitConverter.ToUInt16(b, fileHeader + 16);
        var optional = fileHeader + 20;
        var magic = BitConverter.ToUInt16(b, optional);
        var pe64 = magic == 0x20B;
        var dllCharacteristics = BitConverter.ToUInt16(b, optional + 70);
        var directories = optional + (pe64 ? 112 : 96);
        var sectionTable = optional + optionalSize;

        int? FileOffset(uint rva)
        {
            for (var i = 0; i < sections; i++)
            {
                var s = sectionTable + i * 40;
                var virtualSize = BitConverter.ToUInt32(b, s + 8);
                var virtualAddress = BitConverter.ToUInt32(b, s + 12);
                var rawSize = BitConverter.ToUInt32(b, s + 16);
                var rawPointer = BitConverter.ToUInt32(b, s + 20);
                if (rva >= virtualAddress && rva < virtualAddress + Math.Max(virtualSize, rawSize))
                {
                    return (int)(rva - virtualAddress + rawPointer);
                }
            }

            return null;
        }

        uint? ex = null;
        int? exOffset = null;
        var debugRva = BitConverter.ToUInt32(b, directories + 6 * 8);
        var debugSize = BitConverter.ToUInt32(b, directories + 6 * 8 + 4);
        if (debugRva != 0 && FileOffset(debugRva) is { } debug)
        {
            for (var entry = 0; entry < debugSize / 28; entry++)
            {
                var d = debug + entry * 28;
                if (BitConverter.ToUInt32(b, d + 12) == DebugTypeExDllCharacteristics)
                {
                    exOffset = (int)BitConverter.ToUInt32(b, d + 24);
                    ex = BitConverter.ToUInt32(b, exOffset.Value);
                }
            }
        }

        uint? guardFlags = null;
        ulong? ehCount = null;
        var loadConfigRva = BitConverter.ToUInt32(b, directories + 10 * 8);
        if (pe64 && loadConfigRva != 0 && FileOffset(loadConfigRva) is { } loadConfig)
        {
            var size = BitConverter.ToUInt32(b, loadConfig);
            if (size >= 148)
            {
                guardFlags = BitConverter.ToUInt32(b, loadConfig + 144);
            }

            if (size >= 280)
            {
                ehCount = BitConverter.ToUInt64(b, loadConfig + 272);
            }
        }

        return new Parsed(machine, dllCharacteristics, ex, exOffset, guardFlags, ehCount);
    }
}

/// <summary>The exception stream and module list of a minidump: what the system recorded when the process died.</summary>
static class Minidump
{
    public static int Describe(string path)
    {
        var b = File.ReadAllBytes(path);
        if (BitConverter.ToUInt32(b, 0) != 0x504D444D)
        {
            Console.WriteLine($"{path}: not a minidump");
            return 2;
        }

        var streams = BitConverter.ToUInt32(b, 8);
        var directory = (int)BitConverter.ToUInt32(b, 12);
        var modules = new List<(ulong Base, uint Size, string Name)>();
        int? exception = null;
        for (var i = 0; i < streams; i++)
        {
            var e = directory + i * 12;
            var type = BitConverter.ToUInt32(b, e);
            var rva = (int)BitConverter.ToUInt32(b, e + 8);
            if (type == 6)
            {
                exception = rva;
            }
            else if (type == 4)
            {
                var count = BitConverter.ToUInt32(b, rva);
                for (var m = 0; m < count; m++)
                {
                    var module = rva + 4 + m * 108;
                    var nameRva = (int)BitConverter.ToUInt32(b, module + 20);
                    var nameLength = (int)BitConverter.ToUInt32(b, nameRva);
                    modules.Add((BitConverter.ToUInt64(b, module), BitConverter.ToUInt32(b, module + 8),
                        Encoding.Unicode.GetString(b, nameRva + 4, nameLength)));
                }
            }
        }

        if (exception is not { } x)
        {
            Console.WriteLine($"{path}: no exception stream");
            return 2;
        }

        string Where(ulong address)
        {
            foreach (var (start, size, name) in modules)
            {
                if (address >= start && address < start + size)
                {
                    return $"{Path.GetFileName(name)}+0x{address - start:X}";
                }
            }

            return "not in a module (dynamic code)";
        }

        var record = x + 8;
        var code = BitConverter.ToUInt32(b, record);
        var flags = BitConverter.ToUInt32(b, record + 4);
        var address = BitConverter.ToUInt64(b, record + 16);
        var parameters = BitConverter.ToUInt32(b, record + 24);
        var info = Enumerable.Range(0, (int)Math.Min(parameters, 15u)).Select(p => BitConverter.ToUInt64(b, record + 32 + p * 8)).ToArray();
        Console.WriteLine($"{path}: thread {BitConverter.ToUInt32(b, x)}; exception code 0x{code:X8}; flags 0x{flags:X}; address 0x{address:X} ({Where(address)})");
        Console.WriteLine($"  parameters: {string.Join(", ", info.Select(p => $"0x{p:X}"))}");
        if (code == 0xC0000409 && info.Length > 0)
        {
            Console.WriteLine($"  fast-fail code: {info[0]} ({FastFail(info[0])})");
        }

        var contextRva = (int)BitConverter.ToUInt32(b, record + 152 + 4);
        if (contextRva > 0 && contextRva + 0x100 <= b.Length)
        {
            var rip = BitConverter.ToUInt64(b, contextRva + 0xF8);
            var rsp = BitConverter.ToUInt64(b, contextRva + 0x98);
            Console.WriteLine($"  context: rip 0x{rip:X} ({Where(rip)}); rsp 0x{rsp:X}");
        }

        return 0;
    }

    /// <summary>The FAST_FAIL_ codes of winnt.h (Windows SDK 10.0.26100.0) that bear on this diagnosis.</summary>
    static string FastFail(ulong code) => code switch
    {
        2 => "FAST_FAIL_STACK_COOKIE_CHECK_FAILURE",
        5 => "FAST_FAIL_INVALID_ARG",
        7 => "FAST_FAIL_FATAL_APP_EXIT",
        10 => "FAST_FAIL_GUARD_ICALL_CHECK_FAILURE",
        13 => "FAST_FAIL_INVALID_SET_OF_CONTEXT",
        18 => "FAST_FAIL_INVALID_JUMP_BUFFER",
        37 => "FAST_FAIL_GUARD_JUMPTABLE",
        38 => "FAST_FAIL_INVALID_LONGJUMP_TARGET",
        39 => "FAST_FAIL_INVALID_DISPATCH_CONTEXT",
        44 => "FAST_FAIL_GUARD_SS_FAILURE",
        47 => "FAST_FAIL_INVALID_CONTROL_STACK",
        48 => "FAST_FAIL_SET_CONTEXT_DENIED",
        57 => "FAST_FAIL_CONTROL_INVALID_RETURN_ADDRESS",
        60 => "FAST_FAIL_VEH_CORRUPTION",
        67 => "FAST_FAIL_KERNEL_CET_SHADOW_STACK_ASSIST",
        72 => "FAST_FAIL_CLR_EXCEPTION_AOT",
        _ => "see winnt.h FAST_FAIL_*",
    };
}
