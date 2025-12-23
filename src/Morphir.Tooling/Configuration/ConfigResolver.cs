using Microsoft.Extensions.Logging;
using Microsoft.FSharp.Collections;
using Morphir.Configuration;

namespace Morphir.Tooling.Configuration;

/// <summary>
/// Resolves configuration by loading and merging layers from multiple sources.
/// </summary>
public class ConfigResolver
{
    private readonly ILogger<ConfigResolver> _logger;
    private readonly WorkspaceDiscovery _workspaceDiscovery;

    public ConfigResolver(
        ILogger<ConfigResolver> logger,
        WorkspaceDiscovery workspaceDiscovery)
    {
        _logger = logger;
        _workspaceDiscovery = workspaceDiscovery;
    }

    /// <summary>
    /// Resolves configuration by loading and merging layers.
    /// </summary>
    /// <param name="ciMode">CI profile activation mode (On, Off, Auto). If null, defaults to Auto.</param>
    /// <param name="startPath">Starting path for workspace discovery (defaults to current directory)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task<ConfigResolution> ResolveConfigAsync(
        CiProfileMode? ciMode = null,
        string? startPath = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveCiMode = ciMode ?? CiProfileMode.Auto;
        var layers = new List<ConfigLayer>();

        // 1. Load global config
        var globalConfigPath = Path.Combine(OsPaths.GetGlobalConfigDirectory(), "morphir.toml");
        _logger.LogDebug("Checking for global config at: {Path}", globalConfigPath);

        var globalConfig = TomlParser.ParseConfigFile(globalConfigPath);
        if (globalConfig != null)
        {
            _logger.LogInformation("Loaded global config from: {Path}", globalConfigPath);
            layers.Add(new ConfigLayer(globalConfigPath, globalConfig));
        }
        else
        {
            _logger.LogDebug("No global config found at: {Path}", globalConfigPath);
        }

        // 2. Discover workspace root
        var workspaceRoot = _workspaceDiscovery.DiscoverWorkspaceRoot(startPath);

        if (workspaceRoot != null)
        {
            _logger.LogInformation("Discovered workspace root at: {Root}", workspaceRoot);

            var morphirDir = Path.Combine(workspaceRoot, ".morphir");

            // 3. Load workspace config
            var workspaceConfigPath = Path.Combine(morphirDir, "morphir.toml");
            _logger.LogDebug("Checking for workspace config at: {Path}", workspaceConfigPath);

            var workspaceConfig = TomlParser.ParseConfigFile(workspaceConfigPath);
            if (workspaceConfig != null)
            {
                _logger.LogInformation("Loaded workspace config from: {Path}", workspaceConfigPath);
                layers.Add(new ConfigLayer(workspaceConfigPath, workspaceConfig));
            }
            else
            {
                _logger.LogDebug("No workspace config found at: {Path}", workspaceConfigPath);
            }

            // 4. Load user override config
            var userConfigPath = Path.Combine(morphirDir, "morphir.user.toml");
            _logger.LogDebug("Checking for user config at: {Path}", userConfigPath);

            var userConfig = TomlParser.ParseConfigFile(userConfigPath);
            if (userConfig != null)
            {
                _logger.LogInformation("Loaded user config from: {Path}", userConfigPath);
                layers.Add(new ConfigLayer(userConfigPath, userConfig));
            }
            else
            {
                _logger.LogDebug("No user config found at: {Path}", userConfigPath);
            }

            // 5. Determine if CI overlay should be applied
            var envVars = GetEnvironmentVariables();
            var shouldApplyCi = CiDetection.shouldApplyCiOverlay(effectiveCiMode, envVars);

            _logger.LogDebug("CI mode: {Mode}, Should apply CI overlay: {ShouldApply}", effectiveCiMode, shouldApplyCi);

            var ciProfileApplied = false;

            if (shouldApplyCi)
            {
                // 6. Load CI override config
                var ciConfigPath = Path.Combine(morphirDir, "morphir.ci.toml");
                _logger.LogDebug("Checking for CI config at: {Path}", ciConfigPath);

                var ciConfig = TomlParser.ParseConfigFile(ciConfigPath);
                if (ciConfig != null)
                {
                    _logger.LogInformation("Loaded CI config from: {Path}", ciConfigPath);
                    layers.Add(new ConfigLayer(ciConfigPath, ciConfig));
                    ciProfileApplied = true;
                }
                else
                {
                    _logger.LogDebug("No CI config found at: {Path}", ciConfigPath);
                }
            }

            // 7. Merge all layers
            var layersList = ListModule.OfSeq(layers);
            var effectiveConfig = Merge.mergeLayers(layersList);

            var resolution = new ConfigResolution(
                effectiveConfig,
                layersList,
                Microsoft.FSharp.Core.FSharpOption<string>.Some(workspaceRoot),
                ciProfileApplied);

            _logger.LogDebug("Config resolution complete. Layers: {LayerCount}, CI applied: {CiApplied}",
                layers.Count, ciProfileApplied);

            return Task.FromResult(resolution);
        }
        else
        {
            _logger.LogDebug("No workspace root discovered. Using global config only.");

            // No workspace - use global config only (or defaults)
            var layersList = ListModule.OfSeq(layers);
            var effectiveConfig = Merge.mergeLayers(layersList);

            var resolution = new ConfigResolution(
                effectiveConfig,
                layersList,
                Microsoft.FSharp.Core.FSharpOption<string>.None,
                false);

            return Task.FromResult(resolution);
        }
    }

    private static FSharpMap<string, string> GetEnvironmentVariables()
    {
        var envList = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(e => e.Key is string && e.Value is string)
            .Select(e => Tuple.Create((string)e.Key, (string)e.Value!));

        return MapModule.OfSeq(envList);
    }
}
