// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Morphir.Compilation;
using Morphir.IR;
using Morphir.IR.Model;

namespace Morphir.Tooling.Features.TaskRecords;

/// <summary>How a task stores its IR inside <c>.dest</c>.</summary>
public enum IrLayout
{
    /// <summary>One file holds the whole distribution (<c>single-file</c>).</summary>
    SingleFile,

    /// <summary>A directory tree of documents with a manifest at its root (<c>document-tree</c>).</summary>
    DocumentTree,
}

/// <summary>The serialization format of stored IR.</summary>
public enum IrStorageFormat
{
    /// <summary>JSON (<c>json</c>).</summary>
    Json,

    /// <summary>YAML (<c>yaml</c>).</summary>
    Yaml,

    /// <summary>Ion (<c>ion</c>).</summary>
    Ion,
}

/// <summary>The record spellings of <see cref="IrLayout"/> and <see cref="IrStorageFormat"/>.</summary>
public static class IrStorageNames
{
    /// <summary>The spelling of a layout in a record.</summary>
    public static string Name(IrLayout layout) => layout switch
    {
        IrLayout.SingleFile => "single-file",
        IrLayout.DocumentTree => "document-tree",
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    /// <summary>The spelling of a format in a record.</summary>
    public static string Name(IrStorageFormat format) => format switch
    {
        IrStorageFormat.Json => "json",
        IrStorageFormat.Yaml => "yaml",
        IrStorageFormat.Ion => "ion",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Parses the spelling of a layout. Only <c>single-file</c> and <c>document-tree</c> are layouts.</summary>
    public static bool TryParseLayout(string? text, out IrLayout layout)
    {
        (var found, layout) = text switch
        {
            "single-file" => (true, IrLayout.SingleFile),
            "document-tree" => (true, IrLayout.DocumentTree),
            _ => (false, default),
        };
        return found;
    }

    /// <summary>Parses the spelling of a format.</summary>
    public static bool TryParseFormat(string? text, out IrStorageFormat format)
    {
        (var found, format) = text switch
        {
            "json" => (true, IrStorageFormat.Json),
            "yaml" => (true, IrStorageFormat.Yaml),
            "ion" => (true, IrStorageFormat.Ion),
            _ => (false, default),
        };
        return found;
    }
}

/// <summary>
/// Where and how a task stored its IR. The version is a parsed format version (VER-1). A record spells it
/// with a leading <c>v</c>, as the upstream records do (<c>v4</c>); <see cref="SourceFormatVersion.Text"/> keeps
/// the rest of the recorded spelling.
/// </summary>
public sealed record IrDescriptor(RecordPath Path, IrLayout Layout, IrStorageFormat Format, SourceFormatVersion Version)
{
    /// <summary>A descriptor for IR of the produced version, with the upstream spelling of that version.</summary>
    public static IrDescriptor Create(RecordPath path, IrLayout layout, IrStorageFormat format, Versioning.FormatVersion produced) =>
        new(path, layout, format, SourceFormatVersion.Create(FormatVersion.CanonicalText(produced)));

    /// <summary>The version as a record writes it, for example <c>v4</c> or <c>v4.1.0-draft.1</c>.</summary>
    public string Spelling => "v" + Version.Text;

    /// <summary>Parses a recorded version spelling: <c>v</c> and then a format version.</summary>
    public static bool TryParseVersion(string? text, [NotNullWhen(true)] out SourceFormatVersion? version)
    {
        version = null;
        if (text is not { Length: > 1 } || text[0] != 'v') return false;
        if (!SourceFormatVersion.TryParse(text[1..], out var parsed, out _)) return false;
        version = parsed;
        return true;
    }
}

/// <summary>
/// The time of a task's last successful run, as an RFC 3339 UTC text. The text is kept exactly, so that a
/// tombstone keeps the time of the run that it replaces.
/// </summary>
public sealed record RecordTimestamp
{
    private RecordTimestamp(string text, DateTimeOffset value)
    {
        Text = text;
        Value = value;
    }

    /// <summary>The recorded text.</summary>
    public string Text { get; }

    /// <summary>The parsed time.</summary>
    public DateTimeOffset Value { get; }

    /// <summary>The time <paramref name="now"/>, written with second precision as the upstream records are.</summary>
    public static RecordTimestamp At(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var seconds = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, TimeSpan.Zero);
        return new(seconds.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), seconds);
    }

    /// <summary>Parses an RFC 3339 time with an offset.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out RecordTimestamp? timestamp)
    {
        timestamp = null;
        if (text is null || text.Length < 20 || text[10] != 'T' && text[10] != 't') return false;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)) return false;
        timestamp = new(text, value);
        return true;
    }
}

/// <summary>
/// One schema-1 task result record: the <c>.json</c> file beside a task's <c>.dest</c>. Members that this
/// version does not know are kept in <see cref="Unknown"/> and written back.
/// </summary>
public sealed record TaskRecord
{
    /// <summary>The record schema that this store reads and writes.</summary>
    public const int Schema = 1;

    /// <summary>The task that wrote the record.</summary>
    public required TaskId Task { get; init; }

    /// <summary>The member path, empty for the root module.</summary>
    public required MemberPath Module { get; init; }

    /// <summary>The source language of a compile record.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// The inputs of the run. Upstream records list task ids; a compile of a project file also lists the
    /// project file, relative to the workspace root.
    /// </summary>
    public ImmutableArray<string> Inputs { get; init; } = [];

    /// <summary>The product: paths relative to <c>.dest</c>. <c>-o</c> installs these entries.</summary>
    public ImmutableArray<RecordPath> Value { get; init; } = [];

    /// <summary>The IR descriptor of a task that produces IR.</summary>
    public IrDescriptor? Ir { get; init; }

    /// <summary>
    /// The install ledger: each absolute target that <c>-o</c> named, and the files that install wrote there,
    /// relative to that target.
    /// </summary>
    public ImmutableSortedDictionary<string, ImmutableArray<RecordPath>> Installed { get; init; } =
        ImmutableSortedDictionary.Create<string, ImmutableArray<RecordPath>>(StringComparer.Ordinal);

    /// <summary>True when a later run started and the task has no product.</summary>
    public bool Tombstone { get; init; }

    /// <summary>The time of the last successful run.</summary>
    public required RecordTimestamp CompletedAt { get; init; }

    /// <summary>Members that this version does not know, in ordinal order of their names.</summary>
    public ImmutableArray<KeyValuePair<string, JsonElement>> Unknown { get; init; } = [];

    /// <summary>
    /// The tombstone that replaces this record when a run starts: <c>value</c> and <c>inputs</c> empty,
    /// <c>language</c>, <c>ir</c> and unknown members removed, and <c>installed</c> and <c>completedAt</c> kept.
    /// </summary>
    public TaskRecord ToTombstone() => this with
    {
        Tombstone = true,
        Value = [],
        Inputs = [],
        Language = null,
        Ir = null,
        Unknown = [],
    };
}
