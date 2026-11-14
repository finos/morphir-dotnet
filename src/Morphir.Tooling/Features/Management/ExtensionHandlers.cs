using System.Text.Json;
using Microsoft.Extensions.Logging;
using Morphir.Extensions.Installation;
using Morphir.Mep;
using Morphir.Toolchain;

namespace Morphir.Tooling.Features.Management;

/// <summary>
/// Handlers for extension management commands.
/// Extensions follow the same pattern as tools.
/// </summary>
public static class ExtensionHandlers
{
    /// <summary>Registers the stable name using the existing repositories document and cooperative locks.</summary>
    public static async Task<ExtensionLocalOutcome> RegisterLocalAsync(ExtensionRepositoryAddLocal command, CancellationToken ct)
    {
        if (!command.LocalDeveloper) return new(false, "extension.consent_required", "Explicit --local-developer consent is required.");
        var outcome = await InstalledState.RegisterLocalRepositoryAsync(command.Home, command.Name, command.Directory, command.DryRun, ct);
        return outcome.Match(path => new ExtensionLocalOutcome(true, "extension.repository_registered",
            $"{(command.DryRun ? "Planned" : "Registered")} local-developer repository {command.Name}: {path}"),
            failure => new ExtensionLocalOutcome(false, failure.Code, failure.Message));
    }

    /// <summary>Validates and installs a local declaration through the one package installer. Dry run performs no effects.</summary>
    public static async Task<ExtensionLocalOutcome> InstallLocalAsync(ExtensionLocalDeclarationInstall command, CancellationToken ct)
    {
        if (!command.LocalDeveloper) return new(false, "extension.consent_required", "Explicit --local-developer consent is required.");
        if (!command.NoProbe) return new(false, "extension.probe_required", "This local declaration mode requires explicit --no-probe; first-session agreement remains mandatory.");
        if (!Morphir.Extensions.ExtensionId.TryParse(command.Name, out var id, out _))
            return new(false, "extension.invalid_id", "The name must be a full extension identity.");
        var request = ExtensionSourceRequest.Local(command.Home, command.Repository, command.Release, command.Archive, id);
        return await InstallAsync(new LocalRepositoryProvider(), request, command.Home, command.DryRun, "local-developer ", ct);
    }

    /// <summary>
    /// Installs from a provider source, such as <c>github:owner/repo@1.2.3</c> or <c>nuget:Acme.Morphir.Python@1.2.3</c>. Naming the release is the user's
    /// explicit request, so no <c>--local-developer</c> consent applies; <c>--no-probe</c> keeps its meaning.
    /// Network access happens here only, never when an installed extension runs.
    /// </summary>
    public static async Task<ExtensionLocalOutcome> InstallFromSourceAsync(ExtensionSourceInstall command, HttpClient http,
        CancellationToken ct, IExtensionSourceProvider? provider = null)
    {
        if (!command.NoProbe) return new(false, "extension.probe_required", "Installation requires explicit --no-probe; first-session agreement remains mandatory.");
        if (!ExtensionSourceRequest.TryParse(command.Source, out var parsed, out var failure))
            return new(false, failure.Code, failure.Message);
        ExtensionSourceRequest? request = null;
        InstallationFailure? refused = null;
        parsed.WithNuGetOptions(command.NuGetSource, command.ConfigFile, command.Interactive).Match(
            value => { request = value; return 0; },
            error => { refused = error; return 0; });
        if (request is null)
            return new(false, refused!.Code, refused.Message);
        if (!Path.IsPathFullyQualified(command.Home)) return new(false, "extension.invalid_home", "--home must be absolute.");
        return await InstallAsync(provider ?? ExtensionSources.ProviderFor(request, http, command.Home), request, command.Home, command.DryRun, "", ct);
    }

    private static async Task<ExtensionLocalOutcome> InstallAsync(IExtensionSourceProvider provider, ExtensionSourceRequest request,
        string home, bool dryRun, string mode, CancellationToken ct)
    {
        try
        {
            if (!Path.IsPathFullyQualified(home)) return new(false, "extension.invalid_home", "--home must be absolute.");
            if (dryRun)
            {
                var resolved = await ExtensionSources.ResolveAsync(provider, request, ct);
                return resolved.Match(release => new ExtensionLocalOutcome(true, "extension.planned",
                        $"Planned {mode}installation of {release.ExtensionId.Value} {release.Version.Text} from {request}; claims remain unchecked. Full package verification is required during apply."),
                    failure => new ExtensionLocalOutcome(false, failure.Code, failure.Message));
            }
            var host = SemVerModule.tryParse(Infrastructure.MepExtensionCommand.HostVersion).Match(value => value,
                _ => throw new IOException("Host version is not valid SemVer."));
            var installed = await ExtensionSources.InstallAsync(home, provider, request, Array.AsReadOnly(Array.Empty<string>()), host, ct);
            return installed.Match(captured =>
            {
                using var lifetime = (IDisposable)captured;
                return new ExtensionLocalOutcome(true, "extension.installed",
                    $"Installed {mode}{captured.Record.ExtensionId.Value} {captured.Record.Version.Text} from {request}; claims unchecked; no publisher authentication or readiness claimed.");
            }, failure => new ExtensionLocalOutcome(false, failure.Code, failure.Message));
        }
        catch (InstallationException error) { return new(false, error.Data0.Code, error.Data0.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { return new(false, "extension.local_refused", error.Message); }
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
            var manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.Manifest);
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
