// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>The options of <c>extension install</c> from a provider source.</summary>
public sealed record ExtensionInstallOptions
{
    /// <summary>The provider source, for example <c>nuget:Acme.Morphir.Python@1.2.3</c>.</summary>
    public required string Source { get; init; }

    /// <summary>The NuGet source of <c>--source</c>, for a <c>nuget:</c> source.</summary>
    public WorkspaceDirectory? NuGetSource { get; init; }

    /// <summary>Plan only (<c>--dry-run</c>).</summary>
    public bool DryRun { get; init; }
}

/// <summary>
/// The <c>extension install</c> command as intents: write a NuGet folder feed with an extension package, install from
/// a source into the extension home of the scenario, and read what that home records. The command runs with
/// <c>--home</c> on that home and <c>--no-probe</c>.
/// </summary>
public interface IExtensionInstallDriver
{
    /// <summary>The extension home of the scenario, which each install writes.</summary>
    WorkspaceDirectory Home { get; }

    /// <summary>
    /// Writes a NuGet folder feed with version <paramref name="version"/> of the extension package <paramref name="id"/>
    /// (layout A of the NuGet package convention: <c>morphir/release.json</c> and a subtree <c>morphir/&lt;rid&gt;</c>
    /// for each platform of the CI matrix), and returns the feed folder.
    /// </summary>
    WorkspaceDirectory WriteNuGetFolderFeed(string id, string version);

    /// <summary>Runs <c>extension install</c>.</summary>
    Task<CommandResult> InstallAsync(ExtensionInstallOptions options, CancellationToken cancellation = default);

    /// <summary>What the extension home records after <paramref name="result"/>.</summary>
    ExtensionInstallationReport Installation(CommandResult result);
}

/// <summary>Builds the command lines of <c>extension install</c>, and the feeds they read, in the scenario workspace.</summary>
public sealed class ExtensionInstallDriver(ICliDriver cli, IScenarioWorkspace workspace) : IExtensionInstallDriver
{
    /// <summary>The id of the extension that <see cref="WriteNuGetFolderFeed"/> packs.</summary>
    public const string ExtensionId = "com.acme.python";

    /// <summary>The platforms of the CI matrix, with the descriptor triple and the entry point of each.</summary>
    internal static readonly (string Rid, string Triple, string EntryPoint)[] Platforms =
    [
        ("linux-x64", "x86_64-unknown-linux-gnu", "acme-python"),
        ("linux-arm64", "aarch64-unknown-linux-gnu", "acme-python"),
        ("osx-x64", "x86_64-apple-darwin", "acme-python"),
        ("osx-arm64", "aarch64-apple-darwin", "acme-python"),
        ("win-x64", "x86_64-pc-windows-msvc", "acme-python.exe"),
        ("win-arm64", "aarch64-pc-windows-msvc", "acme-python.exe"),
    ];

    internal static readonly byte[] Payload = Encoding.UTF8.GetBytes("#!/bin/sh\necho acme\n");

    public WorkspaceDirectory Home => workspace.Directory("extension-home");

    public WorkspaceDirectory WriteNuGetFolderFeed(string id, string version)
    {
        var feed = workspace.Directory("nuget-feed");
        var fileName = $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg";
        workspace.WriteBytes(feed.File(fileName), Package(id, version));
        return feed;
    }

    public Task<CommandResult> InstallAsync(ExtensionInstallOptions options, CancellationToken cancellation = default) =>
        cli.RunAsync(Arguments(options, Home), cancellation: cancellation);

    public ExtensionInstallationReport Installation(CommandResult result) =>
        ExtensionInstallationReport.Of(result, workspace.EntriesIn(Home), entry => workspace.ReadText(Home.File(entry.Split('/'))));

    /// <summary>The arguments of <c>extension install</c> with <paramref name="home"/>.</summary>
    public static IReadOnlyList<string> Arguments(ExtensionInstallOptions options, WorkspaceDirectory home)
    {
        var arguments = new List<string> { "extension", "install", options.Source };
        if (options.NuGetSource is { } source)
        {
            arguments.Add("--source");
            arguments.Add(source.Path);
        }

        arguments.AddRange(["--home", home.Path, "--no-probe"]);
        if (options.DryRun) arguments.Add("--dry-run");
        return arguments;
    }

    /// <summary>The bytes of a layout A main package: a .nuspec, <c>morphir/release.json</c> and one subtree for each platform.</summary>
    internal static byte[] Package(string id, string version)
    {
        var claims = "{\"protocolVersions\":[\"0.2.0-draft.1\"],\"extension\":{\"id\":\"" + ExtensionId + "\",\"name\":\"Acme Python\",\"version\":\"" + version
            + "\",\"types\":[\"frontend\"]},\"capabilities\":{\"frontend\":{\"languages\":[{\"id\":\"python\",\"fileExtensions\":[\".py\"]}],\"irVersions\":[\"4\"],\"compile\":true}}}";
        var fileName = $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg";
        var artifacts = Platforms.Select(platform =>
            "{\"runtime\":\"process\",\"platform\":\"" + platform.Triple + "\",\"filename\":\"" + fileName + "\",\"package\":{\"format\":\"nupkg\",\"root\":\"morphir/" + platform.Rid
            + "\",\"entryPoint\":\"" + platform.EntryPoint + "\",\"treeSha256\":\"" + TreeDigest(platform.EntryPoint, Payload) + "\"},\"claims\":" + claims + "}");
        var release = "{\"$schema\":\"https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/release.schema.json\",\"extensionId\":\"" + ExtensionId
            + "\",\"shortId\":\"acme-python\",\"version\":\"" + version + "\",\"platformDifferences\":\"declared\",\"artifacts\":[" + string.Join(",", artifacts)
            + "],\"critical\":[\"artifacts.package\"]}";

        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] bytes)
            {
                using var output = zip.CreateEntry(name).Open();
                output.Write(bytes);
            }

            Add($"{id}.nuspec", Encoding.UTF8.GetBytes(
                $"""<?xml version="1.0" encoding="utf-8"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{id}</id><version>{version}</version><authors>Acme</authors><description>A Morphir extension.</description><packageTypes><packageType name="MorphirExtension" /></packageTypes></metadata></package>"""));
            Add("morphir/release.json", Encoding.UTF8.GetBytes(release));
            foreach (var platform in Platforms) Add($"morphir/{platform.Rid}/{platform.EntryPoint}", Payload);
        }

        return memory.ToArray();
    }

    /// <summary>The tree digest of the package contract: the SHA-256 of the canonical manifest of one executable file.</summary>
    internal static string TreeDigest(string path, byte[] content)
    {
        var manifest = $"morphir-package-tree 0.1.0-preview.1\nx {content.Length} {Convert.ToHexStringLower(SHA256.HashData(content))} {path}\n";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
    }
}

/// <summary>
/// What an extension home records after <c>extension install</c>: the result of the command, the installed entries
/// (catalog, locks and store), and the provenance and source of each lock. The command writes no JSON report, so the
/// report reads the home.
/// </summary>
public sealed class ExtensionInstallationReport
{
    private readonly IReadOnlyDictionary<string, JsonElement> _locks;

    private ExtensionInstallationReport(CommandResult result, IReadOnlyList<string> installed, IReadOnlyDictionary<string, JsonElement> locks)
    {
        Result = result;
        Installed = installed;
        _locks = locks;
    }

    public CommandResult Result { get; }

    /// <summary>The files of the catalog, the locks and the store, as paths relative to the home with <c>/</c>.</summary>
    public IReadOnlyList<string> Installed { get; }

    /// <summary>
    /// Reads the home from its <paramref name="entries"/> (relative paths, directories with a trailing <c>/</c>) and the
    /// text of a lock file by its relative path.
    /// </summary>
    public static ExtensionInstallationReport Of(CommandResult result, IReadOnlyList<string> entries, Func<string, string> readText)
    {
        var installed = entries
            .Where(entry => !entry.EndsWith('/'))
            .Where(entry => entry.StartsWith("catalog/", StringComparison.Ordinal) || entry.StartsWith("locks/extensions/", StringComparison.Ordinal)
                || entry.StartsWith("store/", StringComparison.Ordinal))
            .ToArray();
        var locks = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var entry in installed.Where(entry => entry.StartsWith("locks/extensions/", StringComparison.Ordinal) && entry.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var document = JsonDocument.Parse(readText(entry));
            locks[Path.GetFileNameWithoutExtension(entry)] = document.RootElement.Clone();
        }

        return new(result, installed, locks);
    }

    /// <summary>The home has no catalog, lock or store file.</summary>
    public void ShouldHaveNothingInstalled() =>
        Check(Installed.Count == 0, $"The extension home has {string.Join(", ", Installed)}.");

    /// <summary>The lock of <paramref name="extensionId"/> records the NuGet package <paramref name="id"/> <paramref name="version"/> and a package entry source.</summary>
    public void ShouldRecordNuGetPackage(string extensionId, string id, string version)
    {
        Check(_locks.TryGetValue(extensionId, out var locked), $"The extension home has no lock for {extensionId}.");
        var index = locked.GetProperty("index");
        Check(Text(index, "kind") == "nuget-package", $"The lock provenance of {extensionId} is {Text(index, "kind")}, not nuget-package.");
        Check(Text(index, "id") == id, $"The lock of {extensionId} records the package {Text(index, "id")}, not {id}.");
        Check(Text(index, "version") == version, $"The lock of {extensionId} records the version {Text(index, "version")}, not {version}.");
        Check(locked.TryGetProperty("source", out var source) && Text(source, "kind") == "nuget-package-entry",
            $"The lock source of {extensionId} is not a nuget-package-entry.");
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private void Check(bool condition, string message)
    {
        if (!condition) throw new CommandAssertionException(message, Result);
    }
}
