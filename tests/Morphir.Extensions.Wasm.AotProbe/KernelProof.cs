// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wasmtime;
using Module = Wasmtime.Module;

namespace Morphir.Extensions.Wasm.AotProbe;

/// <summary>
/// The kernel proof of task 2.2 (design KPF-1). It loads the embedded Extism kernel and a published Extism
/// guest into one Wasmtime store, sends one <c>morphir.initialize</c> through <c>handle</c> and checks the
/// three claims of the task. It prints what it observed and returns 0 only when every claim holds.
/// </summary>
public static partial class KernelProof
{
    /// <summary>The published SHA-256 of <c>morphir-python-binding-0.4.2.wasm</c> (finos/morphir-python
    /// release <c>extension/python/v0.4.2</c>, asset <c>morphir-python-binding-0.4.2.wasm.sha256</c>).</summary>
    public const string PythonGuestSha256 = "10acafa0f0282c5240e9620a5f2bb21629cc7580b9add528e7b77c7e54db22b5";

    const long PageBytes = 65536;

    /// <summary>The memory ceiling of the store that runs the call, as in the runtime report sample of the
    /// design (RPT-1): 256 MiB for each linear memory.</summary>
    const long CallCeilingBytes = 256L * 1024 * 1024;

    /// <summary>The first request of a session, in the MEP 0.1 shape that the Python 0.4.2 guest claims
    /// (its release descriptor lists protocol versions ["0.1"]; finos/morphir
    /// spec/mep/smithy/transcripts/doc-initialize.md gives the request).</summary>
    public const string InitializeRequest =
        """{"jsonrpc":"2.0","id":1,"method":"morphir.initialize","params":{"protocolVersions":["0.1"],"host":{"kind":"cli","name":"morphir-dotnet-kernel-proof","version":"0.0.0"}}}""";

    enum Verdict
    {
        Holds,
        Fails,
        NotChecked,
    }

    /// <summary>Runs the proof. Arguments: <c>&lt;guest.wasm&gt; [--publish-log &lt;file&gt;] [--source &lt;path&gt;]...</c>.</summary>
    public static int Run(string[] args, TextWriter output)
    {
        if (!TryParse(args, out var guestPath, out var publishLog, out var sources))
        {
            output.WriteLine("usage: Morphir.Extensions.Wasm.AotProbe <guest.wasm> [--publish-log <file>] [--source <path>]...");
            return 2;
        }

        output.WriteLine("kernel proof: add-wasm-extension-host task 2.2");
        output.WriteLine($"os: {RuntimeInformation.OSDescription}");
        output.WriteLine($"os architecture: {RuntimeInformation.OSArchitecture}; process architecture: {RuntimeInformation.ProcessArchitecture}; runtime identifier: {RuntimeInformation.RuntimeIdentifier}");
        output.WriteLine($"native aot: {!RuntimeFeature.IsDynamicCodeSupported}");
        output.WriteLine($"wasmtime package: {typeof(Engine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"}");
        output.WriteLine();

        var guestBytes = File.ReadAllBytes(guestPath);
        var guestDigest = Convert.ToHexStringLower(SHA256.HashData(guestBytes));
        output.WriteLine($"guest: {Path.GetFileName(guestPath)}, {guestBytes.Length} bytes, sha256 {guestDigest}");
        if (guestDigest != PythonGuestSha256)
        {
            output.WriteLine($"guest digest FAILS: expected the published {PythonGuestSha256}");
            return 1;
        }

        output.WriteLine("guest digest equals the published digest");

        var kernelBytes = ExtismKernel.Bytes;
        output.WriteLine($"kernel: moonrockz/extism {ExtismKernel.PackageVersion}, {kernelBytes.Length} bytes, sha256 {Convert.ToHexStringLower(SHA256.HashData(kernelBytes.Span))} (recorded {ExtismKernel.Sha256})");
        output.WriteLine();

        using var engine = WasmEngine.Create();
        using var kernelModule = Module.FromBytes(engine, "extism-kernel", kernelBytes.Span);
        using var guestModule = Module.FromBytes(engine, Path.GetFileName(guestPath), guestBytes);
        Describe(output, "kernel", kernelModule);
        Describe(output, "guest", guestModule);

        var claim2 = Check(output, "claim 2", () => CheckMemoryLimit(output, engine, MemoryMinimumPages(kernelModule), guestModule));
        var claim3 = Check(output, "claim 3", () => CheckHandle(output, engine, guestModule));
        var claim1 = Check(output, "claim 1", () => CheckNoTrimWarnings(output, publishLog, sources));

        output.WriteLine("summary:");
        output.WriteLine($"claim 1, zero IL warnings and no suppressions: {Text(claim1)}");
        output.WriteLine($"claim 2, the store limit covers the kernel's memory: {Text(claim2)}");
        output.WriteLine($"claim 3, the host reads the result of handle directly: {Text(claim3)}");
        return claim1 == Verdict.Holds && claim2 == Verdict.Holds && claim3 == Verdict.Holds ? 0 : 1;
    }

    static string Text(Verdict verdict) => verdict switch
    {
        Verdict.Holds => "HOLDS",
        Verdict.Fails => "FAILS",
        Verdict.NotChecked => "NOT CHECKED",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict)),
    };

    static Verdict Check(TextWriter output, string name, Func<Verdict> check)
    {
        output.WriteLine($"{name}:");
        Verdict verdict;
        try
        {
            verdict = check();
        }
        catch (Exception exception)
        {
            output.WriteLine($"  {exception.GetType().Name}: {exception.Message}");
            verdict = Verdict.Fails;
        }

        output.WriteLine($"  {name}: {Text(verdict)}");
        output.WriteLine();
        return verdict;
    }

    static bool TryParse(string[] args, out string guestPath, out string? publishLog, out List<string> sources)
    {
        guestPath = "";
        publishLog = null;
        sources = [];
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--publish-log" when i + 1 < args.Length:
                    publishLog = args[++i];
                    break;
                case "--source" when i + 1 < args.Length:
                    sources.Add(args[++i]);
                    break;
                case var path when !path.StartsWith("--", StringComparison.Ordinal) && guestPath.Length == 0:
                    guestPath = path;
                    break;
                default:
                    return false;
            }
        }

        return guestPath.Length > 0;
    }

    static void Describe(TextWriter output, string label, Module module)
    {
        output.WriteLine($"{label} imports ({module.Imports.Count}):");
        foreach (var import in module.Imports)
        {
            output.WriteLine($"  {import.ModuleName} {import.Name} {Signature(import)}");
        }

        output.WriteLine($"{label} exports ({module.Exports.Count}):");
        foreach (var export in module.Exports)
        {
            output.WriteLine($"  {export.Name} {Signature(export)}");
        }

        output.WriteLine();
    }

    static string Signature(Import import) => import switch
    {
        FunctionImport f => $"func ({Kinds(f.Parameters)}) -> ({Kinds(f.Results)})",
        MemoryImport m => $"memory min {m.Minimum} max {m.Maximum?.ToString() ?? "none"} pages",
        _ => import.GetType().Name,
    };

    static string Signature(Export export) => export switch
    {
        FunctionExport f => $"func ({Kinds(f.Parameters)}) -> ({Kinds(f.Results)})",
        MemoryExport m => $"memory min {m.Minimum} max {m.Maximum?.ToString() ?? "none"} pages",
        _ => export.GetType().Name,
    };

    static string Kinds(IReadOnlyList<ValueKind> kinds) => string.Join(", ", kinds.Select(k => k.ToString().ToLowerInvariant()));

    static long MemoryMinimumPages(Module module) =>
        module.Exports.OfType<MemoryExport>().Select(m => m.Minimum).DefaultIfEmpty(0).Max();

    static string MemoryExportName(Module module) =>
        module.Exports.OfType<MemoryExport>().Select(m => m.Name).FirstOrDefault()
        ?? throw new InvalidOperationException($"{module.Name} exports no memory");

    /// <summary>Claim 2. In a store whose limiter sets a small per-memory ceiling, a kernel alloc larger than
    /// the ceiling returns 0 and the kernel memory stays at or under the ceiling, and the guest memory cannot
    /// grow past it either. The same requests in a store with a larger ceiling succeed, so the limiter is
    /// what refused them.</summary>
    static Verdict CheckMemoryLimit(TextWriter output, Engine engine, long kernelMinimum, Module guestModule)
    {
        var ok = true;
        void Expect(bool condition, string what)
        {
            output.WriteLine($"  {(condition ? "ok" : "FAILED")}: {what}");
            ok &= condition;
        }

        var guestMemoryName = MemoryExportName(guestModule);
        var guestMinimum = MemoryMinimumPages(guestModule);
        var ceilingPages = Math.Max(guestMinimum, kernelMinimum) + 16;
        var ceiling = ceilingPages * PageBytes;
        var request = ceiling + PageBytes;
        var growPages = ceilingPages - guestMinimum + 1;
        output.WriteLine($"  test ceiling: {ceiling} bytes ({ceilingPages} pages) for each memory; kernel alloc request {request} bytes; guest grow {growPages} pages from its minimum {guestMinimum}");

        using (var limited = KernelStore.Create(engine, ceiling))
        {
            var guest = limited.Link(guestModule);
            var guestMemory = guest.GetMemory(guestMemoryName) ?? throw new InvalidOperationException("the guest memory is not exported");
            output.WriteLine($"  small store after instantiation: kernel memory {limited.KernelMemory.GetLength()} bytes, guest memory {guestMemory.GetLength()} bytes");

            var refused = limited.Alloc(request);
            var kernelAfter = limited.KernelMemory.GetLength();
            output.WriteLine($"  small store: alloc({request}) = {refused}; kernel memory {kernelAfter} bytes");
            Expect(refused == 0, "a kernel alloc larger than the ceiling returns 0");
            Expect(kernelAfter <= ceiling, "the kernel memory is at or under the ceiling");

            var accepted = limited.Alloc(4096);
            output.WriteLine($"  small store: alloc(4096) = {accepted}; kernel memory {limited.KernelMemory.GetLength()} bytes");
            Expect(accepted != 0, "a kernel alloc under the ceiling still succeeds");

            var guestBefore = guestMemory.GetLength();
            string grow;
            try
            {
                guestMemory.Grow(growPages);
                grow = "grew";
            }
            catch (WasmtimeException exception)
            {
                grow = $"refused ({exception.Message.Split('\n')[0].Trim()})";
            }

            var guestAfter = guestMemory.GetLength();
            output.WriteLine($"  small store: guest memory grow by {growPages} pages {grow}; guest memory {guestBefore} -> {guestAfter} bytes");
            Expect(guestAfter <= ceiling, "the guest memory is at or under the ceiling");
            Expect(guestAfter == guestBefore, "the refused grow left the guest memory unchanged");
        }

        var largeCeiling = 4 * ceiling;
        using (var large = KernelStore.Create(engine, largeCeiling))
        {
            var guest = large.Link(guestModule);
            var guestMemory = guest.GetMemory(guestMemoryName) ?? throw new InvalidOperationException("the guest memory is not exported");
            var handle = large.Alloc(request);
            var kernelAfter = large.KernelMemory.GetLength();
            output.WriteLine($"  large store ({largeCeiling} bytes): alloc({request}) = {handle}; kernel memory {kernelAfter} bytes");
            Expect(handle != 0, "the same alloc succeeds under a larger ceiling");
            Expect(kernelAfter > ceiling && kernelAfter <= largeCeiling, "the kernel memory grew past the small ceiling and stayed under the large one");

            var guestBefore = guestMemory.GetLength();
            guestMemory.Grow(growPages);
            var guestAfter = guestMemory.GetLength();
            output.WriteLine($"  large store: guest memory grow by {growPages} pages; guest memory {guestBefore} -> {guestAfter} bytes");
            Expect(guestAfter > ceiling && guestAfter <= largeCeiling, "the same guest grow succeeds under a larger ceiling");
        }

        return ok ? Verdict.Holds : Verdict.Fails;
    }

    /// <summary>Claim 3. One <c>handle</c> call with the initialize request; the host reads the i32 result, the
    /// output and the error value from the kernel itself. The output must be one JSON-RPC response to id 1.</summary>
    static Verdict CheckHandle(TextWriter output, Engine engine, Module guestModule)
    {
        using var store = KernelStore.Create(engine, CallCeilingBytes);
        var guest = store.Link(guestModule);
        var guestMemory = guest.GetMemory(MemoryExportName(guestModule));
        output.WriteLine($"  store: {CallCeilingBytes} bytes ceiling for each memory; 2 instances (kernel, guest); memories: kernel 1, guest {guestModule.Exports.OfType<MemoryExport>().Count()} exported, {guestModule.Imports.OfType<MemoryImport>().Count()} imported");
        output.WriteLine($"  request ({Encoding.UTF8.GetByteCount(InitializeRequest)} bytes): {InitializeRequest}");

        var result = store.CallHandle(guest, Encoding.UTF8.GetBytes(InitializeRequest));
        output.WriteLine($"  guest initialization: {store.Initialization}");
        output.WriteLine($"  handle returned i32: {result.ReturnCode}");
        output.WriteLine($"  kernel error value: error_get() = {result.ErrorHandle}, {result.Error.Length} bytes{(result.Error.Length > 0 ? ": " + Encoding.UTF8.GetString(result.Error) : "")}");
        output.WriteLine($"  kernel output: output_offset() = {result.OutputOffset}, output_length() = {result.Output.Length}");
        output.WriteLine($"  output bytes (UTF-8): {Encoding.UTF8.GetString(result.Output)}");
        output.WriteLine($"  memory after the call: kernel {store.KernelMemory.GetLength()} bytes, guest {guestMemory?.GetLength().ToString() ?? "unknown"} bytes");
        output.WriteLine($"  host functions outside the kernel and WASI that the guest called: {(store.UnprovidedCalls.Count == 0 ? "none" : string.Join(", ", store.UnprovidedCalls))}");

        var ok = true;
        void Expect(bool condition, string what)
        {
            output.WriteLine($"  {(condition ? "ok" : "FAILED")}: {what}");
            ok &= condition;
        }

        Expect(result.ReturnCode == 0, "handle returned 0");
        Expect(result.ErrorHandle == 0, "the kernel holds no error");
        Expect(result.Output.Length > 0, "the kernel holds output");
        var (wellFormed, kind) = JsonRpcResponse(result.Output);
        Expect(wellFormed, $"the output is one JSON-RPC 2.0 response to id 1 ({kind})");
        return ok ? Verdict.Holds : Verdict.Fails;
    }

    static (bool WellFormed, string Kind) JsonRpcResponse(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, "not an object");
            }

            var version = root.TryGetProperty("jsonrpc", out var v) && v.ValueKind == JsonValueKind.String && v.GetString() == "2.0";
            var id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number && i.GetInt64() == 1;
            var hasResult = root.TryGetProperty("result", out _);
            var hasError = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number
                && e.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String;
            var kind = hasResult && !hasError ? "result" : hasError && !hasResult ? "error" : "neither result nor error";
            return (version && id && hasResult != hasError, kind);
        }
        catch (JsonException exception)
        {
            return (false, $"not JSON: {exception.Message}");
        }
    }

    /// <summary>Claim 1. The Native AOT publish log has no IL warning or error of any number, and the project
    /// files and sources hold no trim or AOT suppression.</summary>
    static Verdict CheckNoTrimWarnings(TextWriter output, string? publishLog, List<string> sources)
    {
        if (publishLog is null || sources.Count == 0)
        {
            output.WriteLine("  pass --publish-log <file> and --source <path> to check the Native AOT publish log and the sources");
            return Verdict.NotChecked;
        }

        var lines = File.ReadAllLines(publishLog);
        var ilLines = lines.Where(line => IlDiagnostic().IsMatch(line)).Distinct().ToArray();
        output.WriteLine($"  publish log: {publishLog}, {lines.Length} lines");
        foreach (var line in lines.Where(line => LogSummary().IsMatch(line)))
        {
            output.WriteLine($"  log summary: {line.Trim()}");
        }

        output.WriteLine($"  IL warnings and errors in the publish log: {ilLines.Length}");
        foreach (var line in ilLines)
        {
            output.WriteLine($"    {line.Trim()}");
        }

        var findings = new List<string>();
        var files = 0;
        foreach (var file in sources.SelectMany(SourceFiles))
        {
            files++;
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (Suppressions.Any(pattern => pattern.IsMatch(line)))
                {
                    findings.Add($"{file}:{lineNumber}: {line.Trim()}");
                }
            }
        }

        output.WriteLine($"  suppressions in {files} project and source files: {findings.Count}");
        foreach (var finding in findings)
        {
            output.WriteLine($"    {finding}");
        }

        return ilLines.Length == 0 && findings.Count == 0 && files > 0 ? Verdict.Holds : Verdict.Fails;
    }

    static IEnumerable<string> SourceFiles(string path)
    {
        if (File.Exists(path))
        {
            return [path];
        }

        string[] extensions = [".cs", ".csproj", ".props", ".targets", ".xml", ".json"];
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal);
    }

    // The patterns are put together from pieces so that this file does not match them itself.
    static readonly Regex[] Suppressions =
    [
        new("<" + "NoWarn>[^<]*IL\\d", RegexOptions.IgnoreCase),
        new("No" + "Warn=\"[^\"]*IL\\d", RegexOptions.IgnoreCase),
        new("Warnings" + "NotAsErrors[^<]*IL\\d", RegexOptions.IgnoreCase),
        new("Unconditional" + "SuppressMessage"),
        new("Suppress" + "Message\\([^)]*IL\\d{4}"),
        new("Trimmer" + "Root(Descriptor|Assembly)"),
        new("Suppress" + "TrimAnalysisWarnings"),
        new("#pragma\\s+warning\\s+disable\\s+IL", RegexOptions.IgnoreCase),
        new("<(Enable" + "TrimAnalyzer|EnableAot" + "Analyzer|IsAot" + "Compatible|IsTrim" + "mable)>\\s*false", RegexOptions.IgnoreCase),
    ];

    [GeneratedRegex(@"\b(warning|error)\s+IL\d{4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex IlDiagnostic();

    [GeneratedRegex(@"Warning\(s\)|Error\(s\)|Build succeeded|Build FAILED")]
    private static partial Regex LogSummary();
}
