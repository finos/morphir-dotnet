using System.Text.Json;
using Morphir.IR.Model;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Decodes only an owned capture, retaining its exact bytes beside the semantic view.</summary>
public sealed class GenerationModelLoader(BoundedModelCapture capture) : IModelLoader
{
    private sealed class InputLimitException : Exception;

    public async ValueTask<Outcome<LoadedModel>> LoadAsync(ModelInput input, ModelReadLimits limits, CancellationToken ct)
    {
        var captured = await capture.CaptureAsync(input, limits, ct);
        if (captured is Outcome<RetainedInput>.Failure failed) return new Outcome<LoadedModel>.Failure(failed.Error);
        var original = ((Outcome<RetainedInput>.Success)captured).Value;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (original is not RetainedInput.File file)
                return Failure("unsupported-format", "Tree decoding is not available for this input format.", original.SourcePath);
            if (input.Format == InputFormat.Ion || input.Format == InputFormat.Auto && IsIon(file))
                return Failure("unsupported-format", "Ion decoding is not available for this input format.", original.SourcePath);
            var result = DecodeJson(file, limits, ct);
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) { return Failure("cancelled", "Model loading was cancelled.", original.SourcePath, kind: GenerationFailureKind.Cancelled); }
        catch (InputLimitException) { return Failure("input-limit", "The model exceeds its depth or node budget.", original.SourcePath); }
        catch (JsonException error) { return Failure("malformed-json", "The input is not a complete JSON document.", original.SourcePath, error.Path ?? "$"); }
    }

    private static bool IsIon(RetainedInput.File file) =>
        file.Entry.Memory.Span.StartsWith(new byte[] { 0xE0, 0x01, 0x00, 0xEA }) ||
        Path.GetExtension(file.SourcePath).Equals(".ion", StringComparison.OrdinalIgnoreCase) ||
        System.Text.Encoding.UTF8.GetString(file.Entry.Memory.Span[..Math.Min(file.Entry.Memory.Length, 128)]).TrimStart().StartsWith("$ion_", StringComparison.Ordinal);

    private static Outcome<LoadedModel> DecodeJson(RetainedInput.File file, ModelReadLimits limits, CancellationToken ct)
    {
        var bytes = file.Entry.Memory;
        if (bytes.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
        CheckJsonBudget(bytes.Span, limits, ct);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaxDepth });
        var root = document.RootElement;
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
