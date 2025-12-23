using Morphir.Tooling.Configuration;

namespace Morphir.Tooling.Features.Management;

/// <summary>
/// Resolves paths for managed artifacts (dist, tools, extensions) with global-first and local override support.
/// </summary>
public sealed class ArtifactPathResolver
{
    private readonly string _globalRoot;
    private readonly string? _localRoot;

    /// <summary>
    /// Creates a new path resolver.
    /// </summary>
    /// <param name="workspaceRoot">Optional workspace root directory (for local artifacts)</param>
    public ArtifactPathResolver(string? workspaceRoot = null)
        : this(OsPaths.GetGlobalConfigDirectory(), workspaceRoot)
    {
    }

    /// <summary>
    /// Creates a new path resolver with custom global root.
    /// This constructor is primarily for testing to avoid using the actual system global directory.
    /// </summary>
    /// <param name="globalRoot">Custom global root directory</param>
    /// <param name="workspaceRoot">Optional workspace root directory (for local artifacts)</param>
    public ArtifactPathResolver(string globalRoot, string? workspaceRoot)
    {
        _globalRoot = globalRoot;
        _localRoot = workspaceRoot != null ? Path.Combine(workspaceRoot, ".morphir") : null;
    }

    /// <summary>
    /// Gets the root directory for an artifact type.
    /// </summary>
    /// <param name="type">Type of artifact</param>
    /// <param name="useLocal">If true and local root exists, returns local path; otherwise global</param>
    /// <returns>Root directory path for the artifact type</returns>
    public string GetArtifactRoot(ArtifactType type, bool useLocal = false)
    {
        var rootBase = (useLocal && _localRoot != null) ? _localRoot : _globalRoot;
        var typeName = type.ToString().ToLowerInvariant();
        return Path.Combine(rootBase, typeName);
    }

    /// <summary>
    /// Gets the directory path for a specific artifact installation.
    /// </summary>
    /// <param name="type">Type of artifact</param>
    /// <param name="platform">Platform RID (e.g., "linux-x64")</param>
    /// <param name="name">Name of artifact (for tools/extensions, omit for dist)</param>
    /// <param name="version">Version string</param>
    /// <param name="useLocal">If true and local root exists, returns local path; otherwise global</param>
    /// <returns>Directory path for the artifact installation</returns>
    public string GetArtifactPath(
        ArtifactType type,
        string platform,
        string version,
        string? name = null,
        bool useLocal = false)
    {
        var root = GetArtifactRoot(type, useLocal);

        return type switch
        {
            ArtifactType.Dist => Path.Combine(root, platform, version),
            ArtifactType.Tool or ArtifactType.Extension when name != null =>
                Path.Combine(root, platform, name, version),
            _ => throw new ArgumentException("Name is required for Tool and Extension types")
        };
    }

    /// <summary>
    /// Gets the manifest file path for an artifact.
    /// </summary>
    /// <param name="type">Type of artifact</param>
    /// <param name="platform">Platform RID</param>
    /// <param name="name">Name of artifact (for tools/extensions, omit for dist)</param>
    /// <param name="version">Version string</param>
    /// <param name="useLocal">If true and local root exists, returns local path; otherwise global</param>
    /// <returns>Full path to manifest.json</returns>
    public string GetManifestPath(
        ArtifactType type,
        string platform,
        string version,
        string? name = null,
        bool useLocal = false)
    {
        var artifactPath = GetArtifactPath(type, platform, version, name, useLocal);
        return Path.Combine(artifactPath, "manifest.json");
    }

    /// <summary>
    /// Gets the bin directory path for an artifact.
    /// </summary>
    /// <param name="type">Type of artifact</param>
    /// <param name="platform">Platform RID</param>
    /// <param name="name">Name of artifact (for tools/extensions, omit for dist)</param>
    /// <param name="version">Version string</param>
    /// <param name="useLocal">If true and local root exists, returns local path; otherwise global</param>
    /// <returns>Full path to bin directory</returns>
    public string GetBinPath(
        ArtifactType type,
        string platform,
        string version,
        string? name = null,
        bool useLocal = false)
    {
        var artifactPath = GetArtifactPath(type, platform, version, name, useLocal);
        return Path.Combine(artifactPath, "bin");
    }

    /// <summary>
    /// Resolves the active artifact by checking local then global selections.
    /// </summary>
    /// <param name="type">Type of artifact</param>
    /// <param name="platform">Platform RID</param>
    /// <param name="name">Name of artifact (for tools/extensions, omit for dist)</param>
    /// <returns>Path to active artifact, or null if none selected</returns>
    public string? ResolveActiveArtifact(ArtifactType type, string platform, string? name = null)
    {
        // Check local selection first (if local root exists)
        if (_localRoot != null)
        {
            var localSelection = GetActiveSelectionPath(type, name, useLocal: true);
            if (File.Exists(localSelection))
            {
                var localVersion = File.ReadAllText(localSelection).Trim();
                var localPath = GetArtifactPath(type, platform, localVersion, name, useLocal: true);
                if (Directory.Exists(localPath))
                    return localPath;
            }
        }

        // Fall back to global selection
        var globalSelection = GetActiveSelectionPath(type, name, useLocal: false);
        if (File.Exists(globalSelection))
        {
            var globalVersion = File.ReadAllText(globalSelection).Trim();
            var globalPath = GetArtifactPath(type, platform, globalVersion, name, useLocal: false);
            if (Directory.Exists(globalPath))
                return globalPath;
        }

        return null;
    }

    /// <summary>
    /// Gets the path to the active selection file.
    /// </summary>
    private string GetActiveSelectionPath(ArtifactType type, string? name, bool useLocal)
    {
        var root = GetArtifactRoot(type, useLocal);
        var fileName = type == ArtifactType.Dist
            ? "active"
            : $"active-{name}";
        return Path.Combine(root, fileName);
    }

    /// <summary>
    /// Sets the active version for an artifact.
    /// </summary>
    /// <param name="type">Type of artifact</param>
    /// <param name="version">Version to set as active</param>
    /// <param name="name">Name of artifact (for tools/extensions, omit for dist)</param>
    /// <param name="useLocal">If true, sets in local scope; otherwise global</param>
    public void SetActiveVersion(ArtifactType type, string version, string? name = null, bool useLocal = false)
    {
        var root = GetArtifactRoot(type, useLocal);
        Directory.CreateDirectory(root);

        var selectionPath = GetActiveSelectionPath(type, name, useLocal);
        File.WriteAllText(selectionPath, version);
    }

    /// <summary>
    /// Gets the active version for an artifact.
    /// </summary>
    /// <param name="type">Type of artifact</param>
    /// <param name="name">Name of artifact (for tools/extensions, omit for dist)</param>
    /// <param name="useLocal">If true, checks local scope; otherwise global</param>
    /// <returns>Active version string, or null if none set</returns>
    public string? GetActiveVersion(ArtifactType type, string? name = null, bool useLocal = false)
    {
        var selectionPath = GetActiveSelectionPath(type, name, useLocal);
        return File.Exists(selectionPath) ? File.ReadAllText(selectionPath).Trim() : null;
    }

    /// <summary>
    /// Gets whether local root is available.
    /// </summary>
    public bool HasLocalRoot => _localRoot != null;

    /// <summary>
    /// Gets the global root directory.
    /// </summary>
    public string GlobalRoot => _globalRoot;

    /// <summary>
    /// Gets the local root directory (null if not in a workspace).
    /// </summary>
    public string? LocalRoot => _localRoot;
}
