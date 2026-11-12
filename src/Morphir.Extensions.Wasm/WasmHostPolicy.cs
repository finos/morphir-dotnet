// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// The host-function and WASI policy of the pinned contract, its <c>policy</c> object (parent decision SBX-1:
/// accept the imports, bound the calls). The numbers are read from the contract. Every other leaf names a behaviour,
/// such as <c>handle-0</c> or <c>stops-call-fails-session</c>; the parser checks that each is the one this library
/// implements, so a pin that changes a behaviour fails to load instead of running with the old one.
/// </summary>
/// <param name="VariableStoreBytes">The most bytes of keys and values the Extism variable store of a session holds
/// (<c>var.storeBytes</c>).</param>
/// <param name="IrCacheBytes">The most bytes of keys and values the IR cache of a session holds (<c>irCache.bytes</c>).</param>
/// <param name="IrCacheEntries">The most entries the IR cache of a session holds (<c>irCache.entries</c>).</param>
/// <param name="LogLevel">What <c>get_log_level</c> returns (<c>logging.getLogLevel</c>).</param>
/// <param name="LogLevels">The log level names in code order from 0 (<c>logging.levels</c>).</param>
/// <param name="DroppedLogLevels">The levels whose <c>log_*</c> messages are dropped (<c>logging.dropped</c>).</param>
/// <param name="HttpStatusCodeWithoutRequest">What <c>http_status_code</c> returns (<c>http.statusCodeWithoutRequest</c>).</param>
/// <param name="FdAboveTwoErrno">The errno of a WASI function on an fd above 2 (<c>wasi.fdAboveTwoErrno</c>).</param>
/// <param name="UnsupportedErrno">The errno of any other WASI function (<c>wasi.unsupportedErrno</c>).</param>
public sealed record WasmHostPolicy(
    long VariableStoreBytes,
    long IrCacheBytes,
    int IrCacheEntries,
    int LogLevel,
    IReadOnlyList<string> LogLevels,
    IReadOnlySet<string> DroppedLogLevels,
    int HttpStatusCodeWithoutRequest,
    int FdAboveTwoErrno,
    int UnsupportedErrno)
{
    /// <summary>Reads the contract's <c>policy</c> object, or throws when it names a behaviour this library does
    /// not implement.</summary>
    internal static WasmHostPolicy Read(JsonElement policy, Func<string, Exception> invalid)
    {
        void Expect(string section, string member, string implemented)
        {
            var value = policy.GetProperty(section).GetProperty(member);
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            if (text != implemented)
            {
                throw invalid($"its policy {section}.{member} is {text}, and this library implements {implemented}");
            }
        }

        static IReadOnlyList<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToArray();

        Expect("config", "missing", "handle-0");
        Expect("var", "missing", "handle-0");
        Expect("var", "zeroValueDeletes", "true");
        Expect("var", "overLimit", "failure");
        Expect("var", "lifetime", "session");
        Expect("irCache", "missing", "null");
        Expect("irCache", "overLimit", "failure");
        Expect("irCache", "lifetime", "session");
        Expect("logging", "recorded", "guest-output");
        Expect("logging", "morphirLog", "unfiltered");
        Expect("http", "request", "stops-call-fails-session");
        Expect("http", "headersWithoutRequest", "handle-0");
        Expect("workspaceInfo", "root", "private-empty-directory");
        Expect("workspaceInfo", "output_dir", string.Empty);
        Expect("wasi", "preopens", "0");
        Expect("wasi", "arguments", "0");
        Expect("wasi", "environment", "0");
        Expect("wasi", "stdin", "empty");
        Expect("wasi", "clocks", "available");
        Expect("wasi", "random", "available");
        Expect("wasi", "stdout", "guest-output");
        Expect("wasi", "stderr", "guest-output");
        Expect("wasi", "procExitZero", "call-succeeds");

        var recorded = Strings(policy.GetProperty("http").GetProperty("recorded"));
        if (!recorded.SequenceEqual(["method", "url"]))
        {
            throw invalid($"its policy http.recorded is {string.Join(", ", recorded)}, and this library records the method and the URL");
        }

        var logging = policy.GetProperty("logging");
        var levels = Strings(logging.GetProperty("levels"));
        var dropped = Strings(logging.GetProperty("dropped")).ToHashSet(StringComparer.Ordinal);
        var level = logging.GetProperty("getLogLevel").GetInt32();
        if (level < 0 || level >= levels.Count || !dropped.IsSubsetOf(levels))
        {
            throw invalid("its logging policy names a level it does not list");
        }

        var variables = policy.GetProperty("var");
        var cache = policy.GetProperty("irCache");
        var wasi = policy.GetProperty("wasi");
        return new WasmHostPolicy(
            variables.GetProperty("storeBytes").GetInt64(),
            cache.GetProperty("bytes").GetInt64(),
            cache.GetProperty("entries").GetInt32(),
            level,
            levels,
            dropped,
            policy.GetProperty("http").GetProperty("statusCodeWithoutRequest").GetInt32(),
            wasi.GetProperty("fdAboveTwoErrno").GetInt32(),
            wasi.GetProperty("unsupportedErrno").GetInt32());
    }
}
