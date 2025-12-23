using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Morphir.Tooling.Features.Management;

/// <summary>
/// Handlers for tool management commands.
/// </summary>
public static class ToolHandlers
{
    public static Task<ToolListResult> HandleList(
        ToolList command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Listing tools for platform {Platform}, local={Local}", platform, command.Local);

        var toolRoot = resolver.GetArtifactRoot(ArtifactType.Tool, command.Local);
        var platformRoot = Path.Combine(toolRoot, platform);

        var tools = new List<ToolInfo>();

        if (Directory.Exists(platformRoot))
        {
            foreach (var toolDir in Directory.GetDirectories(platformRoot))
            {
                var toolName = Path.GetFileName(toolDir);
                var versions = Directory.GetDirectories(toolDir)
                    .Select(Path.GetFileName)
                    .Where(v => v != null)
                    .Cast<string>()
                    .ToList();

                var activeVersion = resolver.GetActiveVersion(ArtifactType.Tool, name: toolName, useLocal: command.Local);

                tools.Add(new ToolInfo(
                    Name: toolName,
                    InstalledVersions: versions,
                    ActiveVersion: activeVersion,
                    Platform: platform
                ));
            }
        }

        return Task.FromResult(new ToolListResult(
            Tools: tools.OrderBy(t => t.Name).ToList(),
            Platform: platform,
            IsLocal: command.Local
        ));
    }

    public static async Task<ToolInstallResult> HandleInstall(
        ToolInstall command,
        ArtifactPathResolver resolver,
        HttpClient httpClient,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Installing tool {Name} version {Version} for platform {Platform} from {Url}",
            command.Name, command.Version, platform, command.SourceUrl);

        try
        {
            var installPath = resolver.GetArtifactPath(ArtifactType.Tool, platform, command.Version, command.Name, command.Local);
            var binPath = resolver.GetBinPath(ArtifactType.Tool, platform, command.Version, command.Name, command.Local);

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

            var manifestPath = resolver.GetManifestPath(ArtifactType.Tool, platform, command.Version, command.Name, command.Local);
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

            return new ToolInstallResult(
                Success: true,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                InstalledPath: installPath
            );
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to install tool {Name}", command.Name);
            return new ToolInstallResult(
                Success: false,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                InstalledPath: string.Empty,
                ErrorMessage: ex.Message
            );
        }
    }

    public static Task<ToolUseResult> HandleUse(
        ToolUse command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Setting active tool {Name} to version {Version} for platform {Platform}, local={Local}",
            command.Name, command.Version, platform, command.Local);

        try
        {
            var toolPath = resolver.GetArtifactPath(ArtifactType.Tool, platform, command.Version, command.Name, command.Local);
            if (!Directory.Exists(toolPath))
            {
                return Task.FromResult(new ToolUseResult(
                    Success: false,
                    Name: command.Name,
                    Version: command.Version,
                    Platform: platform,
                    IsLocal: command.Local,
                    ErrorMessage: $"Tool {command.Name} version {command.Version} for platform {platform} is not installed"
                ));
            }

            resolver.SetActiveVersion(ArtifactType.Tool, command.Version, name: command.Name, useLocal: command.Local);

            return Task.FromResult(new ToolUseResult(
                Success: true,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local
            ));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to set active tool {Name}", command.Name);
            return Task.FromResult(new ToolUseResult(
                Success: false,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local,
                ErrorMessage: ex.Message
            ));
        }
    }

    public static Task<ToolRemoveResult> HandleRemove(
        ToolRemove command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Removing tool {Name} version {Version} for platform {Platform}, local={Local}",
            command.Name, command.Version, platform, command.Local);

        try
        {
            var toolPath = resolver.GetArtifactPath(ArtifactType.Tool, platform, command.Version, command.Name, command.Local);
            
            if (!Directory.Exists(toolPath))
            {
                return Task.FromResult(new ToolRemoveResult(
                    Success: false,
                    Name: command.Name,
                    Version: command.Version,
                    Platform: platform,
                    IsLocal: command.Local,
                    ErrorMessage: $"Tool {command.Name} version {command.Version} is not installed"
                ));
            }

            var activeVersion = resolver.GetActiveVersion(ArtifactType.Tool, name: command.Name, useLocal: command.Local);
            if (activeVersion == command.Version)
            {
                logger?.LogWarning("Removing active tool {Name} version {Version}", command.Name, command.Version);
            }

            Directory.Delete(toolPath, recursive: true);

            return Task.FromResult(new ToolRemoveResult(
                Success: true,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local
            ));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to remove tool {Name}", command.Name);
            return Task.FromResult(new ToolRemoveResult(
                Success: false,
                Name: command.Name,
                Version: command.Version,
                Platform: platform,
                IsLocal: command.Local,
                ErrorMessage: ex.Message
            ));
        }
    }

    public static Task<ToolWhichResult> HandleWhich(
        ToolWhich command,
        ArtifactPathResolver resolver,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var platform = command.Platform ?? Infrastructure.RuntimeIdentifier.GetCurrentRid();
        logger?.LogInformation("Finding active tool {Name} for platform {Platform}", command.Name, platform);

        string? localVersion = null;
        string? globalVersion = null;

        if (resolver.HasLocalRoot)
        {
            localVersion = resolver.GetActiveVersion(ArtifactType.Tool, name: command.Name, useLocal: true);
        }

        globalVersion = resolver.GetActiveVersion(ArtifactType.Tool, name: command.Name, useLocal: false);

        var activeVersion = localVersion ?? globalVersion;
        var isLocal = localVersion != null;

        if (activeVersion == null)
        {
            return Task.FromResult(new ToolWhichResult(
                Found: false,
                Name: command.Name,
                Version: null,
                Platform: platform,
                Path: null,
                IsLocal: false,
                ErrorMessage: $"No active version set for tool {command.Name}"
            ));
        }

        var activePath = resolver.GetArtifactPath(ArtifactType.Tool, platform, activeVersion, command.Name, isLocal);

        return Task.FromResult(new ToolWhichResult(
            Found: true,
            Name: command.Name,
            Version: activeVersion,
            Platform: platform,
            Path: activePath,
            IsLocal: isLocal
        ));
    }
}
