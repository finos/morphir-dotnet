// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.FSharp.Collections;
using Morphir.CodeGeneration.FSharp;
using Morphir.IR.Pipeline;
using Morphir.Myriad.Hosting;

namespace Morphir.Myriad.Build;

/// <summary>Runs the Myriad host for each model module and returns ordered compilation inputs.</summary>
public sealed class GenerateMorphirModules : Microsoft.Build.Utilities.Task
{
    [Required] public ITaskItem[] Models { get; set; } = [];
    [Required] public string ProjectDirectory { get; set; } = "";
    [Required] public string IntermediateDirectory { get; set; } = "";
    [Required] public string HostPath { get; set; } = "";
    [Required] public string PluginPath { get; set; } = "";
    public string DotNetHost { get; set; } = "";
    public bool DesignTimeBuild { get; set; }
    [Output] public ITaskItem[] GeneratedFiles { get; private set; } = [];

    public override bool Execute()
    {
        try
        {
            var generated = new List<ITaskItem>();
            var ownedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inputs = Models.Select(model =>
            {
                var inputPath = Path.GetFullPath(model.ItemSpec, ProjectDirectory);
                return (InputPath: inputPath, OutputRoot: GetOutputRoot(model, inputPath));
            }).ToArray();
            foreach (var (_, outputRoot) in inputs)
            {
                if (!ownedDirectories.Add(outputRoot))
                {
                    Log.LogError($"Morphir output collision: more than one model owns '{outputRoot}'.");
                    return false;
                }
            }

            foreach (var (inputPath, outputRoot) in inputs)
            {
                var options = new PublishOptions(outputRoot, ManifestMode.NewManifest("manifest.json"));
                if (DesignTimeBuild)
                {
                    var manifest = VFileTreeModule.readManifest(options);
                    if (manifest.IsError)
                    {
                        Log.LogError(TreeWriteErrorModule.describe(manifest.ErrorValue));
                        return false;
                    }

                    generated.AddRange(manifest.ResultValue.Select(path => new TaskItem(Path.Combine(outputRoot, path))));
                    continue;
                }

                if (!File.Exists(inputPath))
                {
                    Log.LogError($"Morphir model does not exist: {inputPath}");
                    return false;
                }

                var plan = Transpiler.generate(VFileModule.fromDisk(inputPath));
                if (VFileTreeModule.hasErrors(plan))
                {
                    foreach (var message in VFileTreeModule.collectMessages(plan).Where(message => message.Severity.IsError || message.Severity.IsFatal))
                    {
                        Log.LogError(VMessageModule.describe(message));
                    }

                    return false;
                }

                // Fail on unsafe or colliding paths before starting the host.
                var validation = VFileTreeModule.validatePublish(options, plan);
                if (validation.IsError)
                {
                    Log.LogError($"Morphir generation failed for '{inputPath}': {TreeWriteErrorModule.describe(validation.ErrorValue)}");
                    return false;
                }

                var entries = VFileTreeModule.tryGet(Transpiler.manifest, plan)?.Value?.ToArray() ?? [];
                Directory.CreateDirectory(outputRoot);
                var hostDirectory = Path.Combine(outputRoot, ".host-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(hostDirectory);
                try
                {
                    var compatibleHost = PrepareHost(hostDirectory);
                    var hostOutput = Path.Combine(hostDirectory, "output");
                    var files = new List<VFile>();
                    foreach (var entry in entries)
                    {
                        var outputPath = Path.Combine(hostOutput, entry.Location);
                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                        if (!Generate(inputPath, entry.ModulePath, outputPath, compatibleHost))
                        {
                            return false;
                        }

                        files.Add(VFileModule.ofDiskFile(entry.Location, outputPath));
                    }

                    // Publication begins only after every host invocation has succeeded.
                    var order = ListModule.OfSeq(entries.Select(entry => entry.Location));
                    var tree = VFileTreeModule.set(VFileTreeModule.declaredOrder, order, VFileTreeModule.fromFiles(ListModule.OfSeq(files)));
                    var published = VFileTreeModule.publish(options, tree);
                    if (published.IsError)
                    {
                        Log.LogError($"Morphir generation failed for '{inputPath}': {TreeWriteErrorModule.describe(published.ErrorValue)}");
                        return false;
                    }

                    generated.AddRange(entries.Select(entry => new TaskItem(Path.Combine(outputRoot, entry.Location))));
                }
                finally
                {
                    Directory.Delete(hostDirectory, true);
                }
            }

            GeneratedFiles = generated.ToArray();
            return !Log.HasLoggedErrors;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Log.LogError($"Morphir generation failed: {exception.Message}");
            return false;
        }
    }

    private string GetOutputRoot(ITaskItem model, string inputPath)
    {
        var configured = model.GetMetadata("OutputDirectory");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured, ProjectDirectory));
        }

        var relative = Path.GetRelativePath(ProjectDirectory, inputPath).Replace('\\', '/');
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relative))).ToLowerInvariant()[..12];
        var name = new string(Path.GetFileNameWithoutExtension(inputPath).Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_').ToArray());
        return Path.GetFullPath(Path.Combine(IntermediateDirectory, name + "-" + identity), ProjectDirectory);
    }

    private string PrepareHost(string hostDirectory) =>
        // The plugin's FSharp.Core replaces the one that Myriad 0.85.0 bundles.
        MyriadHost.Prepare(HostPath, Path.Combine(Path.GetDirectoryName(PluginPath)!, "FSharp.Core.dll"), hostDirectory);

    private bool Generate(string inputPath, string modulePath, string outputPath, string compatibleHost)
    {
        if (!File.Exists(compatibleHost) || !File.Exists(PluginPath))
        {
            Log.LogError($"Morphir Myriad host or plugin was not restored. Host: '{HostPath}'; plugin: '{PluginPath}'.");
            return false;
        }

        MyriadRun run;
        try
        {
            run = MyriadHost.Run(DotNetHost, compatibleHost, ProjectDirectory,
                ["--inputfile", inputPath, "--outputfile", outputPath, "--plugin", PluginPath, "--generator-filter", "MorphirFSharpGenerator", "--additionalparams", "MorphirModule=" + modulePath]);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException($"Myriad exceeded two minutes for '{inputPath}' module '{modulePath}'.");
        }

        if (run.ExitCode != 0 || !File.Exists(outputPath))
        {
            Log.LogError($"Myriad failed for '{inputPath}' module '{modulePath}' with exit code {run.ExitCode}: {run.StandardError}{run.StandardOutput}");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(run.StandardError))
        {
            Log.LogMessage(MessageImportance.Low, "{0}", run.StandardError.Trim());
        }

        return true;
    }
}
