// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Packaging;

/// <summary>
/// One WASM engine of a packaged artifact as its <c>&lt;artifact&gt;.build-contents.json</c> records it under
/// <c>engines</c> (build/Helpers/PackagedEngine.cs): for a separate library its path in the artifact and the file's
/// SHA-256, for a linked engine the executable it is linked into.
/// </summary>
public sealed record RecordedEngine(string Path, string Platform, string Sha256, string Delivery, string Version)
{
    /// <summary>The <c>engines</c> of a build contents document; null when the member is missing or null.</summary>
    public static IReadOnlyList<RecordedEngine>? ReadAll(string buildContentsJson)
    {
        using var document = JsonDocument.Parse(buildContentsJson);
        if (!document.RootElement.TryGetProperty("engines", out var engines) || engines.ValueKind != JsonValueKind.Array) return null;
        return [.. engines.EnumerateArray().Select(engine => new RecordedEngine(
            engine.GetProperty("path").GetString()!,
            engine.GetProperty("platform").GetString()!,
            engine.GetProperty("sha256").GetString()!,
            engine.GetProperty("delivery").GetString()!,
            engine.GetProperty("version").GetString()!))];
    }
}

/// <summary>
/// The engine library files of an installed artifact against the engines its build contents record (add-wasm-extension-host,
/// task 5.2; requirement "Packaged engine is qualified"). The installed files that are engine libraries must be exactly the
/// recorded separate libraries, each with its recorded SHA-256, and a linked engine has no engine library file. An archive
/// offering carries one engine, for its own platform; the portable tool package carries one library per recorded platform,
/// each under that platform's <c>runtimes/&lt;rid&gt;/native/</c>, and one for this machine's platform. How many platforms
/// the tool carries is read from the record, not assumed.
/// </summary>
public static class PackagedEngineLayout
{
    /// <summary>The delivery of an engine that is a file of its own.</summary>
    public const string SeparateLibrary = "separate-library";

    /// <summary>The delivery of an engine linked into the executable.</summary>
    public const string Linked = "linked";

    /// <summary>The engine library's file names in the Wasmtime package, one per operating system.</summary>
    public static IReadOnlyList<string> LibraryNames { get; } = ["wasmtime.dll", "libwasmtime.so", "libwasmtime.dylib"];

    /// <summary>True for a file name that is an engine library on some platform.</summary>
    public static bool IsEngineLibrary(string fileName) => LibraryNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What contradicts the record; empty when the installation agrees with it.
    /// </summary>
    /// <param name="installDirectory">Everything the installation put on disk.</param>
    /// <param name="artifactRoot">The folder whose relative paths are the artifact's entry paths: the installation directory
    /// for an archive, the tool store's package folder for the tool package.</param>
    /// <param name="deployment">The offering.</param>
    /// <param name="rid">This machine's platform.</param>
    /// <param name="recorded">The recorded engines, or null when the build contents record none.</param>
    public static IReadOnlyList<string> Problems(string installDirectory, string artifactRoot, string deployment, string rid, IReadOnlyList<RecordedEngine>? recorded)
    {
        if (recorded is null || recorded.Count == 0)
            return ["The build contents record no engines, so the engine the artifact carries cannot be checked. Pack it again with PackRelease."];

        var problems = new List<string>();
        var root = System.IO.Path.GetFullPath(artifactRoot);
        var installed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories).Where(file => IsEngineLibrary(System.IO.Path.GetFileName(file))))
        {
            var full = System.IO.Path.GetFullPath(file);
            var relative = System.IO.Path.GetRelativePath(root, full).Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
                problems.Add($"The engine library {System.IO.Path.GetRelativePath(installDirectory, full).Replace('\\', '/')} is outside the artifact's files in {root}.");
            else
                installed[relative] = full;
        }

        foreach (var engine in recorded.Where(engine => engine.Delivery is not (SeparateLibrary or Linked)))
            problems.Add($"The engine {engine.Path} has the delivery '{engine.Delivery}', neither {SeparateLibrary} nor {Linked}.");

        var separate = recorded.Where(engine => engine.Delivery == SeparateLibrary).ToList();
        foreach (var engine in separate)
        {
            if (!installed.TryGetValue(engine.Path, out var file))
            {
                problems.Add($"The build contents record the engine library {engine.Path} ({engine.Platform}), and it is not in the installed artifact.");
                continue;
            }

            var sha256 = ReleaseFiles.Sha256(file);
            if (sha256 != engine.Sha256)
                problems.Add($"The engine library {engine.Path} has SHA-256 {sha256}, not the {engine.Sha256} its build contents record for {engine.Platform}.");
        }

        foreach (var extra in installed.Keys.Except(separate.Select(engine => engine.Path), StringComparer.Ordinal).Order(StringComparer.Ordinal))
            problems.Add($"The installed artifact has the engine library {extra}, not recorded as a separate library in its build contents.");

        foreach (var engine in recorded.Where(engine => engine.Delivery == Linked))
        {
            var executable = System.IO.Path.Combine(root, engine.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!File.Exists(executable) || IsEngineLibrary(System.IO.Path.GetFileName(executable)))
                problems.Add($"The build contents record the engine as linked into {engine.Path}, which is not an executable of the installed artifact.");
        }

        if (deployment == "dotnet-tool")
        {
            foreach (var engine in separate.Where(engine => !engine.Path.Contains($"runtimes/{engine.Platform}/native/", StringComparison.Ordinal)))
                problems.Add($"The tool's engine library {engine.Path} is recorded for {engine.Platform} and is not under runtimes/{engine.Platform}/native/.");
            foreach (var platform in separate.GroupBy(engine => engine.Platform, StringComparer.Ordinal).Where(group => group.Count() > 1))
                problems.Add($"The tool records {platform.Count()} engine libraries for {platform.Key}.");
            if (separate.All(engine => engine.Platform != rid))
                problems.Add($"The tool package records no engine library for {rid}, this machine's platform.");
        }
        else
        {
            if (recorded.Count != 1)
                problems.Add($"The {deployment} artifact records {recorded.Count} engines; an archive offering carries one, for its platform.");
            foreach (var engine in recorded.Where(engine => engine.Platform != rid))
                problems.Add($"The {deployment} artifact for {rid} records an engine for {engine.Platform}: {engine.Path}.");
        }

        return problems;
    }
}
