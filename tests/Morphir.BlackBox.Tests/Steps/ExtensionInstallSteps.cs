// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of <c>extension install</c> from a NuGet folder feed. The one scenario of the capability gives its
/// arguments to <see cref="ICliDriver"/>; the capability gets a driver interface with its second scenario.
/// </summary>
[Binding]
public sealed class ExtensionInstallSteps(ICliDriver cli, IScenarioWorkspace workspace, ScenarioRun run)
{
    private const string ExtensionId = "com.acme.python";

    /// <summary>The platforms of the CI matrix, with the triple and the entry point of each.</summary>
    private static readonly (string Rid, string Triple, string EntryPoint)[] Platforms =
    [
        ("linux-x64", "x86_64-unknown-linux-gnu", "acme-python"),
        ("linux-arm64", "aarch64-unknown-linux-gnu", "acme-python"),
        ("osx-x64", "x86_64-apple-darwin", "acme-python"),
        ("osx-arm64", "aarch64-apple-darwin", "acme-python"),
        ("win-x64", "x86_64-pc-windows-msvc", "acme-python.exe"),
        ("win-arm64", "aarch64-pc-windows-msvc", "acme-python.exe"),
    ];

    private WorkspaceDirectory Feed => workspace.Directory("nuget-feed");

    private WorkspaceDirectory ExtensionHome => workspace.Directory("extension-home");

    /// <summary>
    /// Layout A of the NuGet package convention: one package with <c>morphir/release.json</c> and a subtree
    /// <c>morphir/&lt;rid&gt;</c> for each platform, as an extension author packs it.
    /// </summary>
    [Given("a NuGet folder feed with version {string} of the extension package {string}")]
    public void AFolderFeedWithTheExtensionPackage(string version, string id)
    {
        var payload = Encoding.UTF8.GetBytes("#!/bin/sh\necho acme\n");
        var claims = "{\"protocolVersions\":[\"0.2.0-draft.1\"],\"extension\":{\"id\":\"" + ExtensionId + "\",\"name\":\"Acme Python\",\"version\":\"" + version
            + "\",\"types\":[\"frontend\"]},\"capabilities\":{\"frontend\":{\"languages\":[{\"id\":\"python\",\"fileExtensions\":[\".py\"]}],\"irVersions\":[\"4\"],\"compile\":true}}}";
        var fileName = $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg";
        var artifacts = Platforms.Select(platform =>
            "{\"runtime\":\"process\",\"platform\":\"" + platform.Triple + "\",\"filename\":\"" + fileName + "\",\"package\":{\"format\":\"nupkg\",\"root\":\"morphir/" + platform.Rid
            + "\",\"entryPoint\":\"" + platform.EntryPoint + "\",\"treeSha256\":\"" + TreeDigest(platform.EntryPoint, payload) + "\"},\"claims\":" + claims + "}");
        var release = $$$"""{"$schema":"https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/release.schema.json","extensionId":"{{{ExtensionId}}}","shortId":"acme-python","version":"{{{version}}}","platformDifferences":"declared","artifacts":[{{{string.Join(",", artifacts)}}}],"critical":["artifacts.package"]}""";

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
            foreach (var platform in Platforms) Add($"morphir/{platform.Rid}/{platform.EntryPoint}", payload);
        }

        workspace.WriteBytes(Feed.File(fileName), memory.ToArray());
    }

    [When("I install the extension {string} from the NuGet folder feed")]
    public async Task InstallFromTheFolderFeed(string source) => run.Result = await cli.RunAsync(Arguments(source, dryRun: false));

    [When("I plan the installation of the extension {string} from the NuGet folder feed")]
    public async Task PlanFromTheFolderFeed(string source) => run.Result = await cli.RunAsync(Arguments(source, dryRun: true));

    [Then("the extension home records the NuGet package {string} version {string}")]
    public void TheHomeRecordsTheNuGetPackage(string id, string version)
    {
        var lockFile = ExtensionHome.File("locks", "extensions", $"{ExtensionId}.json");
        Check(workspace.Exists(lockFile), $"The extension home has no lock for {ExtensionId}.");
        var document = JsonNode.Parse(workspace.ReadText(lockFile))!;
        var index = document["index"]!;
        Check((string?)index["kind"] == "nuget-package", $"The lock provenance is {index["kind"]}, not nuget-package.");
        Check((string?)index["id"] == id, $"The lock records the package {index["id"]}, not {id}.");
        Check((string?)index["version"] == version, $"The lock records the version {index["version"]}, not {version}.");
        Check((string?)document["source"]?["kind"] == "nuget-package-entry", "The lock source is not a nuget-package-entry.");
    }

    [Then("the extension home has no installed extension")]
    public void TheHomeHasNoInstalledExtension()
    {
        var entries = workspace.EntriesIn(ExtensionHome).Where(entry => entry.StartsWith("catalog/", StringComparison.Ordinal)
            || entry.StartsWith("locks/extensions/", StringComparison.Ordinal) || entry.StartsWith("store/", StringComparison.Ordinal)).ToArray();
        Check(entries.Length == 0, $"The extension home has {string.Join(", ", entries)}.");
    }

    private string[] Arguments(string source, bool dryRun)
    {
        var arguments = new List<string> { "extension", "install", source, "--source", Feed.Path, "--home", ExtensionHome.Path, "--no-probe" };
        if (dryRun) arguments.Add("--dry-run");
        return [.. arguments];
    }

    /// <summary>The tree digest of the package contract: the SHA-256 of the canonical manifest of one executable file.</summary>
    private static string TreeDigest(string path, byte[] content)
    {
        var manifest = $"morphir-package-tree 0.1.0-preview.1\nx {content.Length} {Convert.ToHexStringLower(SHA256.HashData(content))} {path}\n";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
