using System.Collections.Immutable;
using System.Text.Json;
using Morphir.Files;
using Morphir.IR.Model;
using Morphir.IR.Trees;
using Morphir.Ion;
using Morphir.Tooling.Infrastructure;

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
    /// Loads a model under the released JSON/Ion-only policy used by <c>morphir validate</c>: refuses YAML files
    /// and JSON/YAML document trees even in <see cref="InputFormat.Auto"/> mode.
    /// </summary>
    internal async ValueTask<Outcome<LoadedModel>> LoadWithoutYamlOrDocumentTreesAsync(ModelInput input, ModelReadLimits limits, CancellationToken ct)
    {
        var captured = await capture.CaptureAsync(input, limits, ct);
        if (captured is Outcome<RetainedInput>.Failure failed) return new Outcome<LoadedModel>.Failure(failed.Error);
        var original = ((Outcome<RetainedInput>.Success)captured).Value;
        return DecodeLegacy(original, input, limits, ct);
    }

    internal static Outcome<LoadedModel> DecodeLegacy(
        RetainedInput original,
        ModelInput input,
        ModelReadLimits limits,
        CancellationToken ct,
        Func<RetainedInput.Tree, ModelReadLimits, Outcome<LoadedModel>>? ionTreeDecoder = null)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (CheckLimits(limits, original.SourcePath) is { } invalidLimits)
                return invalidLimits;
            if (input.Format is not (InputFormat.Auto or InputFormat.Json or InputFormat.Ion))
                return Failure("invalid-input-format", $"Unsupported input format '{input.Format}'.", original.SourcePath, kind: GenerationFailureKind.Invocation);

            if (original is RetainedInput.File candidate && input.Format == InputFormat.Auto && !IsIon(candidate)
                && (HasYamlExtension(candidate) || (!HasJsonExtension(candidate) && IsExtensionlessMorphirYaml(candidate, limits, ct))))
                return Failure("unsupported-format", "YAML model input is unsupported; use JSON or Ion IR.", original.SourcePath);

            if (original is RetainedInput.Tree tree)
            {
                if (input.Format == InputFormat.Json)
                    return Failure("unsupported-format", "JSON document trees are unsupported; use a JSON file or Ion tree.", original.SourcePath);
                if (!tree.Entries.Any(entry => entry.RelativePath == "manifest.ion")
                    && tree.Entries.Any(entry => entry.RelativePath is "manifest.json" or "manifest.yaml" or "manifest.yml" or "morphir-ir.json" or "morphir-ir.yaml" or "morphir-ir.yml"))
                    return Failure("unsupported-format", "JSON and YAML model trees are unsupported; use an Ion tree with manifest.ion.", tree.SourcePath);
                var decoded = (ionTreeDecoder ?? DecodeIonTree)(tree, limits);
                ct.ThrowIfCancellationRequested();
                return decoded;
            }

            return Decode(original, input, limits, ct);
        }
        catch (OperationCanceledException) { return Failure("cancelled", "Model loading was cancelled.", original.SourcePath, kind: GenerationFailureKind.Cancelled); }
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
            if (CheckLimits(limits, original.SourcePath) is { } invalidLimits)
                return invalidLimits;
            if (input.Format is not (InputFormat.Auto or InputFormat.Json or InputFormat.Yaml or InputFormat.Ion))
                return Failure("invalid-input-format", $"Unsupported input format '{input.Format}'.", original.SourcePath, kind: GenerationFailureKind.Invocation);

            var result = original switch
            {
                RetainedInput.Tree tree => DecodeTree(tree, input.Format, limits, ct),
                RetainedInput.File file => DecodeFile(file, input.Format, limits, ct),
                _ => Failure("unsupported-format", "Unsupported input capture.", original.SourcePath)
            };
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) { return Failure("cancelled", "Model loading was cancelled.", original.SourcePath, kind: GenerationFailureKind.Cancelled); }
        catch (InputLimitException) { return Failure("input-limit", "The model exceeds its depth or node budget.", original.SourcePath); }
        catch (JsonException error) { return Failure("malformed-json", "The input is not a complete JSON document.", original.SourcePath, error.Path ?? "$"); }
    }

    private static Outcome<LoadedModel> DecodeFile(RetainedInput.File file, InputFormat format, ModelReadLimits limits, CancellationToken ct) =>
        format switch
        {
            InputFormat.Json => DecodeJson(file, limits, ct),
            InputFormat.Yaml => DecodeYaml(file, limits, ct),
            InputFormat.Ion => DecodeIon(file, limits),
            InputFormat.Auto => DecodeAutoFile(file, limits, ct),
            _ => Failure("invalid-input-format", $"Unsupported input format '{format}'.", file.SourcePath, kind: GenerationFailureKind.Invocation)
        };

    private static Outcome<LoadedModel> DecodeAutoFile(RetainedInput.File file, ModelReadLimits limits, CancellationToken ct)
    {
        if (IsIon(file)) return DecodeIon(file, limits);
        if (HasYamlExtension(file)) return DecodeYaml(file, limits, ct);
        if (HasJsonExtension(file)) return DecodeJson(file, limits, ct);

        try
        {
            return DecodeJson(file, limits, ct);
        }
        catch (JsonException)
        {
            if (TryReadBoundedMorphirYaml(file, limits, ct, out var root))
                return DecodeJsonElement(root, file, ModelRepresentation.Yaml);
            throw;
        }
    }

    private static Outcome<LoadedModel> DecodeTree(RetainedInput.Tree tree, InputFormat format, ModelReadLimits limits, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (format is not (InputFormat.Auto or InputFormat.Json or InputFormat.Yaml or InputFormat.Ion))
            return Failure("invalid-input-format", $"Unsupported input format '{format}'.", tree.SourcePath, kind: GenerationFailureKind.Invocation);

        var manifestFiles = tree.Entries
            .Where(entry => entry.RelativePath is "manifest.json" or "manifest.yaml" or "manifest.yml" or "manifest.ion")
            .Select(entry => entry.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (manifestFiles.Length > 1)
        {
            return Failure(
                "tree.ambiguous_profile",
                $"The tree has more than one manifest, {string.Join(" and ", manifestFiles)}; a reader does not choose one.",
                tree.SourcePath);
        }

        var hasJson = manifestFiles.Length == 1 && manifestFiles[0] == "manifest.json";
        var hasYaml = manifestFiles.Length == 1 && manifestFiles[0] is "manifest.yaml" or "manifest.yml";
        var hasIon = manifestFiles.Length == 1 && manifestFiles[0] == "manifest.ion";

        return format switch
        {
            InputFormat.Json when hasYaml || hasIon =>
                Failure("unsupported-format", "Expected a JSON document tree with manifest.json.", tree.SourcePath),
            InputFormat.Json =>
                DecodeDocumentTree(tree, TreeProfileSelection.JsonOnly, limits, ct),
            InputFormat.Yaml when hasJson || hasIon =>
                Failure("unsupported-format", "Expected a YAML document tree with manifest.yaml.", tree.SourcePath),
            InputFormat.Yaml =>
                DecodeDocumentTree(tree, TreeProfileSelection.YamlOnly, limits, ct),
            InputFormat.Ion when hasJson || hasYaml =>
                Failure("unsupported-format", "Expected an Ion document tree with manifest.ion.", tree.SourcePath),
            InputFormat.Ion =>
                DecodeIonTree(tree, limits),
            InputFormat.Auto when hasJson =>
                DecodeDocumentTree(tree, TreeProfileSelection.JsonOnly, limits, ct),
            InputFormat.Auto when hasYaml =>
                DecodeDocumentTree(tree, TreeProfileSelection.YamlOnly, limits, ct),
            InputFormat.Auto when hasIon =>
                DecodeIonTree(tree, limits),
            InputFormat.Auto =>
                DecodeDocumentTree(tree, TreeProfileSelection.Any, limits, ct),
            _ =>
                Failure("invalid-input-format", $"Unsupported input format '{format}'.", tree.SourcePath, kind: GenerationFailureKind.Invocation)
        };
    }

    private static Outcome<LoadedModel> DecodeDocumentTree(
        RetainedInput.Tree tree,
        TreeProfileSelection profiles,
        ModelReadLimits limits,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var files = tree.Entries.Select(entry => VFile.OfBytes(entry.RelativePath, entry.Memory));
        var vfileTree = VFileTree.FromFiles(files);
        var options = TreeReadOptions.Create(
                limits.MaxEntries,
                limits.MaxBytes,
                limits.MaxDepth,
                limits.MaxNodes,
                profiles)
            .WithMaxTotalBytes(limits.MaxBytes);

        if (!DocumentTreeStore.TryReadModel(options, vfileTree, ct, out var loaded, out var errors))
            return StoreFailure(errors, tree.SourcePath);

        var representation = loaded.Profile.Match(
            json: () => ModelRepresentation.JsonTree,
            yaml: () => ModelRepresentation.YamlTree);
        return new Outcome<LoadedModel>.Success(new(loaded.Model, ModelDescriptor.From(loaded.Model), representation, tree));
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

    private static Outcome<LoadedModel>? CheckLimits(ModelReadLimits limits, string path)
    {
        if (limits.MaxBytes <= 0 || limits.MaxEntries <= 0 || limits.MaxDepth <= 0 || limits.MaxNodes <= 0 || limits.MaxPathLength <= 0)
            return Failure("invalid-input-limits", BoundedCapture.InvalidLimitsMessage, path, kind: GenerationFailureKind.Invocation);
        return null;
    }

    private static bool HasYamlExtension(RetainedInput.File file) =>
        Path.GetExtension(file.SourcePath).ToLowerInvariant() is ".yaml" or ".yml";

    private static bool HasJsonExtension(RetainedInput.File file) =>
        Path.GetExtension(file.SourcePath).Equals(".json", StringComparison.OrdinalIgnoreCase);

    private static bool HasMorphirOrMepRootKey(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && (root.TryGetProperty("formatVersion", out _)
            || root.TryGetProperty("distribution", out _)
            || root.TryGetProperty("$schema", out _)
            || root.TryGetProperty("envelopeVersion", out _)
            || root.TryGetProperty("morphir_model", out _)
            || root.TryGetProperty("jsonrpc", out _));

    private static bool TryReadBoundedMorphirYaml(RetainedInput.File file, ModelReadLimits limits, CancellationToken ct, out JsonElement root)
    {
        ct.ThrowIfCancellationRequested();
        if (IsBinary(file.Entry.Memory.Span))
        {
            root = default;
            return false;
        }
        if (DocumentTreeStore.TryReadYamlDocument(file.SourcePath, file.Entry.Memory, limits.MaxBytes, limits.MaxDepth, limits.MaxNodes, ct, out root, out _)
            && HasMorphirOrMepRootKey(root))
            return true;

        root = default;
        return false;
    }

    private static bool IsExtensionlessMorphirYaml(RetainedInput.File file, ModelReadLimits limits, CancellationToken ct)
    {
        var bytes = file.Entry.Memory;
        if (bytes.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
        try
        {
            CheckJsonBudget(bytes.Span, limits, ct);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaxDepth });
            return false;
        }
        catch (InputLimitException)
        {
            return false;
        }
        catch (JsonException)
        {
            return TryReadBoundedMorphirYaml(file, limits, ct, out _);
        }
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

    internal static Outcome<LoadedModel> DecodeIonTree(RetainedInput.Tree tree, ModelReadLimits limits)
    {
        var files = tree.Entries.Select(entry => VFile.OfBytes(entry.RelativePath, entry.Memory));
        var result = IonIrTree.readDetailed(IonLimits(limits), VFileTree.FromFiles(files));
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

    private static Outcome<LoadedModel> DecodeYaml(RetainedInput.File file, ModelReadLimits limits, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!DocumentTreeStore.TryReadYamlDocument(file.SourcePath, file.Entry.Memory, limits.MaxBytes, limits.MaxDepth, limits.MaxNodes, ct, out var root, out var errors))
            return StoreFailure(errors, file.SourcePath);
        ct.ThrowIfCancellationRequested();
        return DecodeJsonElement(root, file, ModelRepresentation.Yaml);
    }

    private static Outcome<LoadedModel> DecodeJson(RetainedInput.File file, ModelReadLimits limits, CancellationToken ct)
    {
        var bytes = file.Entry.Memory;
        if (bytes.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
        CheckJsonBudget(bytes.Span, limits, ct);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaxDepth });
        return DecodeJsonElement(document.RootElement, file, ModelRepresentation.Json);
    }

    private static Outcome<LoadedModel> DecodeJsonElement(JsonElement root, RetainedInput.File file, ModelRepresentation representation)
    {
        if (root.ValueKind == JsonValueKind.Object && (root.TryGetProperty("envelopeVersion", out _) || root.TryGetProperty("morphir_model", out _) || root.TryGetProperty("jsonrpc", out _)))
            return Failure("unsupported-profile", "MEP model-envelope admission is unavailable; supply bare IR.", file.SourcePath);
        if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count(property => property.NameEquals("formatVersion")) > 1)
            return Failure("duplicate_format_version", "The model declares formatVersion more than once.", file.SourcePath, "$.formatVersion");
        var text = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("formatVersion", out var version)
            ? version.ValueKind == JsonValueKind.String ? version.GetString()! : version.GetRawText() : "";
        if (!SourceFormatVersion.TryParse(text, out var parsedVersion, out _))
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
        var model = new VersionedModel(parsedVersion, distribution);
        return new Outcome<LoadedModel>.Success(new(model, ModelDescriptor.From(model), representation, file));
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

    private static Outcome<LoadedModel> StoreFailure(IReadOnlyList<TreeDiagnostic> errors, string defaultPath)
    {
        var kind = errors.Any(error => error.Code == "tree.unreadable_file")
            ? GenerationFailureKind.Operational
            : GenerationFailureKind.Semantic;
        var diagnostics = errors
            .Select(error => new GenerationDiagnostic(
                error.Code,
                error.Message,
                error.FileOrNull ?? defaultPath,
                error.LocationOrNull ?? "$"))
            .ToImmutableArray();
        return new Outcome<LoadedModel>.Failure(new GenerationFailure(kind, diagnostics));
    }

    private static Outcome<LoadedModel> Failure(string code, string message, string path, string location = "$", GenerationFailureKind kind = GenerationFailureKind.Semantic) =>
        new Outcome<LoadedModel>.Failure(GenerationFailure.One(kind, code, message, path, location));
}
