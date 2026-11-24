// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Compilation;
using Morphir.Ion;
using Morphir.IR.Trees;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
using Severity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Compile;

/// <summary>
/// The serializations of the published IR. The IR writer writes JSON; YAML is the canonical YAML of that JSON (the YAML
/// profile), and Ion is the canonical Ion text of the distribution that the JSON holds (the Ion IR format).
/// </summary>
public static class CompileIrFormats
{
    /// <summary>The file name of the published IR in <c>compile.dest</c>: <c>morphir-ir.json</c>, <c>.yaml</c> or <c>.ion</c>.</summary>
    public static string FileName(IrStorageFormat format) => format switch
    {
        IrStorageFormat.Json => "morphir-ir.json",
        IrStorageFormat.Yaml => "morphir-ir.yaml",
        IrStorageFormat.Ion => "morphir-ir.ion",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>The bytes of the IR in a format, from the bytes that the JSON writer wrote.</summary>
    public static Checked<byte[]> Encode(IrStorageFormat format, byte[] json)
    {
        switch (format)
        {
            case IrStorageFormat.Json:
                return Checked<byte[]>.Pass(json);
            case IrStorageFormat.Yaml:
                return CanonicalYamlWriter.TryWriteJson(json, out var yaml, out var yamlFailure)
                    ? Checked<byte[]>.Pass(yaml)
                    : Failed($"The IR could not be written as YAML: {yamlFailure}");
            case IrStorageFormat.Ion:
                return IonIrWriting.TryWriteJson(json, out var ion, out var ionFailure)
                    ? Checked<byte[]>.Pass(ion)
                    : Failed($"The IR could not be written as Ion: {ionFailure.Message} ({ionFailure.Code})");
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    /// <summary>
    /// The written JSON in a format, with the writer's diagnostics and the encoder's. <paramref name="written"/> receives the
    /// JSON when the writer passed.
    /// </summary>
    public static Checked<byte[]> Encode(IrStorageFormat format, Checked<byte[]> json, Action<byte[]> written)
    {
        if (!json.TryGetValue(out var bytes)) return json;
        written(bytes);
        var encoded = Encode(format, bytes);
        var diagnostics = json.Diagnostics.Concat(encoded.Diagnostics);
        return encoded.TryGetValue(out var result) ? Checked<byte[]>.Of(result, diagnostics) : Checked<byte[]>.Fail(diagnostics);
    }

    /// <summary>
    /// What <c>compile --target</c> gives the model loader: the published bytes and their format, except for YAML, which
    /// the loader does not read, so it gets the JSON that the YAML was written from. Both hold the same model.
    /// </summary>
    public static (ReadOnlyMemory<byte> Bytes, InputFormat Format) GenerationInput(IrStorageFormat format, ReadOnlyMemory<byte> json,
        ReadOnlyMemory<byte> published) => format switch
        {
            IrStorageFormat.Yaml => (json, InputFormat.Json),
            IrStorageFormat.Ion => (published, InputFormat.Ion),
            _ => (published, InputFormat.Auto),
        };

    private static Checked<byte[]> Failed(string message) =>
        Checked<byte[]>.Fail([Diagnostic.Create(Severity.Error, CompileCodes.IrFormatFailed, message, null)]);
}
