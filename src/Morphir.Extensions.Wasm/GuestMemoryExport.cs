// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Wasm;

/// <summary>
/// Makes a guest's own memory readable by the host when the module does not export it. Wasmtime gives the host
/// a memory only through an export, and the memory rules need the guest's memory size on every failure (parent
/// decision BND-2). For a module that defines a memory and does not export it, the host runs a copy of the
/// module whose export section has one more entry: memory 0 under <see cref="Name"/>. Nothing else changes, so
/// the copy runs the same code with the same memory; classification, the digest and every report use the
/// original bytes. This reads section headers, the memory section's count and the export section's count, and
/// is not a WASM parser: the engine has already validated the original bytes.
/// </summary>
internal static class GuestMemoryExport
{
    /// <summary>The export name the host gives the guest's memory, when the module has no export of that name.</summary>
    internal const string Name = "morphir:host/guest-memory";

    const byte MemorySection = 5;
    const byte ExportSection = 7;
    const byte MemoryKind = 2;

    /// <summary>
    /// The module with its memory 0 exported under <paramref name="name"/>, or null when it defines no memory
    /// or has no export section. A module with an imported memory is never given here: classification refuses
    /// every import that is not a function.
    /// </summary>
    internal static byte[]? Exported(ReadOnlySpan<byte> module, string name)
    {
        int? exports = null;
        var exportsHeader = 0;
        var exportsEnd = 0;
        var definesMemory = false;
        var position = 8; // magic and version
        while (position < module.Length)
        {
            var header = position;
            var id = module[position++];
            var size = (int)ReadUnsigned(module, ref position);
            var start = position;
            if (id == MemorySection)
            {
                var payload = start;
                definesMemory = ReadUnsigned(module, ref payload) > 0;
            }
            else if (id == ExportSection)
            {
                exports = start;
                exportsHeader = header;
                exportsEnd = start + size;
            }

            position = start + size;
        }

        if (!definesMemory || exports is not { } exportStart)
        {
            return null;
        }

        var countEnd = exportStart;
        var count = ReadUnsigned(module, ref countEnd);
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var payloadBytes = new List<byte>();
        WriteUnsigned(payloadBytes, count + 1);
        payloadBytes.AddRange(module[countEnd..exportsEnd]);
        WriteUnsigned(payloadBytes, (ulong)nameBytes.Length);
        payloadBytes.AddRange(nameBytes);
        payloadBytes.Add(MemoryKind);
        WriteUnsigned(payloadBytes, 0);

        var rewritten = new List<byte>(module.Length + nameBytes.Length + 16);
        rewritten.AddRange(module[..exportsHeader]);
        rewritten.Add(ExportSection);
        WriteUnsigned(rewritten, (ulong)payloadBytes.Count);
        rewritten.AddRange(payloadBytes);
        rewritten.AddRange(module[exportsEnd..]);
        return [.. rewritten];
    }

    static ulong ReadUnsigned(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        var shift = 0;
        while (true)
        {
            var current = bytes[position++];
            value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }
    }

    static void WriteUnsigned(List<byte> bytes, ulong value)
    {
        do
        {
            var current = (byte)(value & 0x7f);
            value >>= 7;
            bytes.Add(value == 0 ? current : (byte)(current | 0x80));
        }
        while (value != 0);
    }
}
