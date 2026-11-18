// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Morphir.Tooling.Features.ModelConvert;

/// <summary>The indented JSON contract of <c>--json=document</c>; the records of <c>--json</c> are compact.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ConvertReport))]
[JsonSerializable(typeof(ConvertDiagnostic[]))]
internal partial class ConvertJsonContext : JsonSerializerContext
{
    /// <summary>The context with relaxed escaping: stdout is not HTML, so quotes and apostrophes stay readable.</summary>
    public static ConvertJsonContext Relaxed { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
}
