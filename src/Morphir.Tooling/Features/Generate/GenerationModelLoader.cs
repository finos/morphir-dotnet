using System.Text.Json;
using Microsoft.FSharp.Collections;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;
using Morphir.Ion;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Decodes only an owned capture, retaining its exact bytes beside the semantic view.</summary>
public sealed class GenerationModelLoader(BoundedModelCapture capture) : IModelLoader
{
    private sealed class InputLimitException : Exception;

    public async ValueTask<Outcome<LoadedModel>> LoadAsync(ModelInput input, ModelReadLimits limits, CancellationToken ct)
    {
        var captured = await capture.CaptureAsync(input, limits, ct);
        if (captured is Outcome<RetainedInput>.Failure failed) return new Outcome<LoadedModel>.Failure(failed.Error);
        return Decode(((Outcome<RetainedInput>.Success)captured).Value, input, limits, ct);
    }

    /// <summary>
    /// Decodes an owned capture exactly as <see cref="LoadAsync"/> decodes the capture of <paramref name="input"/>.
    /// <c>compile --target</c> gives it the bytes that the compile wrote (CLI-2), so the backend sees the same
    /// <see cref="LoadedModel"/> as <c>generate --input</c> on the published file.
    /// </summary>
    public static Outcome<LoadedModel> Decode(RetainedInput original, ModelInput input, ModelReadLimits limits, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (input.Format == InputFormat.Auto && original is RetainedInput.File candidate && !IsBinary(candidate.Entry.Memory.Span) && IsYaml(candidate))
                return Failure("unsupported-format", "YAML model input is unsupported; use JSON or Ion IR.", original.SourcePath);
            var result = original switch
            {
                RetainedInput.Tree tree when input.Format != InputFormat.Json => DecodeIonTree(tree, limits),
                RetainedInput.Tree => Failure("unsupported-format", "JSON document trees are unsupported; use a JSON file or Ion tree.", original.SourcePath),
                RetainedInput.File file when input.Format == InputFormat.Ion || input.Format == InputFormat.Auto && IsIon(file) => DecodeIon(file, limits),
                RetainedInput.File file => DecodeJson(file, limits, ct),
                _ => Failure("unsupported-format", "Unsupported input capture.", original.SourcePath)
            };
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) { return Failure("cancelled", "Model loading was cancelled.", original.SourcePath, kind: GenerationFailureKind.Cancelled); }
        catch (InputLimitException) { return Failure("input-limit", "The model exceeds its depth or node budget.", original.SourcePath); }
        catch (JsonException error) { return Failure("malformed-json", "The input is not a complete JSON document.", original.SourcePath, error.Path ?? "$"); }
    }

    private static bool IsBinary(ReadOnlySpan<byte> bytes) => bytes.StartsWith(new byte[] { 0xE0, 0x01, 0x00, 0xEA });

    private static bool IsIon(RetainedInput.File file)
    {
        var bytes = file.Entry.Memory.Span;
        if (IsBinary(bytes)) return true;
        if (Path.GetExtension(file.SourcePath).ToLowerInvariant() is ".ion" or ".ionb") return true;
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
        // Content hints may follow arbitrarily long comments. Scan within the already bounded capture.
        while (!bytes.IsEmpty)
        {
            if (bytes[0] is 9 or 10 or 13 or 32) { bytes = bytes[1..]; continue; }
            if (bytes.StartsWith("//"u8))
            {
                var end = bytes.IndexOf((byte)'\n'); if (end < 0) return false;
                bytes = bytes[(end + 1)..]; continue;
            }
            if (bytes.StartsWith("/*"u8))
            {
                var end = bytes[2..].IndexOf("*/"u8); if (end < 0) return false;
                bytes = bytes[(end + 4)..]; continue;
            }
            return bytes.StartsWith("$ion_"u8) || bytes.StartsWith("morphir::"u8) || bytes.StartsWith("'morphir'::"u8) || bytes.StartsWith("morphir_model::"u8);
        }
        return false;
    }

    private static IonReadLimits IonLimits(ModelReadLimits limits) =>
        new((int)Math.Min(limits.MaxBytes, int.MaxValue), limits.MaxDepth, limits.MaxNodes);

    private static bool IsYaml(RetainedInput.File file)
    {
        if (Path.GetExtension(file.SourcePath).ToLowerInvariant() is ".yaml" or ".yml") return true;
        var prefix = System.Text.Encoding.UTF8.GetString(file.Entry.Memory.Span[..Math.Min(256, file.Entry.Memory.Length)]).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return prefix.StartsWith("---", StringComparison.Ordinal) || prefix.StartsWith("formatVersion:", StringComparison.Ordinal) || prefix.StartsWith("distribution:", StringComparison.Ordinal);
    }

    private static Outcome<LoadedModel> DecodeIon(RetainedInput.File file, ModelReadLimits limits)
    {
        var result = IonIr.read(IonLimits(limits), file.Entry.Memory);
        if (result.IsError)
        {
            if (result.ErrorValue.Code == "invalid_document")
            {
                var values = IonReader.read(IonLimits(limits), file.Entry.Memory);
                if (values.IsOk && !values.ResultValue.IsEmpty && values.ResultValue.Head.Annotations.Any(annotation => annotation is IonSymbol.Text text && text.Item == "morphir_model"))
                    return Failure("unsupported-profile", "MEP morphir_model admission is unavailable; supply bare IR.", file.SourcePath);
            }
            return IonFailure(result.ErrorValue, file.SourcePath);
        }
        return IonModel(result.ResultValue, file, IsBinary(file.Entry.Memory.Span) ? ModelRepresentation.IonBinary : ModelRepresentation.IonText);
    }

    private static Outcome<LoadedModel> DecodeIonTree(RetainedInput.Tree tree, ModelReadLimits limits)
    {
        if (!tree.Entries.Any(entry => entry.RelativePath == "manifest.ion") && tree.Entries.Any(entry => entry.RelativePath is "manifest.json" or "manifest.yaml" or "manifest.yml" or "morphir-ir.json" or "morphir-ir.yaml" or "morphir-ir.yml"))
            return Failure("unsupported-format", "JSON and YAML model trees are unsupported; use an Ion tree with manifest.ion.", tree.SourcePath);
        var files = tree.Entries.Select(entry => VFileModule.ofBytes(entry.RelativePath, entry.Memory));
        var result = IonIrTree.readDetailed(IonLimits(limits), VFileTreeModule.fromFiles(ListModule.OfSeq(files)));
        if (result.IsOk) return IonModel(result.ResultValue, tree, ModelRepresentation.IonTree);
        return result.ErrorValue switch
        {
            IonIrTreeError.Invalid invalid => Failure(invalid.Item.Code, invalid.Item.Message,
                invalid.Item.Path?.Value ?? tree.SourcePath, invalid.Item.Location?.Value ?? "$"),
            IonIrTreeError.SourceFailed source => Failure("input-read", "The captured tree entry could not be read.", source.path, kind: GenerationFailureKind.Operational),
            _ => Failure("ion-tree", "The Ion tree could not be decoded.", tree.SourcePath)
        };
    }

    private static Outcome<LoadedModel> IonModel(IrDocument document, RetainedInput original, ModelRepresentation representation)
    {
        var (version, distribution) = document switch
        {
            IrDocument.ClassicIr classic => (classic.formatVersion, DistributionModel.NewClassic(classic.Item2)),
            IrDocument.V4Ir v4 => (v4.formatVersion, DistributionModel.NewV4(v4.Item2)),
            _ => throw new InvalidOperationException("The Ion reader returned an unknown model family.")
        };
        var model = new VersionedModel(SourceFormatVersionModule.parse(version).ResultValue, distribution);
        return new Outcome<LoadedModel>.Success(new(model, ModelDescriptor.From(model), representation, original));
    }

    private static Outcome<LoadedModel> IonFailure(IonIrError error, string path) =>
        Failure(error.Code, error.Message, path, error.Location?.Value ?? error.Path?.Value ?? "$");

    private static Outcome<LoadedModel> DecodeJson(RetainedInput.File file, ModelReadLimits limits, CancellationToken ct)
    {
        var bytes = file.Entry.Memory;
        if (bytes.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
        CheckJsonBudget(bytes.Span, limits, ct);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaxDepth });
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && (root.TryGetProperty("envelopeVersion", out _) || root.TryGetProperty("morphir_model", out _) || root.TryGetProperty("jsonrpc", out _)))
            return Failure("unsupported-profile", "MEP model-envelope admission is unavailable; supply bare IR.", file.SourcePath);
        if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count(property => property.NameEquals("formatVersion")) > 1)
            return Failure("duplicate_format_version", "The model declares formatVersion more than once.", file.SourcePath, "$.formatVersion");
        var text = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("formatVersion", out var version)
            ? version.ValueKind == JsonValueKind.String ? version.GetString()! : version.GetRawText() : "";
        var parsedVersion = SourceFormatVersionModule.parse(text);
        if (parsedVersion.IsError)
            return Failure("unsupported-version", "The model requires an explicit supported formatVersion.", file.SourcePath, "$.formatVersion");
        DistributionModel distribution;
        if (text == "4" || text.StartsWith("4.", StringComparison.Ordinal))
        {
            var decoded = Morphir.Json.V4.V4Json.readDistribution(root);
            if (decoded.IsError) return Failure(decoded.ErrorValue.Rule, decoded.ErrorValue.Message, file.SourcePath, decoded.ErrorValue.Location);
            distribution = DistributionModel.NewV4(decoded.ResultValue.Distribution);
        }
        else
        {
            var decoded = Morphir.Json.Classic.ClassicJson.readDistribution(root);
            if (decoded.IsError) return Failure(decoded.ErrorValue.Rule, decoded.ErrorValue.Message, file.SourcePath, decoded.ErrorValue.Location);
            distribution = DistributionModel.NewClassic(decoded.ResultValue.Distribution);
        }
        var model = new VersionedModel(parsedVersion.ResultValue, distribution);
        return new Outcome<LoadedModel>.Success(new(model, ModelDescriptor.From(model), ModelRepresentation.Json, file));
    }

    private static void CheckJsonBudget(ReadOnlySpan<byte> bytes, ModelReadLimits limits, CancellationToken ct)
    {
        // Scan tokens before allocating a DOM or typed nodes. Property names and closing tokens are not nodes.
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = int.MaxValue });
        var nodes = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject or JsonTokenType.PropertyName) continue;
            if (++nodes > limits.MaxNodes || reader.CurrentDepth + 1 > limits.MaxDepth) throw new InputLimitException();
        }
    }

    private static Outcome<LoadedModel> Failure(string code, string message, string path, string location = "$", GenerationFailureKind kind = GenerationFailureKind.Semantic) =>
        new Outcome<LoadedModel>.Failure(GenerationFailure.One(kind, code, message, path, location));
}
