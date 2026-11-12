// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Morphir.Build.Helpers;

/// <summary>
/// Checks a publish folder against the launch metadata of its artifact, so that the metadata describes the bytes that
/// ship. PackRelease fails on any contradiction.
/// </summary>
public static class PublishedLayout
{
    private const string FSharpCompiler = "FSharp.Compiler.Service.dll";
    private const string RuntimeConfig = "morphir.runtimeconfig.json";
    private const string SharedFramework = "Microsoft.NETCore.App";

    /// <summary>The contradictions between <paramref name="publishDirectory"/> and <paramref name="artifact"/>; empty when none.</summary>
    /// <exception cref="ArgumentException">The artifact is the tool package, which is packed, not published into a folder.</exception>
    public static IReadOnlyList<string> Check(ReleaseArtifact artifact, string publishDirectory)
    {
        if (artifact.Platform is null)
            throw new ArgumentException($"{artifact.FileName} is a tool package, not a publish folder.", nameof(artifact));

        var deployment = artifact.Deployment;
        var problems = new List<string>();
        var files = Directory.GetFiles(publishDirectory, "*", SearchOption.AllDirectories)
            .Select(path => (Path: path, Name: Path.GetRelativePath(publishDirectory, path).Replace('\\', '/')))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
        bool Has(string name) => files.Any(file => file.Name == name);

        if (!Has(artifact.Launch.LaunchPath))
            problems.Add($"the launch path {artifact.Launch.LaunchPath} is not in the publish folder");

        // A single-file artifact is its launch path and nothing else: a library beside it would make it two files.
        if (deployment.Layout == ArtifactLayout.SingleFile)
        {
            foreach (var file in files.Where(file => file.Name != artifact.Launch.LaunchPath))
                problems.Add($"the metadata says the artifact is single-file, {artifact.Launch.LaunchPath} alone, but the publish folder also holds {file.Name}");
        }

        var coreclr = artifact.Platform.OperatingSystem switch
        {
            "windows" => "coreclr.dll",
            "macos" => "libcoreclr.dylib",
            _ => "libcoreclr.so",
        };
        if (deployment.Execution == ExecutionMode.Managed && deployment.BundledRuntime && !Has(coreclr))
            problems.Add($"the metadata says the artifact carries its runtime, but {coreclr} is missing");
        if (deployment.Execution == ExecutionMode.Managed && !deployment.BundledRuntime)
        {
            if (Has(coreclr))
                problems.Add($"the metadata says the artifact runs on an installed runtime, but it carries {coreclr}");
            if (!Has(RuntimeConfig))
                problems.Add($"the metadata says the artifact runs on {deployment.RequiredRuntime}, but {RuntimeConfig} is missing");
            else if (!ReferencesSharedFramework(Path.Combine(publishDirectory, RuntimeConfig)))
                problems.Add($"{RuntimeConfig} has no framework reference to {SharedFramework}");
        }

        if (deployment.InProcessFSharpFrontend && !Has(FSharpCompiler))
            problems.Add($"the metadata says the artifact holds the in-process F# frontend, but {FSharpCompiler} is missing");
        if (!deployment.InProcessFSharpFrontend && Has(FSharpCompiler))
            problems.Add($"the metadata says the artifact has no in-process F# frontend, but it ships {FSharpCompiler}");

        if (deployment.Execution == ExecutionMode.Native)
        {
            foreach (var file in files.Where(file => IsManagedAssembly(file.Path)))
                problems.Add($"the metadata says the artifact is native code, but it ships the managed assembly {file.Name}");
        }

        return problems;
    }

    /// <summary>True when the runtimeconfig names the shared framework as a framework to run on.</summary>
    private static bool ReferencesSharedFramework(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("runtimeOptions", out var options)) return false;
            var frameworks = new List<JsonElement>();
            if (options.TryGetProperty("framework", out var framework)) frameworks.Add(framework);
            if (options.TryGetProperty("frameworks", out var list) && list.ValueKind == JsonValueKind.Array) frameworks.AddRange(list.EnumerateArray());
            // includedFrameworks lists what a self-contained app carries; it is not a reference to an installed runtime.
            return frameworks.Any(candidate =>
                candidate.TryGetProperty("name", out var name) && name.GetString() == SharedFramework);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>True for a PE file with CLI metadata. ELF and Mach-O files, and native PE files, are not.</summary>
    private static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.HasMetadata;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }
}
