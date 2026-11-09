using Morphir.Extensions;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Features.Validate;

/// <summary>
/// One <c>morphir validate</c> run: parse the invocation, read the model as <c>morphir generate</c> does, read which
/// analyzers the configuration turns off, run the analyzers, and make the report. An input that cannot be read runs no
/// analyzer. The model and the file system do not change.
/// </summary>
public sealed class ModelValidation(IModelLoader loader, AnalyzerRegistry analyzers, IAnalyzerConfiguration configuration, HostIdentity host)
{
    public async Task<ValidationReport> RunAsync(ValidateOverrides overrides, InvocationContext context, CancellationToken ct)
    {
        var input = overrides.Input;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(input)) return ValidationReport.Failed(host, 2, null, "missing-input", "Specify --input.");
            InputFormat? format = overrides.InputFormat switch
            {
                null or "auto" => InputFormat.Auto,
                "json" => InputFormat.Json,
                "ion" => InputFormat.Ion,
                _ => null
            };
            if (format is null) return ValidationReport.Failed(host, 2, input, "invalid-input-format", "Input format must be auto, json or ion.");
            string path;
            try { path = Path.GetFullPath(input, context.WorkingDirectory); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return ValidationReport.Failed(host, 2, input, "invalid-path", "Input and invocation directory must be valid filesystem paths.");
            }

            var loaded = await loader.LoadAsync(new ModelInput(path, format.Value), ModelReadLimits.Default, ct);
            if (loaded is Outcome<LoadedModel>.Failure loadFailure) return ValidationReport.Failed(host, path, loadFailure.Error);
            var model = ((Outcome<LoadedModel>.Success)loaded).Value;

            var ids = analyzers.Analyzers.Select(analyzer => analyzer.Entry.Identity.Id).ToArray();
            var settings = configuration.TurnedOff(context.WorkingDirectory, ids, ct);
            if (settings is Outcome<IReadOnlyList<ExtensionId>>.Failure settingsFailure) return ValidationReport.Failed(host, path, settingsFailure.Error);
            var turnedOff = ((Outcome<IReadOnlyList<ExtensionId>>.Success)settings).Value;

            var result = await AnalyzerRunner.RunAsync(analyzers.Analyzers, model.Model, turnedOff, ct);
            return ValidationReport.From(host, model.Original.SourcePath, model.Model, result);
        }
        catch (OperationCanceledException)
        {
            return ValidationReport.Failed(host, 130, input, "cancelled", "Validation was cancelled.");
        }
    }
}
