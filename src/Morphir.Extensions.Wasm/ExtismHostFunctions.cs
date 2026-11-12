// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using Wasmtime;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// The Extism host functions of <c>extism:host/env</c> beyond the kernel, and the four Morphir functions of
/// <c>extism:host/user</c>, as the pinned contract's policy defines them for this host (parent decision SBX-1:
/// accept the imports, bound the calls). One instance per session: its variable store and IR cache start empty and
/// end with the session.
/// </summary>
/// <remarks>
/// <para>
/// A text argument or result is a handle to a kernel block, and handle 0 means no block. The functions read and
/// write blocks through the session's kernel, as an Extism host does: <c>length</c> gives a block's size (0 for a
/// handle that is not a block), the bytes are read from kernel memory, and a result is a new block from
/// <c>alloc</c>. A handle that names no block where the function needs one, or a kernel with no room for a result,
/// stops the call (fault <c>failure</c>).
/// </para>
/// <para>
/// No function here reaches the network, the file system, the environment or the host's configuration. The one
/// directory is the session's private workspace, which <c>morphir_get_workspace_info</c> names and the guest cannot
/// open.
/// </para>
/// </remarks>
internal sealed class ExtismHostFunctions
{
    const string Env = "extism:host/env";
    const string User = "extism:host/user";

    readonly SessionStore session;
    readonly WasmHostPolicy policy;
    readonly BoundedStore variables;
    readonly BoundedStore irCache;
    readonly List<RefusedHttpRequest> httpRequests = [];

    public ExtismHostFunctions(SessionStore session, WasmHostPolicy policy)
    {
        this.session = session;
        this.policy = policy;
        variables = new BoundedStore("var_set", "the variable store", policy.VariableStoreBytes, entryLimit: null);
        irCache = new BoundedStore("morphir_cache_ir", "the IR cache", policy.IrCacheBytes, policy.IrCacheEntries);
    }

    /// <summary>The <c>http_request</c> calls the host refused, with the method and URL each asked for.</summary>
    public IReadOnlyList<RefusedHttpRequest> HttpRequests
    {
        get
        {
            lock (httpRequests)
            {
                return httpRequests.ToArray();
            }
        }
    }

    /// <summary>The callback of the host function <paramref name="module"/>.<paramref name="name"/>, or null when it is
    /// not one (a kernel or WASI function).</summary>
    public Function.UntypedCallbackDelegate? Callback(string module, string name) => (module, name) switch
    {
        (Env, "config_get") => ConfigGet,
        (Env, "var_get") => VarGet,
        (Env, "var_set") => VarSet,
        (Env, "get_log_level") => GetLogLevel,
        (Env, "http_request") => HttpRequest,
        (Env, "http_status_code") => HttpStatusCode,
        (Env, "http_headers") => HttpHeaders,
        (Env, _) when name.StartsWith("log_", StringComparison.Ordinal) && policy.LogLevels.Contains(name[4..]) => Log(name[4..]),
        (User, "morphir_get_workspace_info") => WorkspaceInfo,
        (User, "morphir_cache_ir") => CacheIr,
        (User, "morphir_get_cached_ir") => GetCachedIr,
        (User, "morphir_log") => MorphirLog,
        _ => null,
    };

    /// <summary><c>config_get(key) -> handle</c>: handle 0 for every key, since there is no configuration
    /// (<c>config.missing</c>). The key is not read.</summary>
    static void ConfigGet(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results) => results[0] = 0L;

    /// <summary><c>var_get(key) -> handle</c>: a new block with the stored value, or handle 0 for a missing key
    /// (<c>var.missing</c>).</summary>
    void VarGet(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results)
    {
        const string Name = "var_get";
        var key = session.ReadBlock(Name, arguments[0].AsInt64());
        results[0] = variables.Get(key) is { } value ? session.NewBlock(Name, value) : 0L;
    }

    /// <summary><c>var_set(key, value)</c>: stores the value for the session; value handle 0 deletes the key
    /// (<c>var.zeroValueDeletes</c>). A write that would make the keys and values more than the store's limit stops
    /// the call and changes nothing (<c>var.storeBytes</c>, <c>var.overLimit</c>).</summary>
    void VarSet(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results)
    {
        const string Name = "var_set";
        var key = session.ReadBlock(Name, arguments[0].AsInt64());
        var value = arguments[1].AsInt64();
        if (value == 0)
        {
            variables.Remove(key);
            return;
        }

        variables.Set(key, session.BlockLength(Name, value), () => session.ReadBlock(Name, value));
    }

    /// <summary><c>get_log_level() -> i32</c>: the contract's level, 2 (<c>info</c>) at the pin.</summary>
    void GetLogLevel(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results) => results[0] = policy.LogLevel;

    /// <summary><c>log_&lt;level&gt;(text)</c>: the text becomes guest output, except at a dropped level, where the call
    /// does nothing and the text is not read (<c>logging.dropped</c>, <c>logging.recorded</c>).</summary>
    Function.UntypedCallbackDelegate Log(string level)
    {
        var function = "log_" + level;
        if (policy.DroppedLogLevels.Contains(level))
        {
            return (Caller _, ReadOnlySpan<ValueBox> _, Span<ValueBox> _) => { };
        }

        return (Caller _, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> _) =>
        {
            var (text, elided) = session.ReadText(function, arguments[0].AsInt64());
            session.GuestOutput.Add(new GuestOutput(GuestOutputSource.Log, $"log {level}: {text}"), elided);
        };
    }

    /// <summary>
    /// <c>http_request(request, body) -> handle</c>: nothing. No connection is made. The host records the method and
    /// the URL of the request as guest output and in <see cref="HttpRequests"/>, then stops the call, which fails the
    /// session (<c>http.request</c>, <c>http.recorded</c>). A request that is not a JSON object, or a handle that is
    /// not a block, is refused the same way with the method <c>GET</c> and an empty URL.
    /// </summary>
    void HttpRequest(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results)
    {
        const string Name = "http_request";
        var (method, url) = ReadRequest(arguments[0].AsInt64());
        lock (httpRequests)
        {
            httpRequests.Add(new RefusedHttpRequest(method, url));
        }

        session.GuestOutput.Add(new GuestOutput(GuestOutputSource.RefusedHttp, $"refused http_request {method} {url}: the guest has no network access"));
        throw new HostCallStopped($"{Name} was refused: the guest has no network access");
    }

    /// <summary>The method and URL of an Extism HTTP request block: <c>{"url": ..., "method": ..., "headers": ...}</c>.
    /// At most 64 KiB of the block is read, and each value is cut to <see cref="GuestOutputTail.MessageBytes"/>.</summary>
    (string Method, string Url) ReadRequest(long handle)
    {
        const int MostRequestBytes = 65536;
        string method = "GET", url = string.Empty;
        if (handle == 0 || session.TryBlockLength(handle) is not { } length || length > MostRequestBytes)
        {
            return (method, url);
        }

        try
        {
            using var request = JsonDocument.Parse(session.ReadBlock("http_request", handle));
            if (request.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (request.RootElement.TryGetProperty("method", out var named) && named.ValueKind == JsonValueKind.String)
                {
                    method = Bounded(named.GetString()!);
                }

                if (request.RootElement.TryGetProperty("url", out var target) && target.ValueKind == JsonValueKind.String)
                {
                    url = Bounded(target.GetString()!);
                }
            }
        }
        catch (JsonException)
        {
            // Not a JSON request: refused all the same, with the defaults.
        }

        return (method, url);
    }

    static string Bounded(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) <= GuestOutputTail.MessageBytes)
        {
            return text;
        }

        var kept = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > GuestOutputTail.MessageBytes)
            {
                break;
            }

            kept.Append(rune.ToString());
        }

        return kept.ToString();
    }

    /// <summary><c>http_status_code() -> i32</c>: 0, since no request was made (<c>http.statusCodeWithoutRequest</c>).</summary>
    void HttpStatusCode(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results) => results[0] = policy.HttpStatusCodeWithoutRequest;

    /// <summary><c>http_headers() -> handle</c>: handle 0, since no request was made (<c>http.headersWithoutRequest</c>).</summary>
    static void HttpHeaders(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results) => results[0] = 0L;

    /// <summary><c>morphir_get_workspace_info() -> handle</c>: a new block with
    /// <c>{"root": &lt;private empty directory&gt;, "output_dir": ""}</c> (<c>workspaceInfo</c>). The directory is
    /// created on the first call and is the same for every call of the session.</summary>
    void WorkspaceInfo(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results)
    {
        const string Name = "morphir_get_workspace_info";
        string root;
        try
        {
            root = session.Workspace.Create();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new HostCallStopped($"{Name}: the session's workspace could not be created: {error.Message}");
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("root", root);
            writer.WriteString("output_dir", string.Empty);
            writer.WriteEndObject();
        }

        results[0] = session.NewBlock(Name, buffer.ToArray());
    }

    /// <summary><c>morphir_cache_ir(key, ir)</c>: stores the value for the session. A write that would make the keys
    /// and values more than the cache's byte limit, or add an entry past its entry limit, stops the call and changes
    /// nothing (<c>irCache.bytes</c>, <c>irCache.entries</c>, <c>irCache.overLimit</c>).</summary>
    void CacheIr(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results)
    {
        const string Name = "morphir_cache_ir";
        var key = session.ReadBlock(Name, arguments[0].AsInt64());
        var value = arguments[1].AsInt64();
        irCache.Set(key, session.BlockLength(Name, value), () => session.ReadBlock(Name, value));
    }

    static readonly byte[] Null = "null"u8.ToArray();

    /// <summary><c>morphir_get_cached_ir(key) -> handle</c>: a new block with the cached value, or with the JSON text
    /// <c>null</c> for a missing key (<c>irCache.missing</c>).</summary>
    void GetCachedIr(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results)
    {
        const string Name = "morphir_get_cached_ir";
        var key = session.ReadBlock(Name, arguments[0].AsInt64());
        results[0] = session.NewBlock(Name, irCache.Get(key) ?? Null);
    }

    /// <summary><c>morphir_log(level, message)</c>: the message becomes guest output whatever the level
    /// (<c>logging.morphirLog</c>, <c>unfiltered</c>).</summary>
    void MorphirLog(Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results)
    {
        const string Name = "morphir_log";
        var (level, _) = session.ReadText(Name, arguments[0].AsInt64());
        var (text, elided) = session.ReadText(Name, arguments[1].AsInt64());
        session.GuestOutput.Add(new GuestOutput(GuestOutputSource.MorphirLog, $"morphir_log {level}: {text}"), elided);
    }

    /// <summary>
    /// A store of one session with a limit on the bytes of its keys and values and, optionally, on its entries. Keys
    /// are compared as bytes. A write over a limit throws <see cref="HostCallStopped"/> and changes nothing; the value
    /// is read only after the write is known to fit.
    /// </summary>
    sealed class BoundedStore(string function, string description, long byteLimit, int? entryLimit)
    {
        readonly Dictionary<byte[], byte[]> values = new(ByteKeys.Instance);
        long bytes;

        public byte[]? Get(byte[] key) => values.GetValueOrDefault(key);

        public void Remove(byte[] key)
        {
            if (values.Remove(key, out var old))
            {
                bytes -= key.Length + old.Length;
            }
        }

        public void Set(byte[] key, long valueLength, Func<byte[]> readValue)
        {
            long previous = 0;
            if (values.TryGetValue(key, out var old))
            {
                previous = key.Length + old.Length;
            }
            else if (entryLimit is { } entries && values.Count >= entries)
            {
                throw new HostCallStopped($"{function}: {description} holds its limit of {entries} entries");
            }

            var total = bytes - previous + key.Length + valueLength;
            if (total > byteLimit)
            {
                throw new HostCallStopped(
                    $"{function}: {description} would hold {total} bytes of keys and values, over its limit of {byteLimit} bytes");
            }

            values[key] = readValue();
            bytes = total;
        }
    }

    sealed class ByteKeys : IEqualityComparer<byte[]>
    {
        public static ByteKeys Instance { get; } = new();

        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] key)
        {
            var hash = new HashCode();
            hash.AddBytes(key);
            return hash.ToHashCode();
        }
    }
}
