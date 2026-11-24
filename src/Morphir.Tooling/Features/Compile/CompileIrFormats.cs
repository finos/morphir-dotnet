// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Compilation;
using Morphir.Ion;
using Morphir.IR.Trees;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
using Severity = Morphir.Compilation.DiagnosticSeverity;
using FormatVersion = Morphir.IR.Versioning.FormatVersion;

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

    /// <summary>
    /// The version to resolve the JSON writer for, so that a requested IR version reaches the format that serves it. Ion
    /// states <c>3.1.0</c> for a v3 library, which the JSON writer of IR 3 writes and the JSON profile cannot state, so
    /// Ion at <c>3.1.0</c> writes JSON at <c>3</c>. Every other request is the requested version.
    /// </summary>
    public static FormatVersion WriterVersion(IrStorageFormat format, FormatVersion requested) =>
        format == IrStorageFormat.Ion ? IonIrWriting.JsonWriterVersion(requested) : requested;

    /// <summary>
    /// The options of the JSON writer for a format. Ion holds a v3 doc as a string, and reads a member without one as the
    /// empty doc, so for Ion a member with no documentation is written with the empty doc. JSON and YAML keep the layout.
    /// </summary>
    public static Morphir.Compilation.WriteOptions WriterOptions(IrStorageFormat format, Morphir.Compilation.WriteOptions layout) =>
        format == IrStorageFormat.Ion ? layout.WithAbsentDocumentation(AbsentDocumentation.EmptyWrapper) : layout;

    /// <summary>
    /// The bytes of the IR in a format, from the bytes that the JSON writer wrote. Ion states the exact release of
    /// <paramref name="requested"/>, such as <c>3.1.0</c>, in its header.
    /// </summary>
    public static Checked<byte[]> Encode(IrStorageFormat format, byte[] json, FormatVersion requested)
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
                return IonIrWriting.TryWriteJson(json, requested, out var ion, out var ionFailure)
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
    public static Checked<byte[]> Encode(IrStorageFormat format, Checked<byte[]> json, FormatVersion requested, Action<byte[]> written)
    {
        if (!json.TryGetValue(out var bytes)) return json;
        written(bytes);
        var encoded = Encode(format, bytes, requested);
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
