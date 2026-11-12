// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Wasmtime;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// WASI preview 1, <c>wasi_snapshot_preview1</c>, as the pinned contract's sandbox defines it, in managed code (task
/// 3.4; see the library's README for why the engine's own WASI is not used). Every WASI import of a guest is linked
/// here, by name, with the type the classifier already checked against the engine's WASI definitions.
/// </summary>
/// <remarks>
/// <para>What each call gives, from the contract's WASI table and its <c>policy.wasi</c> values:</para>
/// <list type="table">
/// <item><term><c>args_sizes_get</c>, <c>environ_sizes_get</c></term><description>Success, with a count and a size of 0.</description></item>
/// <item><term><c>fd_read</c> on fd 0</term><description>Success, with 0 bytes read: stdin is empty.</description></item>
/// <item><term>A function on an fd above 2</term><description>Errno 8 (<c>badf</c>): there is no preopened directory.</description></item>
/// <item><term><c>clock_time_get</c>, <c>clock_res_get</c>, <c>random_get</c></term><description>Real values.</description></item>
/// <item><term><c>fd_write</c> on fd 1 or 2</term><description>Success; the bytes become guest output.</description></item>
/// <item><term><c>proc_exit</c></term><description>Ends the call: with 0 it succeeds, with another code the session fails.</description></item>
/// <item><term>Anything else, <c>poll_oneoff</c> and <c>path_open</c> on fd 0 among them</term><description>Errno 76 (<c>notcapable</c>), at once.</description></item>
/// </list>
/// <para>
/// No call reads the host's arguments, environment, stdin or file system, and none blocks. Where the contract is
/// silent this sandbox follows WASI itself: a pointer outside the guest's memory gives errno 21 (<c>fault</c>) and
/// a clock id outside the four of preview 1 gives errno 28 (<c>inval</c>). The process and thread CPU-time clocks
/// read the monotonic clock, as the MoonBit host's sandbox does. Calls answered with errno 8 or 76 are counted by
/// name in <see cref="RefusedCalls"/>.
/// </para>
/// </remarks>
internal sealed class WasiSandbox
{
    public const string Module = "wasi_snapshot_preview1";

    const int Success = 0;
    const int Fault = 21;
    const int Inval = 28;
    const string StdoutPrefix = "stdout: ";
    const string StderrPrefix = "stderr: ";

    /// <summary>The positions of the fd parameters of the preview 1 functions that take one, from the WASI preview 1
    /// witx. A function not listed takes no fd.</summary>
    static readonly FrozenDictionary<string, int[]> FdParameters = new Dictionary<string, int[]>(StringComparer.Ordinal)
    {
        ["fd_advise"] = [0],
        ["fd_allocate"] = [0],
        ["fd_close"] = [0],
        ["fd_datasync"] = [0],
        ["fd_fdstat_get"] = [0],
        ["fd_fdstat_set_flags"] = [0],
        ["fd_fdstat_set_rights"] = [0],
        ["fd_filestat_get"] = [0],
        ["fd_filestat_set_size"] = [0],
        ["fd_filestat_set_times"] = [0],
        ["fd_pread"] = [0],
        ["fd_prestat_get"] = [0],
        ["fd_prestat_dir_name"] = [0],
        ["fd_pwrite"] = [0],
        ["fd_read"] = [0],
        ["fd_readdir"] = [0],
        ["fd_renumber"] = [0, 1],
        ["fd_seek"] = [0],
        ["fd_sync"] = [0],
        ["fd_tell"] = [0],
        ["fd_write"] = [0],
        ["path_create_directory"] = [0],
        ["path_filestat_get"] = [0],
        ["path_filestat_set_times"] = [0],
        ["path_link"] = [0, 4],
        ["path_open"] = [0],
        ["path_readlink"] = [0],
        ["path_remove_directory"] = [0],
        ["path_rename"] = [0, 3],
        ["path_symlink"] = [2],
        ["path_unlink_file"] = [0],
        ["sock_accept"] = [0],
        ["sock_recv"] = [0],
        ["sock_send"] = [0],
        ["sock_shutdown"] = [0],
    }.ToFrozenDictionary(StringComparer.Ordinal);

    readonly WasmHostPolicy policy;
    readonly GuestOutputTail output;
    readonly string? memoryName;
    readonly Dictionary<string, long> refused = new(StringComparer.Ordinal);

    /// <param name="policy">The contract's policy, for the two errnos.</param>
    /// <param name="output">The session's guest output.</param>
    /// <param name="memoryName">The export through which the host reads the guest's memory, or null when the guest
    /// has none.</param>
    public WasiSandbox(WasmHostPolicy policy, GuestOutputTail output, string? memoryName)
    {
        this.policy = policy;
        this.output = output;
        this.memoryName = memoryName;
    }

    /// <summary>How many calls of each WASI function were answered with errno 8 (<c>badf</c>) or 76
    /// (<c>notcapable</c>): the capabilities the guest asked for and did not get.</summary>
    public IReadOnlyDictionary<string, long> RefusedCalls
    {
        get
        {
            lock (refused)
            {
                return new Dictionary<string, long>(refused, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>The callback for the WASI function <paramref name="name"/>.</summary>
    public Function.UntypedCallbackDelegate Callback(string name)
    {
        if (name == "proc_exit")
        {
            return static (Caller _, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> _) => throw new GuestExited(arguments[0].AsInt32());
        }

        Func<Caller, ReadOnlySpan<ValueBox>, int> answer = name switch
        {
            "args_sizes_get" or "environ_sizes_get" => EmptySizes,
            "clock_time_get" => ClockTime,
            "clock_res_get" => ClockResolution,
            "random_get" => RandomBytes,
            "fd_read" => Read,
            "fd_write" => Write,
            _ => (_, _) => policy.UnsupportedErrno,
        };
        FdParameters.TryGetValue(name, out var fds);
        return (Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results) =>
        {
            var errno = AnyFdAboveTwo(fds, arguments) ? policy.FdAboveTwoErrno : answer(caller, arguments);
            if (errno == policy.FdAboveTwoErrno || errno == policy.UnsupportedErrno)
            {
                lock (refused)
                {
                    refused[name] = refused.GetValueOrDefault(name) + 1;
                }
            }

            if (results.Length > 0)
            {
                results[0] = errno;
            }
        };
    }

    static bool AnyFdAboveTwo(int[]? fds, ReadOnlySpan<ValueBox> arguments)
    {
        if (fds is null)
        {
            return false;
        }

        foreach (var index in fds)
        {
            if ((uint)arguments[index].AsInt32() > 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The guest's memory, as the calling instance exports it to the host, or null.</summary>
    Memory? GuestMemory(Caller caller) => memoryName is null ? null : caller.GetMemory(memoryName);

    /// <summary>Whether <paramref name="length"/> bytes at <paramref name="pointer"/> lie inside the memory.</summary>
    static bool Inside(Memory memory, uint pointer, ulong length) => pointer + length <= (ulong)memory.GetLength();

    static bool TryWriteU32(Memory? memory, uint pointer, uint value)
    {
        if (memory is null || !Inside(memory, pointer, 4))
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(memory.GetSpan(pointer, 4), value);
        return true;
    }

    static bool TryWriteU64(Memory? memory, uint pointer, ulong value)
    {
        if (memory is null || !Inside(memory, pointer, 8))
        {
            return false;
        }

        BinaryPrimitives.WriteUInt64LittleEndian(memory.GetSpan(pointer, 8), value);
        return true;
    }

    /// <summary><c>args_sizes_get</c> and <c>environ_sizes_get</c>: a count and a size of 0, whatever the host
    /// process has (<c>wasi.arguments</c>, <c>wasi.environment</c>).</summary>
    int EmptySizes(Caller caller, ReadOnlySpan<ValueBox> arguments)
    {
        var memory = GuestMemory(caller);
        var count = (uint)arguments[0].AsInt32();
        var size = (uint)arguments[1].AsInt32();
        if (memory is null || !Inside(memory, count, 4) || !Inside(memory, size, 4))
        {
            return Fault;
        }

        TryWriteU32(memory, count, 0);
        TryWriteU32(memory, size, 0);
        return Success;
    }

    static readonly long StartTimestamp = Stopwatch.GetTimestamp();

    /// <summary>Nanoseconds of the monotonic clock: the high-resolution timestamp since this library first read it,
    /// plus one, so it is never 0 and never goes back.</summary>
    static ulong MonotonicNanoseconds()
    {
        var ticks = Stopwatch.GetTimestamp() - StartTimestamp;
        var frequency = Stopwatch.Frequency;
        return (ulong)(ticks / frequency * 1_000_000_000L + ticks % frequency * 1_000_000_000L / frequency) + 1;
    }

    static ulong RealtimeNanoseconds() => (ulong)(DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100UL;

    /// <summary><c>clock_time_get(id, precision, time)</c>: the realtime clock (id 0) or the monotonic clock (ids 1
    /// to 3), in nanoseconds (<c>wasi.clocks</c>).</summary>
    int ClockTime(Caller caller, ReadOnlySpan<ValueBox> arguments)
    {
        var id = (uint)arguments[0].AsInt32();
        if (id > 3)
        {
            return Inval;
        }

        var now = id == 0 ? RealtimeNanoseconds() : MonotonicNanoseconds();
        return TryWriteU64(GuestMemory(caller), (uint)arguments[2].AsInt32(), now) ? Success : Fault;
    }

    /// <summary><c>clock_res_get(id, resolution)</c>: 100 ns for the realtime clock, the timestamp's resolution for
    /// the others.</summary>
    int ClockResolution(Caller caller, ReadOnlySpan<ValueBox> arguments)
    {
        var id = (uint)arguments[0].AsInt32();
        if (id > 3)
        {
            return Inval;
        }

        var resolution = id == 0 ? 100UL : (ulong)Math.Max(1L, 1_000_000_000L / Stopwatch.Frequency);
        return TryWriteU64(GuestMemory(caller), (uint)arguments[1].AsInt32(), resolution) ? Success : Fault;
    }

    /// <summary><c>random_get(buffer, length)</c>: random bytes from the system's generator (<c>wasi.random</c>).</summary>
    int RandomBytes(Caller caller, ReadOnlySpan<ValueBox> arguments)
    {
        var memory = GuestMemory(caller);
        var pointer = (uint)arguments[0].AsInt32();
        var length = (uint)arguments[1].AsInt32();
        if (memory is null || !Inside(memory, pointer, length))
        {
            return length == 0 ? Success : Fault;
        }

        RandomNumberGenerator.Fill(memory.GetSpan(pointer, checked((int)length)));
        return Success;
    }

    /// <summary><c>fd_read(fd, iovs, count, nread)</c>: on stdin, 0 bytes read (<c>wasi.stdin</c>, <c>empty</c>); on
    /// stdout or stderr, errno 76. An fd above 2 is answered before this runs.</summary>
    int Read(Caller caller, ReadOnlySpan<ValueBox> arguments)
    {
        if (arguments[0].AsInt32() != 0)
        {
            return policy.UnsupportedErrno;
        }

        var memory = GuestMemory(caller);
        var vectors = (uint)arguments[1].AsInt32();
        var count = (uint)arguments[2].AsInt32();
        if (memory is null || !Inside(memory, vectors, count * 8UL))
        {
            return Fault;
        }

        return TryWriteU32(memory, (uint)arguments[3].AsInt32(), 0) ? Success : Fault;
    }

    /// <summary>
    /// <c>fd_write(fd, iovs, count, nwritten)</c>: on stdout or stderr, every byte is accepted and becomes guest output
    /// (<c>wasi.stdout</c>, <c>wasi.stderr</c>), never part of a reply. At most
    /// <see cref="GuestOutputTail.PayloadBytes"/> of one write are read; the rest are counted as dropped. On stdin,
    /// errno 76.
    /// </summary>
    int Write(Caller caller, ReadOnlySpan<ValueBox> arguments)
    {
        var fd = arguments[0].AsInt32();
        if (fd is not (1 or 2))
        {
            return policy.UnsupportedErrno;
        }

        var memory = GuestMemory(caller);
        var vectors = (uint)arguments[1].AsInt32();
        var count = (uint)arguments[2].AsInt32();
        if (memory is null || !Inside(memory, vectors, count * 8UL))
        {
            return Fault;
        }

        ulong total = 0;
        for (var index = 0u; index < count; index++)
        {
            var vector = memory.GetSpan(vectors + index * 8, 8);
            var buffer = BinaryPrimitives.ReadUInt32LittleEndian(vector);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(vector[4..]);
            if (!Inside(memory, buffer, length))
            {
                return Fault;
            }

            total += length;
        }

        if (total > uint.MaxValue)
        {
            return Inval;
        }

        var taken = (int)Math.Min(total, GuestOutputTail.PayloadBytes);
        var payload = new byte[taken];
        var filled = 0;
        for (var index = 0u; index < count && filled < taken; index++)
        {
            var vector = memory.GetSpan(vectors + index * 8, 8);
            var buffer = BinaryPrimitives.ReadUInt32LittleEndian(vector);
            var length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(vector[4..]), (uint)(taken - filled));
            memory.GetSpan(buffer, length).CopyTo(payload.AsSpan(filled));
            filled += length;
        }

        if (!TryWriteU32(memory, (uint)arguments[3].AsInt32(), (uint)total))
        {
            return Fault;
        }

        if (total > 0)
        {
            var (source, prefix) = fd == 1 ? (GuestOutputSource.Stdout, StdoutPrefix) : (GuestOutputSource.Stderr, StderrPrefix);
            output.Add(new GuestOutput(source, prefix + Encoding.UTF8.GetString(payload)), (long)total - taken);
        }

        return Success;
    }
}
