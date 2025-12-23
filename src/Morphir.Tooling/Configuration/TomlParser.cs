using CsToml;
using Microsoft.FSharp.Core;
using Morphir.Configuration;
using System.Text;

namespace Morphir.Tooling.Configuration;

/// <summary>
/// Parses TOML configuration files into domain models.
/// </summary>
public static class TomlParser
{
    /// <summary>
    /// Parses a TOML file into a MorphirConfig.
    /// Returns null if the file doesn't exist or contains no morphir config.
    /// Throws on parse errors.
    /// </summary>
    public static MorphirConfig? ParseConfigFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var tomlBytes = File.ReadAllBytes(filePath);
        var document = CsTomlSerializer.Deserialize<Dictionary<string, object>>(tomlBytes);

        return ParseTomlDocument(document);
    }

    private static MorphirConfig? ParseTomlDocument(Dictionary<string, object> document)
    {
        // Parse cache configuration if present
        var cachePaths = ParseCachePaths(document);

        // Return config with parsed values
        return new MorphirConfig(cachePaths);
    }

    private static CachePaths ParseCachePaths(Dictionary<string, object> document)
    {
        FSharpOption<string> workspaceCache = FSharpOption<string>.None;
        FSharpOption<string> globalCache = FSharpOption<string>.None;

        // Look for cache configuration under [morphir.cache] or [cache]
        if (document.TryGetValue("morphir", out var morphirObj) &&
            morphirObj is Dictionary<string, object> morphirDict &&
            morphirDict.TryGetValue("cache", out var cacheObj) &&
            cacheObj is Dictionary<string, object> cacheDict)
        {
            if (cacheDict.TryGetValue("workspace", out var wsObj) && wsObj is string wsString)
            {
                workspaceCache = FSharpOption<string>.Some(wsString);
            }

            if (cacheDict.TryGetValue("global", out var gcObj) && gcObj is string gcString)
            {
                globalCache = FSharpOption<string>.Some(gcString);
            }
        }
        else if (document.TryGetValue("cache", out var directCacheObj) &&
                 directCacheObj is Dictionary<string, object> directCacheDict)
        {
            if (directCacheDict.TryGetValue("workspace", out var wsObj) && wsObj is string wsString)
            {
                workspaceCache = FSharpOption<string>.Some(wsString);
            }

            if (directCacheDict.TryGetValue("global", out var gcObj) && gcObj is string gcString)
            {
                globalCache = FSharpOption<string>.Some(gcString);
            }
        }

        return new CachePaths(workspaceCache, globalCache);
    }
}
