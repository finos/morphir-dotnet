// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using Morphir.Compilation;
using Severity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Compile;

/// <summary>A frontend chosen for a request, and the language it reads the documents as.</summary>
public sealed record ResolvedFrontend(IFrontend Frontend, LanguageId Language)
{
    /// <summary>The frontend label of reports, for example <c>morphir-dotnet.fsharp (in-process)</c>.</summary>
    public string Label => $"{Frontend.Identity.Value} ({Frontend.Transport.WireValue})";
}

/// <summary>Selects a frontend (FE-2).</summary>
public interface IFrontendResolver
{
    /// <summary>
    /// The file extensions that the registered frontends claim: for <paramref name="language"/>, or for every
    /// language when it is null. Source selection reads a directory with them.
    /// </summary>
    IReadOnlySet<string> ClaimedExtensions(LanguageId? language);

    /// <summary>
    /// Fails when no registered frontend reads <paramref name="language"/>. It needs no source, so the command
    /// fails before any project file is evaluated.
    /// </summary>
    Checked<LanguageId> CheckAvailable(LanguageId language);

    /// <summary>The one frontend for the documents, the language and the output target.</summary>
    Checked<ResolvedFrontend> Resolve(LanguageId? language, IReadOnlyList<string> documents, IrTarget target);

    /// <summary>
    /// A resolver over <paramref name="extension"/> only: the frontend of a configured extension. The same
    /// capability rules apply to it.
    /// </summary>
    IFrontendResolver Only(IFrontend extension);
}

/// <summary>
/// Selects among the registered frontends by what each one advertises, on every run: the language (or the
/// extensions of every document), each document's extension, the output target, and multi-document support.
/// With no extension configured, an in-process frontend wins over others. Every refusal names the frontend
/// and the capability that blocked the request.
/// </summary>
public sealed class FrontendResolver(IEnumerable<IFrontend> frontends, IEnumerable<FrontendHint> hints) : IFrontendResolver
{
    private readonly ImmutableArray<IFrontend> _frontends = [.. frontends];
    private readonly ImmutableArray<FrontendHint> _hints = [.. hints];

    /// <inheritdoc />
    public IFrontendResolver Only(IFrontend extension) => new FrontendResolver([extension], _hints);

    /// <inheritdoc />
    public IReadOnlySet<string> ClaimedExtensions(LanguageId? language) =>
        _frontends
            .SelectMany(frontend => frontend.Capability.Languages)
            .Where(capability => language is null || capability.Id.Equals(language))
            .SelectMany(capability => capability.FileExtensions)
            .ToHashSet(StringComparer.Ordinal);

    /// <inheritdoc />
    public Checked<LanguageId> CheckAvailable(LanguageId language) =>
        Readers(language).Length > 0 ? Checked<LanguageId>.Pass(language) : Fail<LanguageId>(CompileCodes.FrontendUnavailable, Unavailable(language));

    /// <inheritdoc />
    public Checked<ResolvedFrontend> Resolve(LanguageId? language, IReadOnlyList<string> documents, IrTarget target)
    {
        // 1. Language, or the extensions of every document.
        List<ResolvedFrontend> candidates;
        if (language is not null)
        {
            var readers = Readers(language);
            if (readers.Length == 0) return Fail<ResolvedFrontend>(CompileCodes.FrontendUnavailable, Unavailable(language));
            var refusals = new List<string>();
            candidates = [];
            foreach (var (frontend, capability) in readers)
            {
                var unclaimed = documents.FirstOrDefault(document => !Claims(capability, document));
                if (unclaimed is null) candidates.Add(new(frontend, language));
                else refusals.Add($"The frontend {frontend.Identity} does not claim the extension of '{unclaimed}'. Its extensions for {language}: {string.Join(", ", capability.FileExtensions)}.");
            }
            if (candidates.Count == 0) return Fail<ResolvedFrontend>(CompileCodes.FrontendUnavailable, string.Join(" ", refusals));
        }
        else
        {
            candidates = [.. _frontends.SelectMany(frontend => frontend.Capability.Languages
                .Where(capability => documents.All(document => Claims(capability, document)))
                .Select(capability => new ResolvedFrontend(frontend, capability.Id)))];
            if (candidates.Count == 0)
            {
                var extensions = string.Join(", ", documents.Select(Path.GetExtension).Distinct(StringComparer.Ordinal));
                return Fail<ResolvedFrontend>(CompileCodes.LanguageRequired,
                    $"No language is selected, and no registered frontend claims the extensions of every document ({extensions}). Use --language, or set frontend.language in morphir.toml.");
            }
            if (candidates.Select(candidate => candidate.Language).Distinct().Count() > 1)
                return Fail<ResolvedFrontend>(CompileCodes.LanguageRequired,
                    $"No language is selected, and the documents match more than one language: {string.Join(", ", candidates.Select(candidate => candidate.Language.Value).Distinct().Order(StringComparer.Ordinal))}. Use --language.");
        }

        // 2. Output target.
        var serving = candidates.Where(candidate => candidate.Frontend.Capability.Outputs.Contains(target)).ToList();
        if (serving.Count == 0)
            return Fail<ResolvedFrontend>(CompileCodes.IrVersionNotServed, string.Join(" ", candidates.Select(candidate =>
                $"The frontend {candidate.Frontend.Identity} does not produce {target}. It produces: {string.Join(", ", candidate.Frontend.Capability.Outputs)}.")));

        // 3. Multi-document support.
        var able = documents.Count <= 1 ? serving : serving.Where(candidate => candidate.Frontend.Capability.MultiDocument).ToList();
        if (able.Count == 0)
            return Fail<ResolvedFrontend>(CompileCodes.FrontendUnavailable, string.Join(" ", serving.Select(candidate =>
                $"The frontend {candidate.Frontend.Identity} compiles one document per request, and the source set has {documents.Count}.")));

        // 4. With no extension configured, the in-process registration wins.
        var inProcess = able.Where(candidate => candidate.Frontend.Transport.Equals(FrontendTransport.InProcess)).ToList();
        var chosen = inProcess.Count > 0 ? inProcess : able;
        if (chosen.Count > 1)
            return Fail<ResolvedFrontend>(CompileCodes.AmbiguousFrontend,
                $"More than one registered frontend serves the request: {string.Join(", ", chosen.Select(candidate => candidate.Frontend.Identity.Value))}.");
        return Checked<ResolvedFrontend>.Pass(chosen[0]);
    }

    private (IFrontend Frontend, LanguageCapability Capability)[] Readers(LanguageId language) =>
        [.. _frontends.SelectMany(frontend => frontend.Capability.Languages
            .Where(capability => capability.Id.Equals(language))
            .Select(capability => (frontend, capability)))];

    private static bool Claims(LanguageCapability capability, string document) =>
        capability.FileExtensions.Contains(Path.GetExtension(document), StringComparer.Ordinal);

    private string Unavailable(LanguageId language)
    {
        var hint = string.Concat(_hints.Where(hint => hint.Language.Equals(language)).Select(hint => " " + hint.Message));
        var registered = _frontends.IsEmpty
            ? "This build registers no frontend."
            : $"Registered frontends: {string.Join(", ", _frontends.Select(frontend => frontend.Identity.Value))}.";
        return $"No registered frontend reads the language '{language}'. {registered}{hint} Set [frontend.{language}].extension in morphir.toml, or use --extension, to configure a frontend extension.";
    }

    private static Checked<T> Fail<T>(DiagnosticCode code, string message) =>
        Checked<T>.Fail([Diagnostic.Create(Severity.Error, code, message, null)]);
}
