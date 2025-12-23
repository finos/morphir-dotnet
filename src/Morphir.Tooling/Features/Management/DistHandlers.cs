using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Morphir.Tooling.Features.Management;

/// <summary>
/// Handlers for dist management commands.
/// All handlers are pure functions that take dependencies explicitly.
/// </summary>
public static class DistHandlers
{
    /// <summary>
    /// Lists installed distributions.
    /// </summary>
    public static Task<DistListResult> HandleList(
        DistList command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Listing distributions for platform {Platform}, local={Local}", platform, command.Local);

        var distRoot = resolver.GetArtifactRoot(ArtifactType.Dist, command.Local);
        var platformRoot = Path.Combine(distRoot, platform);

        var distributions = new List<DistInfo>();

        if (Directory.Exists(platformRoot))
        {
            var activeVersion = resolver.GetActiveVersion(ArtifactType.Dist, useLocal: command.Local);

            foreach (var versionDir in Directory.GetDirectories(platformRoot))
            {
                var version = Path.GetFileName(versionDir);
                var manifestPath = resolver.GetManifestPath(ArtifactType.Dist, platform, version, useLocal: command.Local);

                string? description = null;
                DateTime? installedAt = null;

                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var manifestJson = File.ReadAllText(manifestPath);
                        // Use System.Text.Json with comment handling
                        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
                        using var doc = JsonDocument.Parse(manifestJson, options);

                        if (doc.RootElement.TryGetProperty("description", out var descProp))
                            description = descProp.GetString();

                        if (doc.RootElement.TryGetProperty("installedAt", out var installedProp))
                        {
                            if (DateTime.TryParse(installedProp.GetString(), out var dt))
                                installedAt = dt;
                        }
                    }
                    catch (JsonException ex)
                    {
                        logger?.LogWarning(ex, "Failed to parse manifest for version {Version}", version);
                    }
                }

                distributions.Add(new DistInfo(
                    Version: version,
                    Platform: platform,
                    Description: description,
                    IsActive: version == activeVersion,
                    InstalledAt: installedAt
                ));
            }
        }

        return Task.FromResult(new DistListResult(
            Distributions: distributions.OrderBy(d => d.Version).ToList(),
            Platform: platform,
            IsLocal: command.Local
        ));
    }

    /// <summary>
    /// Installs a distribution from a URL.
    /// </summary>
    public static async Task<DistInstallResult> HandleInstall(
        DistInstall command,
        ArtifactPathResolver resolver,
        HttpClient httpClient,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Installing distribution version {Version} for platform {Platform} from {Url}",
            command.Version, platform, command.SourceUrl);

        try
        {
            // Get installation path
            var installPath = resolver.GetArtifactPath(ArtifactType.Dist, platform, command.Version, useLocal: command.Local);
            var binPath = resolver.GetBinPath(ArtifactType.Dist, platform, command.Version, useLocal: command.Local);

            // Create directories
            Directory.CreateDirectory(binPath);

            // Download artifact
            logger?.LogInformation("Downloading artifact from {Url}", command.SourceUrl);
            var response = await httpClient.GetAsync(command.SourceUrl, ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsByteArrayAsync(ct);

            // For now, just save the downloaded content as a single file
            // In a real implementation, this would extract a zip/tar.gz
            var downloadedFile = Path.Combine(binPath, "artifact");
            await File.WriteAllBytesAsync(downloadedFile, content, ct);

            logger?.LogInformation("Downloaded {Bytes} bytes to {Path}", content.Length, downloadedFile);

            // Create manifest
            var manifest = new Manifest
            {
                Name = "morphir-dist",
                Version = command.Version,
                Platform = platform,
                SourceUrl = command.SourceUrl,
                InstalledAt = DateTime.UtcNow
            };

            var manifestPath = resolver.GetManifestPath(ArtifactType.Dist, platform, command.Version, useLocal: command.Local);
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

            logger?.LogInformation("Installation complete at {Path}", installPath);

            return new DistInstallResult(
                Success: true,
                Version: command.Version,
                Platform: platform,
                InstalledPath: installPath
            );
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to install distribution");
            return new DistInstallResult(
                Success: false,
                Version: command.Version,
                Platform: platform,
                InstalledPath: string.Empty,
                ErrorMessage: ex.Message
            );
        }
    }

    /// <summary>
    /// Sets the active distribution version.
    /// </summary>
    public static Task<DistUseResult> HandleUse(
        DistUse command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Setting active distribution to version {Version} for platform {Platform}, local={Local}",
            command.Version, platform, command.Local);

        try
        {
            // Check if the distribution exists
            var distPath = resolver.GetArtifactPath(ArtifactType.Dist, platform, command.Version, useLocal: command.Local);
            if (!Directory.Exists(distPath))
            {
                return Task.FromResult(new DistUseResult(
                    Success: false,
                    Version: command.Version,
                    Platform: platform,
                    IsLocal: command.Local,
                    ErrorMessage: $"Distribution version {command.Version} for platform {platform} is not installed"
                ));
            }

            // Set as active
            resolver.SetActiveVersion(ArtifactType.Dist, command.Version, useLocal: command.Local);

            logger?.LogInformation("Set active distribution to {Version}", command.Version);

            return Task.FromResult(new DistUseResult(
                Success: true,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local
            ));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to set active distribution");
            return Task.FromResult(new DistUseResult(
                Success: false,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local,
                ErrorMessage: ex.Message
            ));
        }
    }

    /// <summary>
    /// Removes an installed distribution.
    /// </summary>
    public static Task<DistRemoveResult> HandleRemove(
        DistRemove command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Removing distribution version {Version} for platform {Platform}, local={Local}",
            command.Version, platform, command.Local);

        try
        {
            var distPath = resolver.GetArtifactPath(ArtifactType.Dist, platform, command.Version, useLocal: command.Local);

            if (!Directory.Exists(distPath))
            {
                return Task.FromResult(new DistRemoveResult(
                    Success: false,
                    Version: command.Version,
                    Platform: platform,
                    IsLocal: command.Local,
                    ErrorMessage: $"Distribution version {command.Version} is not installed"
                ));
            }

            // Check if this is the active version
            var activeVersion = resolver.GetActiveVersion(ArtifactType.Dist, useLocal: command.Local);
            if (activeVersion == command.Version)
            {
                logger?.LogWarning("Removing active distribution version {Version}", command.Version);
            }

            // Remove the directory
            Directory.Delete(distPath, recursive: true);

            logger?.LogInformation("Removed distribution version {Version}", command.Version);

            return Task.FromResult(new DistRemoveResult(
                Success: true,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local
            ));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to remove distribution");
            return Task.FromResult(new DistRemoveResult(
                Success: false,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local,
                ErrorMessage: ex.Message
            ));
        }
    }

    /// <summary>
    /// Shows which distribution is currently active.
    /// </summary>
    public static Task<DistWhichResult> HandleWhich(
        DistWhich command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Finding active distribution for platform {Platform}", platform);

        // Check local first, then global
        string? localVersion = null;
        string? globalVersion = null;

        if (resolver.HasLocalRoot)
        {
            localVersion = resolver.GetActiveVersion(ArtifactType.Dist, useLocal: true);
        }

        globalVersion = resolver.GetActiveVersion(ArtifactType.Dist, useLocal: false);

        // Prefer local over global
        var activeVersion = localVersion ?? globalVersion;
        var isLocal = localVersion != null;

        if (activeVersion == null)
        {
            return Task.FromResult(new DistWhichResult(
                Found: false,
                Version: null,
                Platform: platform,
                Path: null,
                IsLocal: false,
                ErrorMessage: "No active distribution set"
            ));
        }

        var activePath = resolver.GetArtifactPath(ArtifactType.Dist, platform, activeVersion, useLocal: isLocal);

        return Task.FromResult(new DistWhichResult(
            Found: true,
            Version: activeVersion,
            Platform: platform,
            Path: activePath,
            IsLocal: isLocal
        ));
    }
}
