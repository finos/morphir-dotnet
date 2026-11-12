using System.Globalization;
using System.Text;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// A guest written for the named tests of tasks 3.3 and 3.4, as WebAssembly text that Wasmtime converts to bytes.
/// The pinned modules each do one thing; this guest does many, one per call: the first byte of the input names
/// the step, and an unknown step traps. It imports every host function and WASI function the steps use, so one
/// module covers them all. Its output is raw bytes, which <see cref="SessionStore.CallHandle"/> returns as they are.
/// </summary>
internal static class CraftedGuest
{
    /// <summary>The bytes one flood step writes: 10 MiB, in 10,240 writes of 1 KiB.</summary>
    public const int FloodWrites = 10240;

    public const int FloodChunkBytes = 1024;

    public const string StdoutJson = """{"jsonrpc":"2.0","id":1,"result":"from-stdout"}""";

    public const string OutputJson = """{"jsonrpc":"2.0","id":1,"result":"from-output"}""";

    /// <summary>The steps, by the input byte that selects them.</summary>
    public static class Step
    {
        public const string VarSetAtLimit = "A";      // var_set("k", 1,048,575 bytes): with the key, exactly 1 MiB
        public const string VarSetOneByte = "B";      // var_set("j", 1 byte)
        public const string VarDelete = "C";          // var_set("k", handle 0)
        public const string VarSetOverLimit = "D";    // var_set("k", 1,048,576 bytes): with the key, 1 byte over
        public const string VarGet = "G";             // var_get("k"): "present" or "missing"
        public const string CacheAtLimit = "I";       // morphir_cache_ir("k", 16,777,215 bytes): exactly 16 MiB
        public const string CacheOneByte = "J";       // morphir_cache_ir("j", 1 byte)
        public const string Cache1024 = "E";          // 1,024 entries, keys "aaa" to "ppd", values "0"
        public const string CacheAnother = "F";       // morphir_cache_ir("zzz", 1 byte): a new entry
        public const string CacheOverwrite = "O";     // morphir_cache_ir("aaa", 1 byte): an entry that exists
        public const string CacheMissing = "M";       // morphir_get_cached_ir("missing-key"): the block it gives
        public const string Workspace = "W";          // morphir_get_workspace_info(): the block it gives
        public const string Http = "H";               // http_request for the URL the guest was built with
        public const string PathOpenFd3 = "p";        // path_open on fd 3: the errno
        public const string PathOpenFd0 = "q";        // path_open on fd 0: the errno
        public const string Realtime = "c";           // clock_time_get(realtime): errno at 0, time at 8
        public const string Monotonic = "m";          // clock_time_get(monotonic): errno at 0, time at 8
        public const string Random = "r";             // random_get(32): errno at 0, bytes at 16
        public const string ArgsAndEnvironment = "e"; // errnos of args_sizes_get, environ_sizes_get, args_get, environ_get, then the four counts
        public const string Stdin = "s";              // fd_read(0): errno, bytes read
        public const string FloodStdout = "o";        // 10 MiB to stdout, then "done"
        public const string FloodLog = "l";           // 10 MiB through log_info, then "done"
        public const string Unsupported = "y";        // errnos of poll_oneoff (10 s), fd_fdstat_get(1), sched_yield
        public const string StdoutLooksLikeReply = "j"; // a JSON-RPC response on stdout, another as the output
        public const string Trap = "T";               // traps
    }

    /// <summary>The guest, with <paramref name="httpUrl"/> as the URL of its <c>http_request</c>.</summary>
    public static string Wat(string httpUrl = "https://example.org/")
    {
        var request = $$"""{"url":"{{httpUrl}}","method":"POST"}""";
        var data = new StringBuilder()
            .Append(Data(256, "k")).Append(Data(257, "j")).Append(Data(258, "zzz")).Append(Data(264, "/etc/passwd"))
            .Append(Data(270, "aaa")).Append(Data(300, "ok")).Append(Data(310, "present")).Append(Data(320, "missing"))
            .Append(Data(330, "done")).Append(Data(340, "missing-key")).Append(Data(400, request))
            .Append(Data(1024, new string('x', FloodChunkBytes))).Append(Data(2048, StdoutJson)).Append(Data(2200, OutputJson))
            .ToString();
        var steps = new (string Step, string Body)[]
        {
            (Step.VarSetAtLimit, "(call $var_set (call $block (i32.const 256) (i32.const 1)) (call $alloc (i64.const 1048575))) (call $answer (i32.const 300) (i32.const 2))"),
            (Step.VarSetOneByte, "(call $var_set (call $block (i32.const 257) (i32.const 1)) (call $alloc (i64.const 1))) (call $answer (i32.const 300) (i32.const 2))"),
            (Step.VarDelete, "(call $var_set (call $block (i32.const 256) (i32.const 1)) (i64.const 0)) (call $answer (i32.const 300) (i32.const 2))"),
            (Step.VarSetOverLimit, "(call $var_set (call $block (i32.const 256) (i32.const 1)) (call $alloc (i64.const 1048576))) (call $answer (i32.const 300) (i32.const 2))"),
            (Step.VarGet, """
                (if (i64.eqz (call $var_get (call $block (i32.const 256) (i32.const 1))))
                  (then (call $answer (i32.const 320) (i32.const 7)))
                  (else (call $answer (i32.const 310) (i32.const 7))))
                """),
            (Step.CacheAtLimit, "(call $cache_ir (call $block (i32.const 256) (i32.const 1)) (call $alloc (i64.const 16777215))) (call $answer (i32.const 300) (i32.const 2))"),
            (Step.CacheOneByte, "(call $cache_ir (call $block (i32.const 257) (i32.const 1)) (call $alloc (i64.const 1))) (call $answer (i32.const 300) (i32.const 2))"),
            (Step.Cache1024, """
                (local.set $n (i32.const 0))
                (block $stored
                  (loop $store
                    (br_if $stored (i32.ge_u (local.get $n) (i32.const 1024)))
                    (local.set $h (call $alloc (i64.const 3)))
                    (call $store_u8 (local.get $h) (i32.add (i32.const 97) (i32.and (local.get $n) (i32.const 15))))
                    (call $store_u8 (i64.add (local.get $h) (i64.const 1))
                      (i32.add (i32.const 97) (i32.and (i32.shr_u (local.get $n) (i32.const 4)) (i32.const 15))))
                    (call $store_u8 (i64.add (local.get $h) (i64.const 2))
                      (i32.add (i32.const 97) (i32.and (i32.shr_u (local.get $n) (i32.const 8)) (i32.const 15))))
                    (local.set $v (call $alloc (i64.const 1)))
                    (call $store_u8 (local.get $v) (i32.const 48))
                    (call $cache_ir (local.get $h) (local.get $v))
                    (local.set $n (i32.add (local.get $n) (i32.const 1)))
                    (br $store)))
                (call $answer (i32.const 300) (i32.const 2))
                """),
            (Step.CacheAnother, "(call $cache_ir (call $block (i32.const 258) (i32.const 3)) (call $block (i32.const 300) (i32.const 1))) (call $answer (i32.const 300) (i32.const 2))"),
            (Step.CacheOverwrite, "(call $cache_ir (call $block (i32.const 270) (i32.const 3)) (call $block (i32.const 300) (i32.const 1))) (call $answer (i32.const 300) (i32.const 2))"),
            (Step.CacheMissing, "(call $answer_block (call $get_cached_ir (call $block (i32.const 340) (i32.const 11))))"),
            (Step.Workspace, "(call $answer_block (call $workspace_info))"),
            (Step.Http, $"(drop (call $http_request (call $block (i32.const 400) (i32.const {Encoding.UTF8.GetByteCount(request)})) (i64.const 0))) (call $answer (i32.const 330) (i32.const 4))"),
            (Step.PathOpenFd3, """
                (i32.store (i32.const 0) (call $path_open (i32.const 3) (i32.const 0) (i32.const 264) (i32.const 11) (i32.const 0)
                  (i64.const 0) (i64.const 0) (i32.const 0) (i32.const 64)))
                (call $answer (i32.const 0) (i32.const 4))
                """),
            (Step.PathOpenFd0, """
                (i32.store (i32.const 0) (call $path_open (i32.const 0) (i32.const 0) (i32.const 264) (i32.const 11) (i32.const 0)
                  (i64.const 0) (i64.const 0) (i32.const 0) (i32.const 64)))
                (call $answer (i32.const 0) (i32.const 4))
                """),
            (Step.Realtime, "(i64.store (i32.const 8) (i64.const 0)) (i32.store (i32.const 0) (call $clock_time_get (i32.const 0) (i64.const 1) (i32.const 8))) (call $answer (i32.const 0) (i32.const 16))"),
            (Step.Monotonic, "(i64.store (i32.const 8) (i64.const 0)) (i32.store (i32.const 0) (call $clock_time_get (i32.const 1) (i64.const 1) (i32.const 8))) (call $answer (i32.const 0) (i32.const 16))"),
            (Step.Random, """
                (i64.store (i32.const 16) (i64.const 0)) (i64.store (i32.const 24) (i64.const 0))
                (i64.store (i32.const 32) (i64.const 0)) (i64.store (i32.const 40) (i64.const 0))
                (i32.store (i32.const 0) (call $random_get (i32.const 16) (i32.const 32)))
                (call $answer (i32.const 0) (i32.const 48))
                """),
            (Step.ArgsAndEnvironment, """
                (i32.store (i32.const 16) (i32.const 1)) (i32.store (i32.const 20) (i32.const 1))
                (i32.store (i32.const 24) (i32.const 1)) (i32.store (i32.const 28) (i32.const 1))
                (i32.store (i32.const 0) (call $args_sizes_get (i32.const 16) (i32.const 20)))
                (i32.store (i32.const 4) (call $environ_sizes_get (i32.const 24) (i32.const 28)))
                (i32.store (i32.const 8) (call $args_get (i32.const 64) (i32.const 128)))
                (i32.store (i32.const 12) (call $environ_get (i32.const 64) (i32.const 128)))
                (call $answer (i32.const 0) (i32.const 32))
                """),
            (Step.Stdin, """
                (i32.store (i32.const 40) (i32.const 128)) (i32.store (i32.const 44) (i32.const 16)) (i32.store (i32.const 48) (i32.const 1))
                (i32.store (i32.const 0) (call $fd_read (i32.const 0) (i32.const 40) (i32.const 1) (i32.const 48)))
                (i32.store (i32.const 4) (i32.load (i32.const 48)))
                (call $answer (i32.const 0) (i32.const 8))
                """),
            (Step.FloodStdout, $$"""
                (i32.store (i32.const 40) (i32.const 1024)) (i32.store (i32.const 44) (i32.const {{FloodChunkBytes}}))
                (local.set $n (i32.const 0))
                (block $written
                  (loop $write
                    (br_if $written (i32.ge_u (local.get $n) (i32.const {{FloodWrites}})))
                    (drop (call $fd_write (i32.const 1) (i32.const 40) (i32.const 1) (i32.const 48)))
                    (local.set $n (i32.add (local.get $n) (i32.const 1)))
                    (br $write)))
                (call $answer (i32.const 330) (i32.const 4))
                """),
            (Step.FloodLog, $$"""
                (local.set $h (call $block (i32.const 1024) (i32.const {{FloodChunkBytes}})))
                (local.set $n (i32.const 0))
                (block $logged
                  (loop $log
                    (br_if $logged (i32.ge_u (local.get $n) (i32.const {{FloodWrites}})))
                    (call $log_info (local.get $h))
                    (local.set $n (i32.add (local.get $n) (i32.const 1)))
                    (br $log)))
                (call $answer (i32.const 330) (i32.const 4))
                """),
            (Step.Unsupported, """
                (i64.store (i32.const 0) (i64.const 0)) (i64.store (i32.const 8) (i64.const 0))
                (i32.store (i32.const 16) (i32.const 1)) (i64.store (i32.const 24) (i64.const 10000000000))
                (i64.store (i32.const 32) (i64.const 0)) (i64.store (i32.const 40) (i64.const 0))
                (i32.store (i32.const 200) (call $poll_oneoff (i32.const 0) (i32.const 128) (i32.const 1) (i32.const 192)))
                (i32.store (i32.const 204) (call $fd_fdstat_get (i32.const 1) (i32.const 64)))
                (i32.store (i32.const 208) (call $sched_yield))
                (call $answer (i32.const 200) (i32.const 12))
                """),
            (Step.StdoutLooksLikeReply, $"""
                (i32.store (i32.const 40) (i32.const 2048)) (i32.store (i32.const 44) (i32.const {StdoutJson.Length}))
                (drop (call $fd_write (i32.const 1) (i32.const 40) (i32.const 1) (i32.const 48)))
                (call $answer (i32.const 2200) (i32.const {OutputJson.Length}))
                """),
        };

        var handle = new StringBuilder();
        foreach (var (step, body) in steps)
        {
            handle.Append(CultureInfo.InvariantCulture, $"    (if (i32.eq (local.get $step) (i32.const {(int)step[0]}))\n      (then\n{body}\n        (return (i32.const 0))))\n");
        }

        return $$"""
            (module
              (import "extism:host/env" "alloc" (func $alloc (param i64) (result i64)))
              (import "extism:host/env" "store_u8" (func $store_u8 (param i64 i32)))
              (import "extism:host/env" "length" (func $length (param i64) (result i64)))
              (import "extism:host/env" "input_load_u8" (func $input_load_u8 (param i64) (result i32)))
              (import "extism:host/env" "output_set" (func $output_set (param i64 i64)))
              (import "extism:host/env" "var_get" (func $var_get (param i64) (result i64)))
              (import "extism:host/env" "var_set" (func $var_set (param i64 i64)))
              (import "extism:host/env" "log_info" (func $log_info (param i64)))
              (import "extism:host/env" "http_request" (func $http_request (param i64 i64) (result i64)))
              (import "extism:host/user" "morphir_cache_ir" (func $cache_ir (param i64 i64)))
              (import "extism:host/user" "morphir_get_cached_ir" (func $get_cached_ir (param i64) (result i64)))
              (import "extism:host/user" "morphir_get_workspace_info" (func $workspace_info (result i64)))
              (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "fd_read" (func $fd_read (param i32 i32 i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "clock_time_get" (func $clock_time_get (param i32 i64 i32) (result i32)))
              (import "wasi_snapshot_preview1" "random_get" (func $random_get (param i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "path_open"
                (func $path_open (param i32 i32 i32 i32 i32 i64 i64 i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "args_sizes_get" (func $args_sizes_get (param i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "args_get" (func $args_get (param i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "environ_sizes_get" (func $environ_sizes_get (param i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "environ_get" (func $environ_get (param i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "poll_oneoff" (func $poll_oneoff (param i32 i32 i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "fd_fdstat_get" (func $fd_fdstat_get (param i32 i32) (result i32)))
              (import "wasi_snapshot_preview1" "sched_yield" (func $sched_yield (result i32)))
              (memory (export "memory") 2)
            {{data}}
              ;; Copies $len bytes of guest memory at $ptr into a new kernel block.
              (func $block (param $ptr i32) (param $len i32) (result i64)
                (local $h i64) (local $i i32)
                (local.set $h (call $alloc (i64.extend_i32_u (local.get $len))))
                (block $copied
                  (loop $copy
                    (br_if $copied (i32.ge_u (local.get $i) (local.get $len)))
                    (call $store_u8
                      (i64.add (local.get $h) (i64.extend_i32_u (local.get $i)))
                      (i32.load8_u (i32.add (local.get $ptr) (local.get $i))))
                    (local.set $i (i32.add (local.get $i) (i32.const 1)))
                    (br $copy)))
                (local.get $h))
              (func $answer (param $ptr i32) (param $len i32)
                (call $output_set (call $block (local.get $ptr) (local.get $len)) (i64.extend_i32_u (local.get $len))))
              (func $answer_block (param $h i64)
                (call $output_set (local.get $h) (call $length (local.get $h))))
              (func (export "handle") (result i32)
                (local $step i32) (local $h i64) (local $v i64) (local $n i32)
                (local.set $step (call $input_load_u8 (i64.const 0)))
            {{handle}}
                unreachable))
            """;
    }

    /// <summary>The guest of <see cref="Wat"/>, admitted to this host.</summary>
    public static ExtismModule Admit(string? httpUrl = null) => AdmitWat(Wat(httpUrl ?? "https://example.org/"));

    public static ExtismModule AdmitWat(string wat) =>
        WasmEngine.Shared.Load(Wasmtime.Module.ConvertText(wat)).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(
                admitted => admitted,
                refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));

    /// <summary>A data segment of WebAssembly text with <paramref name="text"/> at <paramref name="address"/>.</summary>
    static string Data(int address, string text)
    {
        var escaped = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            escaped.Append(b is >= 0x20 and < 0x7f and not (byte)'"' and not (byte)'\\'
                ? ((char)b).ToString()
                : $"\\{b:x2}");
        }

        return $"  (data (i32.const {address}) \"{escaped}\")\n";
    }

    /// <summary>The input that selects <paramref name="step"/>.</summary>
    public static byte[] Input(string step) => Encoding.ASCII.GetBytes(step);
}
