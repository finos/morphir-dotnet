// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Morphir.Myriad.Hosting;

namespace Morphir.Myriad.SystemTextJson.Build;

/// <summary>
/// Runs the Myriad host once for each FSharpJsonContext item, and returns the compile items with each
/// generated file right after its input file.
/// </summary>
public sealed partial class GenerateJsonContexts : Microsoft.Build.Utilities.Task
{
    /// <summary>The FSharpJsonContext items. The item is the input file; custom metadata are context settings.</summary>
    [Required] public ITaskItem[] Contexts { get; set; } = [];

    /// <summary>The Compile items of the project, in order.</summary>
    [Required] public ITaskItem[] Compile { get; set; } = [];

    /// <summary>The project directory.</summary>
    [Required] public string ProjectDirectory { get; set; } = "";

    /// <summary>The directory for generated files and stamps.</summary>
    [Required] public string OutputDirectory { get; set; } = "";

    /// <summary>The Myriad host (Myriad.dll).</summary>
    [Required] public string HostPath { get; set; } = "";

    /// <summary>The plugin assembly. Its folder holds the FSharp.Core that the host uses.</summary>
    [Required] public string PluginPath { get; set; } = "";

    /// <summary>The dotnet executable.</summary>
    public string DotNetHost { get; set; } = "";

    /// <summary>True in a design-time build: return the last generated files and run nothing.</summary>
    public bool DesignTimeBuild { get; set; }

    /// <summary>The Compile items with the generated files in place.</summary>
    [Output] public ITaskItem[] OrderedCompile { get; private set; } = [];

    /// <summary>The generated files.</summary>
    [Output] public ITaskItem[] GeneratedFiles { get; private set; } = [];

    private const string GeneratorClass = "MorphirSystemTextJsonGenerator";

    [GeneratedRegex(@"^(?:(?<file>.+?)(?:\((?<line>\d+),(?<column>\d+)\))?: )?error (?<code>MSTJ\d{4}): (?<message>.*)$", RegexOptions.Multiline)]
    private static partial Regex ErrorLine();

    private sealed record ContextJob(ITaskItem Item, string InputPath, string Name, string OutputPath, string StampPath, string[] Parameters);

    /// <inheritdoc />
    public override bool Execute()
    {
        try
        {
            var jobs = Plan();
            if (jobs is null)
            {
                return false;
            }

            Directory.CreateDirectory(OutputDirectory);
            string? preparedHost = null;
            string? hostDirectory = null;
            try
            {
                foreach (var job in jobs)
                {
                    if (DesignTimeBuild || IsCurrent(job))
                    {
                        continue;
                    }

                    if (preparedHost is null)
                    {
                        hostDirectory = Path.Combine(OutputDirectory, ".host-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(hostDirectory);
                        preparedHost = MyriadHost.Prepare(HostPath, Path.Combine(Path.GetDirectoryName(PluginPath)!, "FSharp.Core.dll"), hostDirectory);
                    }

                    if (!Generate(job, preparedHost, hostDirectory!))
                    {
                        return false;
                    }
                }
            }
            finally
            {
                if (hostDirectory is not null)
                {
                    Directory.Delete(hostDirectory, true);
                }
            }

            var generatedByInput = jobs
                .Where(job => File.Exists(job.OutputPath))
                .ToLookup(job => job.InputPath, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<ITaskItem>();
            foreach (var item in Compile)
            {
                ordered.Add(item);
                foreach (var job in generatedByInput[Path.GetFullPath(item.ItemSpec, ProjectDirectory)])
                {
                    ordered.Add(new TaskItem(job.OutputPath));
                }
            }

            OrderedCompile = [.. ordered];
            GeneratedFiles = [.. jobs.Where(job => File.Exists(job.OutputPath)).Select(job => new TaskItem(job.OutputPath))];
            return !Log.HasLoggedErrors;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            Log.LogError($"System.Text.Json context generation failed: {exception.Message}");
            return false;
        }
    }

    /// <summary>Checks the items and works out each context's files. Returns null after logging errors.</summary>
    private List<ContextJob>? Plan()
    {
        var compileInputs = new HashSet<string>(Compile.Select(item => Path.GetFullPath(item.ItemSpec, ProjectDirectory)), StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jobs = new List<ContextJob>();
        foreach (var item in Contexts)
        {
            var inputPath = Path.GetFullPath(item.ItemSpec, ProjectDirectory);
            var metadata = item.CloneCustomMetadata();
            var settings = metadata.Keys.Cast<object>()
                .Select(key => (Key: key.ToString() ?? "", Value: metadata[key]?.ToString() ?? ""))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToArray();
            var name = settings.FirstOrDefault(entry => entry.Key == "Name").Value ?? "";
            if (!compileInputs.Contains(inputPath))
            {
                Log.LogError(null, "MSTJ0002", null, inputPath, 0, 0, 0, 0, $"The FSharpJsonContext input '{item.ItemSpec}' is not a Compile item of this project. Add it to Compile before the files that use the context.");
                continue;
            }

            // A name that is not a file name is refused by the generator with its own message; use a safe file name here.
            var fileName = new string(name.Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            if (fileName.Length == 0)
            {
                fileName = "Context";
            }

            if (!names.Add(fileName))
            {
                Log.LogError(null, "MSTJ0004", null, inputPath, 0, 0, 0, 0, $"More than one FSharpJsonContext is named '{name}'.");
                continue;
            }

            var parameters = settings.Select(entry => entry.Key + "=" + entry.Value).ToArray();
            jobs.Add(new ContextJob(item, inputPath, name, Path.Combine(OutputDirectory, fileName + ".g.fs"), Path.Combine(OutputDirectory, fileName + ".stamp"), parameters));
        }

        return Log.HasLoggedErrors ? null : jobs;
    }

    /// <summary>The stamp of a job: the input text, the settings and the plugin build.</summary>
    private string Stamp(ContextJob job)
    {
        var plugin = new FileInfo(PluginPath);
        var text = string.Join("\n", [File.ReadAllText(job.InputPath), .. job.Parameters, plugin.Length.ToString(), plugin.LastWriteTimeUtc.Ticks.ToString()]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private bool IsCurrent(ContextJob job) =>
        File.Exists(job.OutputPath) && File.Exists(job.StampPath) && File.ReadAllText(job.StampPath) == Stamp(job);

    private bool Generate(ContextJob job, string preparedHost, string hostDirectory)
    {
        if (!File.Exists(PluginPath))
        {
            Log.LogError($"The Morphir.Myriad.SystemTextJson plugin was not found: '{PluginPath}'.");
            return false;
        }

        var temporary = Path.Combine(hostDirectory, Path.GetFileName(job.OutputPath));
        // Myriad takes one key=value after each --additionalparams flag.
        var run = MyriadHost.Run(DotNetHost, preparedHost, ProjectDirectory,
            ["--inputfile", job.InputPath, "--outputfile", temporary, "--plugin", PluginPath, "--generator-filter", GeneratorClass,
             .. job.Parameters.SelectMany(parameter => new[] { "--additionalparams", parameter })]);

        if (run.ExitCode != 0 || !File.Exists(temporary))
        {
            var reported = false;
            foreach (Match match in ErrorLine().Matches(run.StandardError))
            {
                var file = match.Groups["file"].Success ? match.Groups["file"].Value : job.InputPath;
                var line = match.Groups["line"].Success ? int.Parse(match.Groups["line"].Value) : 0;
                var column = match.Groups["column"].Success ? int.Parse(match.Groups["column"].Value) : 0;
                Log.LogError(null, match.Groups["code"].Value, null, file, line, column, 0, 0, match.Groups["message"].Value.TrimEnd());
                reported = true;
            }

            if (!reported)
            {
                Log.LogError(null, "MSTJ0001", null, job.InputPath, 0, 0, 0, 0, $"Myriad failed for context '{job.Name}' with exit code {run.ExitCode}: {run.StandardError}{run.StandardOutput}");
            }

            return false;
        }

        var generated = File.ReadAllText(temporary);
        if (!File.Exists(job.OutputPath) || File.ReadAllText(job.OutputPath) != generated)
        {
            File.WriteAllText(job.OutputPath, generated);
        }

        File.WriteAllText(job.StampPath, Stamp(job));
        return true;
    }
}
