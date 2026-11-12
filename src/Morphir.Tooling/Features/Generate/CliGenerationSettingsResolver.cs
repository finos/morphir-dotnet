namespace Morphir.Tooling.Features.Generate;

/// <summary>Resolves explicit invocation values, including <c>--extension</c>. No workspace discovery or configuration I/O.</summary>
public sealed class CliGenerationSettingsResolver : IGenerationSettingsResolver
{
    public ValueTask<Outcome<GenerationSettings>> ResolveAsync(GenerateOverrides overrides, InvocationContext context, CancellationToken ct)
    {
        Outcome<GenerationSettings> Fail(string code, string message) =>
            new Outcome<GenerationSettings>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation, code, message));

        if (ct.IsCancellationRequested)
            return ValueTask.FromResult<Outcome<GenerationSettings>>(new Outcome<GenerationSettings>.Failure(
                GenerationFailure.One(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled.")));

        foreach (var (name, value) in new[] { ("input", overrides.Input), ("target", overrides.Target), ("output", overrides.Output) })
            if (string.IsNullOrWhiteSpace(value))
                return ValueTask.FromResult(Fail("missing-" + name, $"Specify --{name}."));

        var format = overrides.InputFormat switch
        {
            null or "auto" => InputFormat.Auto,
            "json" => InputFormat.Json,
            "ion" => InputFormat.Ion,
            _ => (InputFormat?)null
        };
        if (format is null)
            return ValueTask.FromResult(Fail("invalid-input-format", "Input format must be auto, json or ion."));

        if (overrides.Extension is { } extension && string.IsNullOrWhiteSpace(extension))
            return ValueTask.FromResult(Fail("invalid-extension", "The backend extension from --extension is empty."));

        var options = BackendOptionText.Parse(overrides.Options);
        if (overrides.Extension is not null && overrides.ExtensionId is not null)
            return ValueTask.FromResult(Fail("invalid-extension", "Choose --extension or --extension-id, not both."));
        Morphir.Extensions.ExtensionId? installedId = null;
        if (overrides.ExtensionId is { } rawId)
        {
            if (!Morphir.Extensions.ExtensionId.TryParse(rawId, out installedId, out _))
                return ValueTask.FromResult(Fail("invalid-extension", "--extension-id must be a valid full extension identity."));
        }
        if (options is Outcome<BackendOptions>.Failure optionFailure)
            return ValueTask.FromResult<Outcome<GenerationSettings>>(new Outcome<GenerationSettings>.Failure(optionFailure.Error));

        try
        {
            var input = Path.GetFullPath(overrides.Input!, context.WorkingDirectory);
            var output = Path.GetFullPath(overrides.Output!, context.WorkingDirectory);
            return ValueTask.FromResult<Outcome<GenerationSettings>>(new Outcome<GenerationSettings>.Success(
                new(new(input, format.Value), RequestedTarget.Parse(overrides.Target!), ((Outcome<BackendOptions>.Success)options).Value, new(output), ModelReadLimits.Default)
                {
                    Extension = overrides.Extension is { } selected
                        ? new Morphir.Tooling.Features.Compile.Setting<string>(selected, new Morphir.Tooling.Features.Compile.SettingOrigin.CommandLine("--extension"))
                        : null,
                    InstalledExtensionId = installedId,
                }));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ValueTask.FromResult(Fail("invalid-path", "Input, output and invocation directory must be valid filesystem paths."));
        }
    }
}
