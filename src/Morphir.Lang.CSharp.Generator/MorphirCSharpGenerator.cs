// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Morphir.Lang.CSharp.Generator;

/// <summary>
/// The source generator of Morphir.Lang.CSharp (design.html, BLD-1): a thin adapter over the pipeline function. Each
/// <c>*.morphir.json</c> additional file goes through <see cref="CSharpPipeline.Generate"/>, and the files go to the
/// compilation. A failure is a diagnostic; the generator does not throw.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MorphirCSharpGenerator : IIncrementalGenerator
{
    /// <summary>The suffix of a model file.</summary>
    public const string ModelSuffix = ".morphir.json";

    /// <summary>
    /// The MSBuild property that gives the generation to the MSBuild target of the package. When it is true, the target
    /// writes the files, and the generator does nothing.
    /// </summary>
    public const string FilesProperty = "build_property.MorphirGenerateCSharpFiles";

    /// <summary>The MSBuild property with the namespace of all generated files. Empty: the namespace of the package of the model.</summary>
    public const string NamespaceProperty = "build_property.MorphirCSharpNamespace";

    // UTF-8 with no byte order mark: the bytes of the CLI and of the MSBuild target, also in EmitCompilerGeneratedFiles.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private const string HelpLink = "https://morphir.finos.org/rules/csharp/generation_failed";

    /// <summary>The generation of a model failed. The message has the model file, the code and the reason.</summary>
    public static readonly DiagnosticDescriptor GenerationFailed = new(
        "MORPHIR2001", "Morphir C# generation failed", "{0}: {1}", "Morphir.Generation", DiagnosticSeverity.Error,
        isEnabledByDefault: true, helpLinkUri: HelpLink);

    /// <summary>A warning of the pipeline. The message has the model file, the code and the reason.</summary>
    public static readonly DiagnosticDescriptor GenerationWarning = new(
        "MORPHIR2002", "Morphir C# generation warning", "{0}: {1}", "Morphir.Generation", DiagnosticSeverity.Warning,
        isEnabledByDefault: true, helpLinkUri: HelpLink);

    /// <summary>Two models write a file with the same path and different text.</summary>
    public static readonly DiagnosticDescriptor DuplicateFile = new(
        "MORPHIR2003", "Two Morphir models write one file", "The models '{0}' and '{1}' both write '{2}' with different text", "Morphir.Generation",
        DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: HelpLink);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var settings = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => Settings.Of(provider.GlobalOptions));

        var results = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(ModelSuffix, StringComparison.OrdinalIgnoreCase))
            .Combine(settings)
            .Where(static pair => pair.Right.Enabled)
            .Select(static (pair, ct) => Run(pair.Left, pair.Right, ct))
            .Collect();

        context.RegisterSourceOutput(results, static (output, models) => Emit(output, models));
    }

    private static ModelResult Run(AdditionalText file, Settings settings, CancellationToken ct)
    {
        try
        {
            var text = file.GetText(ct);
            if (text is null)
                return ModelResult.Failed(file.Path, "unreadable-model: The compiler cannot read the model file.");

            var request = CSharpPipelineRequest.Create(Encoding.UTF8.GetBytes(text.ToString()))
                .ForTargetFramework(settings.FrameworkIdentifier, settings.FrameworkVersion);
            if (settings.Namespace.Length > 0)
            {
                var parsed = Morphir.Lang.CSharp.Backend.NamespaceOverride.TryCreate(settings.Namespace);
                if (parsed.IsError)
                    return ModelResult.Failed(file.Path, $"{parsed.ErrorValue.Rule}: MorphirCSharpNamespace: {parsed.ErrorValue.Message}");
                request = request.WithNamespace(parsed.ResultValue);
            }

            return new ModelResult(file.Path, CSharpPipeline.Generate(request, ct), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            return ModelResult.Failed(file.Path, $"generation-failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private static void Emit(SourceProductionContext output, ImmutableArray<ModelResult> models)
    {
        // A file that two models write with one text (the polyfill file) is added one time.
        var added = new Dictionary<string, (string Model, string Text)>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            var location = Location.Create(model.Path, default, default);
            if (model.Failure is { } failure)
            {
                output.ReportDiagnostic(Diagnostic.Create(GenerationFailed, location, model.Path, failure));
                continue;
            }

            var result = model.Result!;
            foreach (var diagnostic in result.Diagnostics)
            {
                if (diagnostic.IsError)
                    output.ReportDiagnostic(Diagnostic.Create(GenerationFailed, location, model.Path, diagnostic.ToString()));
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                    output.ReportDiagnostic(Diagnostic.Create(GenerationWarning, location, model.Path, diagnostic.ToString()));
            }

            if (!result.Succeeded) continue;
            foreach (var file in result.Files)
            {
                if (added.TryGetValue(file.RelativePath, out var earlier))
                {
                    if (!string.Equals(earlier.Text, file.Text, StringComparison.Ordinal))
                        output.ReportDiagnostic(Diagnostic.Create(DuplicateFile, location, earlier.Model, model.Path, file.RelativePath));
                    continue;
                }

                added.Add(file.RelativePath, (model.Path, file.Text));
                output.AddSource(file.RelativePath, SourceText.From(file.Text, Utf8));
            }
        }
    }

    /// <summary>The MSBuild properties that the generator reads. netstandard2.0 has no init accessors, so it is a class.</summary>
    private sealed class Settings : IEquatable<Settings>
    {
        private Settings(bool enabled, string ns, string frameworkIdentifier, string frameworkVersion)
        {
            Enabled = enabled;
            Namespace = ns;
            FrameworkIdentifier = frameworkIdentifier;
            FrameworkVersion = frameworkVersion;
        }

        public bool Enabled { get; }
        public string Namespace { get; }
        public string FrameworkIdentifier { get; }
        public string FrameworkVersion { get; }

        public static Settings Of(AnalyzerConfigOptions options)
        {
            string Read(string name) => options.TryGetValue(name, out var value) ? value.Trim() : "";
            return new(
                !string.Equals(Read(FilesProperty), "true", StringComparison.OrdinalIgnoreCase),
                Read(NamespaceProperty),
                Read("build_property.TargetFrameworkIdentifier"),
                Read("build_property.TargetFrameworkVersion"));
        }

        // Equal settings keep the cached results of the incremental pipeline.
        public bool Equals(Settings? other) =>
            other is not null && Enabled == other.Enabled && Namespace == other.Namespace
            && FrameworkIdentifier == other.FrameworkIdentifier && FrameworkVersion == other.FrameworkVersion;

        public override bool Equals(object? obj) => Equals(obj as Settings);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Namespace + "|" + FrameworkIdentifier + "|" + FrameworkVersion) ^ (Enabled ? 1 : 0);
    }

    private sealed class ModelResult
    {
        public ModelResult(string path, CSharpPipelineResult? result, string? failure)
        {
            Path = path;
            Result = result;
            Failure = failure;
        }

        public string Path { get; }
        public CSharpPipelineResult? Result { get; }
        public string? Failure { get; }

        public static ModelResult Failed(string path, string failure) => new(path, null, failure);
    }
}
