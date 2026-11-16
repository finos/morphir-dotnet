// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Morphir.Lang.CSharp.Backend;

namespace Morphir.Lang.CSharp.Build;

/// <summary>
/// Writes the C# of each model into the output folder with the pipeline function (design.html, BLD-1). The task writes a
/// file only when its bytes change, so an unchanged model keeps the time of its files, and it deletes the C# files of the
/// folder that no model writes now. The folder belongs to the task.
/// </summary>
public sealed class GenerateMorphirCSharp : ITask
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public IBuildEngine BuildEngine { get; set; } = null!;

    /// <inheritdoc />
    public ITaskHost? HostObject { get; set; }

    /// <summary>The model files (<c>*.morphir.json</c>). With no model, the task deletes the C# files of the folder.</summary>
    public ITaskItem[] Models { get; set; } = [];

    /// <summary>The folder of the generated files, in the intermediate output folder of the project.</summary>
    [Required]
    public string OutputDirectory { get; set; } = "";

    /// <summary>The namespace of all generated files. Empty: the namespace of the package of the model.</summary>
    public string Namespace { get; set; } = "";

    /// <summary>The target framework identifier of the project, for example <c>.NETCoreApp</c>.</summary>
    public string TargetFrameworkIdentifier { get; set; } = "";

    /// <summary>The target framework version of the project, for example <c>v10.0</c>.</summary>
    public string TargetFrameworkVersion { get; set; } = "";

    /// <summary>The generated files, in the order of their paths.</summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = [];

    /// <inheritdoc />
    public bool Execute()
    {
        NamespaceOverride? namespaceOverride = null;
        if (Namespace.Trim().Length > 0)
        {
            var parsed = NamespaceOverride.TryCreate(Namespace.Trim());
            if (parsed.IsError)
            {
                Error("", $"{parsed.ErrorValue.Rule}: MorphirCSharpNamespace: {parsed.ErrorValue.Message}");
                return false;
            }

            namespaceOverride = parsed.ResultValue;
        }

        var root = Path.GetFullPath(OutputDirectory);
        var files = new SortedDictionary<string, (string Model, string Text)>(StringComparer.Ordinal);
        var failed = false;
        foreach (var model in Models)
        {
            var path = model.GetMetadata("FullPath");
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Error(path, $"unreadable-model: {error.Message}");
                failed = true;
                continue;
            }

            var request = CSharpPipelineRequest.Create(bytes).ForTargetFramework(TargetFrameworkIdentifier, TargetFrameworkVersion);
            if (namespaceOverride is not null) request = request.WithNamespace(namespaceOverride);
            var result = CSharpPipeline.Generate(request, CancellationToken.None);
            foreach (var diagnostic in result.Diagnostics)
            {
                if (diagnostic.IsError) Error(path, diagnostic.ToString());
                else if (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning) Warning(path, diagnostic.ToString());
            }

            if (!result.Succeeded)
            {
                failed = true;
                continue;
            }

            foreach (var file in result.Files)
            {
                if (files.TryGetValue(file.RelativePath, out var earlier))
                {
                    // A file that two models write with one text (the polyfill file) is written one time.
                    if (!string.Equals(earlier.Text, file.Text, StringComparison.Ordinal))
                    {
                        BuildEngine.LogErrorEvent(new BuildErrorEventArgs("", "MORPHIR2003", path, 0, 0, 0, 0,
                            $"The models '{earlier.Model}' and '{path}' both write '{file.RelativePath}' with different text.", "", nameof(GenerateMorphirCSharp)));
                        failed = true;
                    }

                    continue;
                }

                files.Add(file.RelativePath, (path, file.Text));
            }
        }

        if (failed) return false;

        var written = new List<ITaskItem>();
        foreach (var file in files)
        {
            var target = Path.GetFullPath(Path.Combine(root, file.Key));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                Error(file.Value.Model, $"invalid-path: The generated path '{file.Key}' is outside the output folder.");
                return false;
            }

            var bytes = Utf8.GetBytes(file.Value.Text);
            if (!File.Exists(target) || !File.ReadAllBytes(target).SequenceEqual(bytes))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }

            written.Add(new GeneratedFile(target));
        }

        // A C# file of the folder that no model writes now goes, so a removed module is not compiled.
        var keep = new HashSet<string>(written.Select(item => item.ItemSpec), StringComparer.Ordinal);
        if (Directory.Exists(root))
        {
            foreach (var stale in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(path => !keep.Contains(path)).ToList())
                File.Delete(stale);
        }

        GeneratedFiles = [.. written];
        return true;
    }

    private void Error(string file, string message) =>
        BuildEngine.LogErrorEvent(new BuildErrorEventArgs("", "MORPHIR2001", file, 0, 0, 0, 0, message, "", nameof(GenerateMorphirCSharp)));

    private void Warning(string file, string message) =>
        BuildEngine.LogWarningEvent(new BuildWarningEventArgs("", "MORPHIR2002", file, 0, 0, 0, 0, message, "", nameof(GenerateMorphirCSharp)));
}
