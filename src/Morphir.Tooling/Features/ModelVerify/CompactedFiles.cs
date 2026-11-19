// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using Morphir.IR.Files;
using Morphir.IR.Trees;
using Morphir.Tooling.Infrastructure.JsonSchema;

namespace Morphir.Tooling.Features.ModelVerify;

/// <summary>
/// The compacted files of <c>model verify</c> (cli-model-commands, "Model verify reads compacted files"): a file whose
/// top-level <c>$schema</c> marks a name-compaction envelope (<c>ir/name-compaction</c>) is read with
/// <see cref="MorphirFile"/>, which applies the context refusals and expands the document, and the plain document is
/// what the schema of its version checks. A file is compacted when its schema is one that <see cref="MorphirFile"/>
/// declares (<c>MorphirFile.IsDeclared</c>).
/// </summary>
internal static class CompactedFiles
{
    /// <summary>The expansion of a file: its plain document as JSON text, or the refusals of its envelope or context.</summary>
    /// <param name="Json">The plain document, or null when the file is refused.</param>
    /// <param name="Errors">Each refusal, with its <c>context.</c> code (or <c>invalid_json</c>); none when the file expanded.</param>
    public sealed record Expansion(string? Json, IReadOnlyList<StoreError> Errors);

    /// <summary>
    /// The <c>$schema</c> at the top level of <paramref name="json"/>, or null when it has none or is not JSON, which the
    /// checks of a plain file then report. A <c>$schema</c> that is not a string is given as its JSON text, which no
    /// declared schema equals.
    /// </summary>
    public static string? TopLevelSchema(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, SchemaValidator.ModelDocumentOptions);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$schema", out var schema)
                ? schema.ValueKind == JsonValueKind.String ? schema.GetString() ?? "" : schema.GetRawText()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The JSON Pointer of the member that holds an envelope's document.</summary>
    public const string DocumentPointer = "/document";

    /// <summary>
    /// The error with its <c>Path</c>, a JSON Pointer into an expanded document, located in the file as written: inside
    /// the envelope's <c>document</c> member (<c>""</c> becomes <c>/document</c>, <c>/def</c> becomes
    /// <c>/document/def</c>). A path that is not a pointer, such as <c>$</c>, is kept.
    /// </summary>
    public static ValidationError InDocument(ValidationError error) => error with
    {
        Path = error.Path.Length == 0 || error.Path.StartsWith('/') ? DocumentPointer + error.Path : error.Path,
    };

    /// <summary>
    /// Reads the envelope of <paramref name="json"/> and expands its document, with each diagnostic naming
    /// <paramref name="file"/>. The IR family of the expansion is the enclosed document's own.
    /// </summary>
    public static Expansion Expand(string file, string json)
    {
        return MorphirFile.TryReadPlainDocument(ContextLimitsModule.draft1, file, Encoding.UTF8.GetBytes(json), out var document, out var errors)
            ? new(document.GetRawText(), [])
            : new(null, errors);
    }
}
