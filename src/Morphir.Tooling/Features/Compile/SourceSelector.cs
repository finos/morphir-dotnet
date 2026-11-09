// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using Morphir.Compilation;
using Severity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Compile;

/// <summary>Selects the ordered source documents of one compile (SRC-1).</summary>
public interface ISourceSelector
{
    /// <summary>
    /// Selects the documents that <paramref name="settings"/> names. A directory gives only files whose
    /// extension is in <paramref name="claimedExtensions"/>: the extensions that the registered frontends
    /// advertise for the selected language, or for every language when none is selected.
    /// </summary>
    Task<Checked<SelectedSources>> SelectAsync(CompileSettings settings, IReadOnlySet<string> claimedExtensions, CancellationToken ct);
}

/// <summary>
/// Language-neutral source selection. A project file goes to the one provider whose pattern matches it. Input
/// files keep the given order. A directory gives its files in lexical order of their '/'-separated relative
/// paths. No rule here belongs to one language, and no project file is evaluated unless it is named.
/// </summary>
public sealed class SourceSelector(ProjectFileProviderRegistry providers) : ISourceSelector
{
    /// <inheritdoc />
    public async Task<Checked<SelectedSources>> SelectAsync(CompileSettings settings, IReadOnlySet<string> claimedExtensions, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return settings.Sources switch
        {
            SourceRequest.ProjectFile file => await ExpandAsync(file.Path, ct),
            SourceRequest.Inputs inputs => SelectInputs(settings, inputs.Paths, claimedExtensions),
            SourceRequest.SourceDirectory directory => SelectDirectory(settings, directory.Path, claimedExtensions),
            _ => throw new System.Diagnostics.UnreachableException()
        };
    }

    private async Task<Checked<SelectedSources>> ExpandAsync(string projectFile, CancellationToken ct)
    {
        if (!File.Exists(projectFile))
            return Fail(CompileCodes.MissingSource, $"The project file '{projectFile}' does not exist.");
        var resolved = providers.Resolve(projectFile);
        if (!resolved.TryGetValue(out var provider)) return Checked<SelectedSources>.Fail(resolved.Diagnostics);
        var expanded = await provider.ExpandAsync(projectFile, ct);
        var directory = Path.GetDirectoryName(projectFile)!;
        return expanded.Match(
            (selection, diagnostics) => Checked<SelectedSources>.Of(
                new SelectedSources(selection, Path.GetFullPath(selection.Root, directory)), diagnostics),
            diagnostics => Checked<SelectedSources>.Fail(diagnostics));
    }

    private Checked<SelectedSources> SelectInputs(CompileSettings settings, ImmutableArray<string> paths, IReadOnlySet<string> claimed)
    {
        var directories = paths.Where(Directory.Exists).ToArray();
        if (directories.Length > 0)
        {
            if (paths.Length > 1)
                return Fail(CompileCodes.InvalidSetting,
                    "The option --input takes source files, or exactly one directory. Give the directory alone.");
            return SelectDirectory(settings, directories[0], claimed);
        }

        foreach (var path in paths)
        {
            if (!File.Exists(path)) return Fail(CompileCodes.MissingSource, $"The input '{path}' does not exist.");
            if (IsProjectFile(path))
                return Fail(CompileCodes.ProjectFileAsInput,
                    $"The input '{path}' is a project file. Use --project-file to compile the sources that it names.");
        }

        var root = CommonDirectory(paths);
        return Selection(root, paths.Select(path => Relative(root, path)), null);
    }

    private static Checked<SelectedSources> SelectDirectory(CompileSettings settings, string directory, IReadOnlySet<string> claimed)
    {
        var documents = Directory.Exists(directory)
            ? Files(directory)
                .Where(path => claimed.Contains(Path.GetExtension(path)))
                .Select(path => Relative(directory, path))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
        if (documents.Length > 0) return Selection(directory, documents, null);
        if (settings.Language is null)
        {
            var unread = settings.UnreadProjectFileKeys.IsEmpty
                ? ""
                : $" The configured {string.Join(", ", settings.UnreadProjectFileKeys)} is read only for the selected language.";
            return Fail(CompileCodes.LanguageRequired,
                $"No language is selected, and the directory '{directory}' has no source document that a registered frontend claims. Use --language, or set frontend.language in morphir.toml.{unread}");
        }
        return Fail(CompileCodes.NoDocuments,
            $"The directory '{directory}' has no source document for the language '{settings.Language.Value}'. Extensions: {string.Join(", ", claimed.Order(StringComparer.Ordinal))}.");
    }

    private bool IsProjectFile(string path)
    {
        var resolved = providers.Resolve(path);
        return resolved.IsPassed || resolved.Diagnostics.Any(diagnostic => Equals(diagnostic.CodeOrNull, RegistryCodes.AmbiguousProjectFile));
    }

    /// <summary>Every file under <paramref name="directory"/>. Links to directories are not followed.</summary>
    private static IEnumerable<string> Files(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            foreach (var file in Directory.EnumerateFiles(current)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(current))
                if (new DirectoryInfo(child).LinkTarget is null) pending.Push(child);
        }
    }

    private static string CommonDirectory(IEnumerable<string> files)
    {
        var parents = files.Select(file => Path.GetDirectoryName(file)!).ToArray();
        var common = parents[0];
        foreach (var parent in parents.Skip(1))
            while (!IsWithin(common, parent)) common = Path.GetDirectoryName(common) ?? common;
        return common;
    }

    private static bool IsWithin(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative == "." || !(relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative));
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static Checked<SelectedSources> Selection(string root, IEnumerable<string> documents, string? projectFile)
    {
        if (SourceSelection.TryParse(root, documents, projectFile, out var selection, out var failure))
            return Checked<SelectedSources>.Pass(new SelectedSources(selection, root));
        return Fail(CompileCodes.InvalidSetting, failure.ToString());
    }

    private static Checked<SelectedSources> Fail(DiagnosticCode code, string message) =>
        Checked<SelectedSources>.Fail([Diagnostic.Create(Severity.Error, code, message, null)]);
}
