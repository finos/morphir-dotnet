// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Morphir.CodeGeneration.FSharp;

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
                var manifestPath = Path.Combine(outputRoot, "manifest.json");
                if (DesignTimeBuild)
                {
                    generated.AddRange(ReadManifest(manifestPath).Select(path => new TaskItem(OwnedPath(outputRoot, path))));
                    continue;
                }

                if (!File.Exists(inputPath))
                {
                    Log.LogError($"Morphir model does not exist: {inputPath}");
                    return false;
                }

                var result = Transpiler.Generate(inputPath, File.ReadAllText(inputPath));
                if (result.IsError)
                {
                    foreach (var diagnostic in result.ErrorValue)
                    {
                        Log.LogError($"{diagnostic.InputPath}: {diagnostic.Category} at {diagnostic.Location}: {diagnostic.Message}");
                    }

                    return false;
                }

                var modules = result.ResultValue.ToArray();
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var module in modules)
                {
                    _ = OwnedPath(outputRoot, module.RelativePath);
                    if (!paths.Add(module.RelativePath.Replace('\\', '/')))
                    {
                        Log.LogError($"Morphir generated path collision in '{inputPath}': {module.RelativePath}");
                        return false;
                    }
                }

                Directory.CreateDirectory(outputRoot);
                using var publicationLock = AcquireLock(Path.Combine(outputRoot, ".publish.lock"));
                var stage = Path.Combine(outputRoot, ".stage-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                try
                {
                    var compatibleHost = PrepareHost(stage);
                    foreach (var module in modules)
                    {
                        var stagePath = OwnedPath(stage, module.RelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(stagePath)!);
                        if (!Generate(inputPath, module.ModulePath, stagePath, compatibleHost))
                        {
                            return false;
                        }
                    }

                    // Publication begins only after every host invocation has succeeded.
                    var oldPaths = ReadManifest(manifestPath);
                    foreach (var module in modules)
                    {
                        var source = OwnedPath(stage, module.RelativePath);
                        var destination = OwnedPath(outputRoot, module.RelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        if (!File.Exists(destination) || !File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(destination)))
                        {
                            File.Move(source, destination, true);
                        }
                    }

                    var manifest = JsonSerializer.Serialize(modules.Select(module => module.RelativePath.Replace('\\', '/')).ToArray()) + Environment.NewLine;
                    if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath) != manifest)
                    {
                        var stagedManifest = Path.Combine(stage, "manifest.json");
                        File.WriteAllText(stagedManifest, manifest);
                        File.Move(stagedManifest, manifestPath, true);
                    }

                    foreach (var obsolete in oldPaths.Where(path => !paths.Contains(path)))
                    {
                        File.Delete(OwnedPath(outputRoot, obsolete));
                    }

                    generated.AddRange(modules.Select(module => new TaskItem(OwnedPath(outputRoot, module.RelativePath))));
                }
                finally
                {
                    Directory.Delete(stage, true);
                }
            }

            GeneratedFiles = generated.ToArray();
            return !Log.HasLoggedErrors;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
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
            return Path.GetFullPath(configured, ProjectDirectory);
        }

        var relative = Path.GetRelativePath(ProjectDirectory, inputPath).Replace('\\', '/');
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relative))).ToLowerInvariant()[..12];
        var name = new string(Path.GetFileNameWithoutExtension(inputPath).Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_').ToArray());
        return Path.GetFullPath(Path.Combine(IntermediateDirectory, name + "-" + identity), ProjectDirectory);
    }

    private string PrepareHost(string stage)
    {
        if (!File.Exists(HostPath))
        {
            throw new InvalidOperationException($"Myriad host could not be found: '{HostPath}'.");
        }

        var sourceRoot = Path.GetDirectoryName(HostPath)!;
        var destinationRoot = Path.Combine(stage, ".host");
        Directory.CreateDirectory(destinationRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        // Myriad 0.85.0 bundles FSharp.Core 9. Backend assemblies target Core 10.
        // A single higher-version Core must load before the host and plugin share
        // Fantomas async APIs. Never modify the restored tool in the NuGet cache.
        var sharedCore = Path.Combine(Path.GetDirectoryName(PluginPath)!, "FSharp.Core.dll");
        File.Copy(sharedCore, Path.Combine(destinationRoot, "FSharp.Core.dll"), true);
        return Path.Combine(destinationRoot, Path.GetFileName(HostPath));
    }

    private bool Generate(string inputPath, string modulePath, string outputPath, string compatibleHost)
    {
        if (!File.Exists(compatibleHost) || !File.Exists(PluginPath))
        {
            Log.LogError($"Morphir Myriad host or plugin was not restored. Host: '{HostPath}'; plugin: '{PluginPath}'.");
            return false;
        }

        var host = string.IsNullOrWhiteSpace(DotNetHost) ? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet" : DotNetHost;
        var start = new ProcessStartInfo(host)
        {
            WorkingDirectory = ProjectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "--roll-forward", "LatestMajor", compatibleHost, "--inputfile", inputPath, "--outputfile", outputPath, "--plugin", PluginPath, "--generator-filter", "MorphirFSharpGenerator", "--additionalparams", "MorphirModule=" + modulePath })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Myriad host.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000))
        {
            process.Kill(true);
            throw new InvalidOperationException($"Myriad exceeded two minutes for '{inputPath}' module '{modulePath}'.");
        }

        var output = stdout.GetAwaiter().GetResult();
        var diagnostics = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            Log.LogError($"Myriad failed for '{inputPath}' module '{modulePath}' with exit code {process.ExitCode}: {diagnostics}{output}");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(diagnostics))
        {
            Log.LogMessage(MessageImportance.Low, "{0}", diagnostics.Trim());
        }

        return true;
    }

    private static string[] ReadManifest(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? throw new InvalidOperationException($"Invalid generated-file manifest '{path}'.")
        : [];

    private static string OwnedPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Replace('\\', '/').Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidOperationException($"Invalid generated relative path '{relative}'.");
        }

        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Generated path '{relative}' escapes '{root}'.");
        }

        var directory = Path.GetFullPath(root);
        foreach (var segment in relative.Replace('\\', '/').Split('/').SkipLast(1).Prepend(""))
        {
            directory = Path.Combine(directory, segment);
            if (Directory.Exists(directory) && new DirectoryInfo(directory).LinkTarget is not null)
            {
                throw new InvalidOperationException($"Generated directory '{directory}' is a symbolic link.");
            }
        }

        return path;
    }

    private static FileStream AcquireLock(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 100)
            {
                Thread.Sleep(100);
            }
        }
    }
}
