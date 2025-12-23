using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Morphir.Tooling.Features.Management;

/// <summary>
/// Handlers for extension management commands.
/// Extensions follow the same pattern as tools.
/// </summary>
public static class ExtensionHandlers
{
    public static Task<ExtensionListResult> HandleList(
        ExtensionList command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Listing extensions for platform {Platform}, local={Local}", platform, command.Local);

        var extensionRoot = resolver.GetArtifactRoot(ArtifactType.Extension, command.Local);
        var platformRoot = Path.Combine(extensionRoot, platform);

        var extensions = new List<ExtensionInfo>();

        if (Directory.Exists(platformRoot))
        {
            foreach (var extensionDir in Directory.GetDirectories(platformRoot))
            {
                var extensionName = Path.GetFileName(extensionDir);
                var versions = Directory.GetDirectories(extensionDir)
                    .Select(Path.GetFileName)
                    .Where(v => v != null)
                    .Cast<string>()
                    .ToList();

                var activeVersion = resolver.GetActiveVersion(ArtifactType.Extension, name: extensionName, useLocal: command.Local);

                extensions.Add(new ExtensionInfo(
                    Name: extensionName,
                    InstalledVersions: versions,
                    ActiveVersion: activeVersion,
                    Platform: platform
                ));
            }
        }

        return Task.FromResult(new ExtensionListResult(
            Extensions: extensions.OrderBy(e => e.Name).ToList(),
            Platform: platform,
            IsLocal: command.Local
        ));
    }

    public static async Task<ExtensionInstallResult> HandleInstall(
        ExtensionInstall command,
        ArtifactPathResolver resolver,
        HttpClient httpClient,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Installing extension {Name} version {Version} for platform {Platform} from {Url}",
            command.Name, command.Version, platform, command.SourceUrl);

        try
        {
            var installPath = resolver.GetArtifactPath(ArtifactType.Extension, platform, command.Version, command.Name, command.Local);
            var binPath = resolver.GetBinPath(ArtifactType.Extension, platform, command.Version, command.Name, command.Local);

            Directory.CreateDirectory(binPath);

            logger?.LogInformation("Downloading artifact from {Url}", command.SourceUrl);
            var response = await httpClient.GetAsync(command.SourceUrl, ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsByteArrayAsync(ct);
            var downloadedFile = Path.Combine(binPath, "artifact");
            await File.WriteAllBytesAsync(downloadedFile, content, ct);

            var manifest = new Manifest
            {
                Name = command.Name,
                Version = command.Version,
                Platform = platform,
                SourceUrl = command.SourceUrl,
                InstalledAt = DateTime.UtcNow
            };

            var manifestPath = resolver.GetManifestPath(ArtifactType.Extension, platform, command.Version, command.Name, command.Local);
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

            return new ExtensionInstallResult(
                Success: true,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                InstalledPath: installPath
            );
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to install extension {Name}", command.Name);
            return new ExtensionInstallResult(
                Success: false,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                InstalledPath: string.Empty,
                ErrorMessage: ex.Message
            );
        }
    }

    public static Task<ExtensionUseResult> HandleUse(
        ExtensionUse command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Setting active extension {Name} to version {Version} for platform {Platform}, local={Local}",
            command.Name, command.Version, platform, command.Local);

        try
        {
            var extensionPath = resolver.GetArtifactPath(ArtifactType.Extension, platform, command.Version, command.Name, command.Local);
            if (!Directory.Exists(extensionPath))
            {
                return Task.FromResult(new ExtensionUseResult(
                    Success: false,
                    Name: command.Name,
                    Version: command.Version,
                    Platform: platform,
                    IsLocal: command.Local,
                    ErrorMessage: $"Extension {command.Name} version {command.Version} for platform {platform} is not installed"
                ));
            }

            resolver.SetActiveVersion(ArtifactType.Extension, command.Version, name: command.Name, useLocal: command.Local);

            return Task.FromResult(new ExtensionUseResult(
                Success: true,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local
            ));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to set active extension {Name}", command.Name);
            return Task.FromResult(new ExtensionUseResult(
                Success: false,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local,
                ErrorMessage: ex.Message
            ));
        }
    }

    public static Task<ExtensionRemoveResult> HandleRemove(
        ExtensionRemove command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Removing extension {Name} version {Version} for platform {Platform}, local={Local}",
            command.Name, command.Version, platform, command.Local);

        try
        {
            var extensionPath = resolver.GetArtifactPath(ArtifactType.Extension, platform, command.Version, command.Name, command.Local);

            if (!Directory.Exists(extensionPath))
            {
                return Task.FromResult(new ExtensionRemoveResult(
                    Success: false,
                    Name: command.Name,
                    Version: command.Version,
                    Platform: platform,
                    IsLocal: command.Local,
                    ErrorMessage: $"Extension {command.Name} version {command.Version} is not installed"
                ));
            }

            var activeVersion = resolver.GetActiveVersion(ArtifactType.Extension, name: command.Name, useLocal: command.Local);
            if (activeVersion == command.Version)
            {
                logger?.LogWarning("Removing active extension {Name} version {Version}", command.Name, command.Version);
            }

            Directory.Delete(extensionPath, recursive: true);

            return Task.FromResult(new ExtensionRemoveResult(
                Success: true,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local
            ));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to remove extension {Name}", command.Name);
            return Task.FromResult(new ExtensionRemoveResult(
                Success: false,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local,
                ErrorMessage: ex.Message
            ));
        }
    }

    public static Task<ExtensionWhichResult> HandleWhich(
        ExtensionWhich command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Finding active extension {Name} for platform {Platform}", command.Name, platform);

        string? localVersion = null;
        string? globalVersion = null;

        if (resolver.HasLocalRoot)
        {
            localVersion = resolver.GetActiveVersion(ArtifactType.Extension, name: command.Name, useLocal: true);
        }

        globalVersion = resolver.GetActiveVersion(ArtifactType.Extension, name: command.Name, useLocal: false);

        var activeVersion = localVersion ?? globalVersion;
        var isLocal = localVersion != null;

        if (activeVersion == null)
        {
            return Task.FromResult(new ExtensionWhichResult(
                Found: false,
                Name: command.Name,
                Version: null,
                Platform: platform,
                Path: null,
                IsLocal: false,
                ErrorMessage: $"No active version set for extension {command.Name}"
            ));
        }

        var activePath = resolver.GetArtifactPath(ArtifactType.Extension, platform, activeVersion, command.Name, isLocal);

        return Task.FromResult(new ExtensionWhichResult(
            Found: true,
            Name: command.Name,
            Version: activeVersion,
            Platform: platform,
            Path: activePath,
            IsLocal: isLocal
        ));
    }
}
