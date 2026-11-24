// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using Morphir.Files;

namespace Morphir.Tooling.Features.TaskRecords;

/// <summary>The result of decoding record bytes.</summary>
public abstract record RecordDecoding
{
    private RecordDecoding() { }

    /// <summary>The bytes are a schema-1 record.</summary>
    public sealed record Decoded(TaskRecord Record) : RecordDecoding;

    /// <summary>The bytes are not a schema-1 record.</summary>
    public sealed record Invalid(string Reason) : RecordDecoding;
}

/// <summary>The result of reading a record file.</summary>
public abstract record RecordReading
{
    private RecordReading() { }

    /// <summary>There is no record file.</summary>
    public sealed record Missing : RecordReading;

    /// <summary>The record file holds a schema-1 record.</summary>
    public sealed record Found(TaskRecord Record) : RecordReading;

    /// <summary>The record file could not be read or decoded.</summary>
    public sealed record Unreadable(string Reason) : RecordReading;
}

/// <summary>
/// Reads and writes schema-1 task records with <see cref="Utf8JsonWriter"/> and <see cref="JsonDocument"/>,
/// so no reflection is used. Members are written in the upstream order; unknown members follow in ordinal order.
/// </summary>
public static class TaskRecordCodec
{
    private static readonly string[] KnownMembers =
        ["schema", "task", "module", "language", "inputs", "value", "ir", "installed", "tombstone", "completedAt"];

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Decodes record bytes. A repeated member, a wrong type or an unsafe path makes the record invalid.</summary>
    public static RecordDecoding Decode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return Decode(document.RootElement);
        }
        catch (JsonException error)
        {
            return new RecordDecoding.Invalid("The record is not JSON: " + error.Message);
        }
    }

    private static RecordDecoding Decode(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return Invalid("The record is not a JSON object.");
        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var member in root.EnumerateObject())
            if (!members.TryAdd(member.Name, member.Value))
                return Invalid($"The record has the member '{member.Name}' more than once.");

        if (!members.TryGetValue("schema", out var schema) || schema.ValueKind != JsonValueKind.Number
            || !schema.TryGetInt32(out var schemaNumber) || schemaNumber != TaskRecord.Schema)
            return Invalid($"The record schema must be {TaskRecord.Schema}.");
        if (!TaskId.TryParse(Text(members, "task"), out var task, out var taskError)) return Invalid("task: " + taskError);
        if (!MemberPath.TryParse(Text(members, "module"), out var module, out var moduleError)) return Invalid("module: " + moduleError);
        if (!RecordTimestamp.TryParse(Text(members, "completedAt"), out var completedAt))
            return Invalid("completedAt must be an RFC 3339 time.");

        string? language = null;
        if (members.TryGetValue("language", out var languageElement))
        {
            if (languageElement.ValueKind != JsonValueKind.String) return Invalid("language must be a string.");
            language = languageElement.GetString();
        }

        var inputs = ImmutableArray<string>.Empty;
        if (members.TryGetValue("inputs", out var inputsElement))
        {
            if (!Strings(inputsElement, out var values)) return Invalid("inputs must be an array of strings.");
            inputs = values;
        }

        var value = ImmutableArray<RecordPath>.Empty;
        if (members.TryGetValue("value", out var valueElement))
        {
            if (!Paths(valueElement, out var paths, out var error)) return Invalid("value: " + error);
            value = paths;
        }

        IrDescriptor? ir = null;
        if (members.TryGetValue("ir", out var irElement))
        {
            if (!Descriptor(irElement, out ir, out var error)) return Invalid("ir: " + error);
        }

        var installed = ImmutableSortedDictionary.CreateBuilder<string, ImmutableArray<RecordPath>>(StringComparer.Ordinal);
        if (members.TryGetValue("installed", out var installedElement))
        {
            if (installedElement.ValueKind != JsonValueKind.Object) return Invalid("installed must be an object.");
            foreach (var target in installedElement.EnumerateObject())
            {
                if (!Paths(target.Value, out var files, out var error)) return Invalid($"installed[{target.Name}]: {error}");
                if (installed.ContainsKey(target.Name)) return Invalid($"installed names {target.Name} more than once.");
                installed[target.Name] = files;
            }
        }

        var tombstone = false;
        if (members.TryGetValue("tombstone", out var tombstoneElement))
        {
            if (tombstoneElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Invalid("tombstone must be a Boolean.");
            tombstone = tombstoneElement.GetBoolean();
        }

        var unknown = members
            .Where(member => !KnownMembers.Contains(member.Key, StringComparer.Ordinal))
            .OrderBy(member => member.Key, StringComparer.Ordinal)
            .Select(member => KeyValuePair.Create(member.Key, member.Value.Clone()))
            .ToImmutableArray();

        return new RecordDecoding.Decoded(new TaskRecord
        {
            Task = task,
            Module = module,
            Language = language,
            Inputs = inputs,
            Value = value,
            Ir = ir,
            Installed = installed.ToImmutable(),
            Tombstone = tombstone,
            CompletedAt = completedAt,
            Unknown = unknown,
        });

        static RecordDecoding Invalid(string reason) => new RecordDecoding.Invalid(reason);
    }

    private static string? Text(Dictionary<string, JsonElement> members, string name) =>
        members.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static bool Strings(JsonElement element, out ImmutableArray<string> values)
    {
        values = [];
        if (element.ValueKind != JsonValueKind.Array) return false;
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return false;
            builder.Add(item.GetString()!);
        }
        values = builder.ToImmutable();
        return true;
    }

    private static bool Paths(JsonElement element, out ImmutableArray<RecordPath> paths, out string error)
    {
        paths = [];
        if (!Strings(element, out var texts))
        {
            error = "must be an array of strings.";
            return false;
        }
        var builder = ImmutableArray.CreateBuilder<RecordPath>(texts.Length);
        foreach (var text in texts)
        {
            if (!RecordPath.TryParse(text, out var path, out var pathError))
            {
                error = pathError;
                return false;
            }
            builder.Add(path);
        }
        paths = builder.MoveToImmutable();
        error = "";
        return true;
    }

    private static bool Descriptor(JsonElement element, out IrDescriptor? descriptor, out string error)
    {
        descriptor = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "must be an object.";
            return false;
        }
        string? Member(string name) =>
            element.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String ? member.GetString() : null;

        if (!RecordPath.TryParse(Member("path"), out var path, out var pathError))
        {
            error = "path: " + pathError;
            return false;
        }
        if (!IrStorageNames.TryParseLayout(Member("layout"), out var layout))
        {
            error = "layout must be single-file or document-tree.";
            return false;
        }
        if (!IrStorageNames.TryParseFormat(Member("format"), out var format))
        {
            error = "format must be json, yaml or ion.";
            return false;
        }
        if (!IrDescriptor.TryParseVersion(Member("version"), out var version))
        {
            error = "version must be 'v' and then a format version, for example v4.";
            return false;
        }
        descriptor = new IrDescriptor(path, layout, format, version);
        error = "";
        return true;
    }

    /// <summary>Encodes a record as indented UTF-8 JSON with line feeds.</summary>
    public static byte[] Encode(TaskRecord record)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", TaskRecord.Schema);
            writer.WriteString("task", record.Task.Value);
            writer.WriteString("module", record.Module.Value);
            if (record.Language is { } language) writer.WriteString("language", language);
            writer.WriteStartArray("inputs");
            foreach (var input in record.Inputs) writer.WriteStringValue(input);
            writer.WriteEndArray();
            WritePaths(writer, "value", record.Value);
            if (record.Ir is { } ir)
            {
                writer.WriteStartObject("ir");
                writer.WriteString("path", ir.Path.Value);
                writer.WriteString("layout", IrStorageNames.Name(ir.Layout));
                writer.WriteString("format", IrStorageNames.Name(ir.Format));
                writer.WriteString("version", ir.Spelling);
                writer.WriteEndObject();
            }
            writer.WriteStartObject("installed");
            foreach (var (target, files) in record.Installed) WritePaths(writer, target, files);
            writer.WriteEndObject();
            if (record.Tombstone) writer.WriteBoolean("tombstone", true);
            writer.WriteString("completedAt", record.CompletedAt.Text);
            foreach (var (name, value) in record.Unknown)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static void WritePaths(Utf8JsonWriter writer, string name, ImmutableArray<RecordPath> paths)
    {
        writer.WriteStartArray(name);
        foreach (var path in paths) writer.WriteStringValue(path.Value);
        writer.WriteEndArray();
    }
}

/// <summary>Reads and writes record files. A write goes through a temporary file and a rename.</summary>
/// <remarks>On Windows the rename waits a bounded time while another process (a virus scanner or a search indexer)
/// holds the record open (<see cref="FileReplacement"/>). The writer holds the task's lock, so no other Morphir run
/// writes the record meanwhile; anything that changes it during the wait is outside that protocol. The rename then
/// does not go ahead, so the write cannot drop what that writer wrote, and the write fails as an I/O error.</remarks>
internal static class RecordFile
{
    public static RecordReading Read(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new RecordReading.Missing();
        }
        catch (Exception error) when (TaskFailures.IsIo(error))
        {
            return new RecordReading.Unreadable(error.Message);
        }
        return TaskRecordCodec.Decode(bytes) switch
        {
            RecordDecoding.Decoded decoded => new RecordReading.Found(decoded.Record),
            RecordDecoding.Invalid invalid => new RecordReading.Unreadable(invalid.Reason),
            _ => throw new InvalidOperationException("Unknown record decoding."),
        };
    }

    public static void Write(string path, TaskRecord record) => Write(path, record, afterWait: null);

    /// <param name="afterWait">Runs after each wait for another handle, before the record is checked again. Tests
    /// use it to release or change the record without timing.</param>
    internal static void Write(string path, TaskRecord record, Action? afterWait) =>
        Write(path, record, afterWait, CancellationToken.None, beforeReplace: null);

    /// <param name="ct">Checked before the record is staged and immediately before each attempt to replace the record: a
    /// cancellation throws <see cref="OperationCanceledException"/>, removes the staged record and leaves the record.</param>
    /// <param name="beforeReplace">Runs before each attempt to replace the record, for tests.</param>
    internal static void Write(string path, TaskRecord record, Action? afterWait, CancellationToken ct, Action? beforeReplace) =>
        Write(path, record, afterWait, ct, beforeReplace, PathRemoval.Default);

    /// <param name="cleanup">Removes the staged record when the write fails or is cancelled. Tests replace it.</param>
    internal static void Write(string path, TaskRecord record, Action? afterWait, CancellationToken ct, Action? beforeReplace,
        PathRemoval cleanup)
    {
        ct.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(TaskRecordCodec.Encode(record));
                stream.Flush(flushToDisk: true);
            }
            var before = Snapshot(path);
            var replaced = FileReplacement.Replace(() =>
            {
                beforeReplace?.Invoke();
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }, () =>
            {
                afterWait?.Invoke();
                var now = Snapshot(path);
                return now is null ? before is null : before is not null && now.AsSpan().SequenceEqual(before);
            });
            if (!replaced) throw new IOException($"The task record {path} changed while its replacement waited for another handle");
        }
        catch
        {
            // A replaced record moved the staged one; only a failed or cancelled write still has it. Its removal never
            // hides the failure or the cancellation.
            cleanup.TryRemove(temporary);
            throw;
        }
    }

    // The bytes of the record, or null when there is none.
    private static byte[]? Snapshot(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }
}

/// <summary>
/// Removes a file or a directory that another handle may hold for a short time. On Windows a virus scanner or a search
/// indexer opens a file just after it is written, without delete sharing, so a delete that another handle refuses waits
/// and tries again, up to <see cref="FileReplacement.Attempts"/> times. It throws only for a cancellation of the token
/// that a caller gives it, so the failure or the cancellation of the work that it cleans up after stands;
/// <see cref="TryRemove"/> says whether the path is gone.
/// </summary>
/// <param name="Delete">Deletes the file or the directory with its content.</param>
/// <param name="IsTransient">Whether a failure of the delete is one that another handle causes, which a later attempt can pass.</param>
/// <param name="Wait">Waits between attempts.</param>
internal sealed record PathRemoval(Action<string> Delete, Func<Exception, bool> IsTransient, Action<TimeSpan> Wait)
{
    /// <summary>The removal of the file system: <see cref="DeletePath"/>, the handle check of <see cref="FileReplacement"/>, and a sleep.</summary>
    public static PathRemoval Default { get; } = new(DeletePath, FileReplacement.IsHeldByAnotherHandle, Thread.Sleep);

    /// <summary>Deletes a directory with its content, or a file.</summary>
    public static void DeletePath(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    /// <summary>
    /// Removes what is at <paramref name="path"/>, as far as the file system lets it. True when nothing is there after. A
    /// cancellation of <paramref name="ct"/> during a wait throws <see cref="OperationCanceledException"/>; a cleanup after a
    /// failure or a cancellation gives no token, so it always runs to its end.
    /// </summary>
    public bool TryRemove(string path, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Exists(path)) Delete(path);
                return !Exists(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (attempt >= FileReplacement.Attempts || !IsTransient(error)) return false;
                Wait(TimeSpan.FromMilliseconds(Math.Min(20 << (attempt - 1), 500)));
                ct.ThrowIfCancellationRequested();
            }
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
