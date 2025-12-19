using System;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// Build targets for publishing packages to NuGet.org and local feeds
/// </summary>
partial class Build
{
    // NuGet Publishing targets

    /// <summary>
    /// Publish library NuGet packages to NuGet.org
    /// Publishes: Morphir.Core, Morphir.Tooling, Morphir
    /// Requires: --api-key parameter
    /// Parameters: --nuget-source (default: https://api.nuget.org/v3/index.json)
    /// </summary>
    Target PublishLibs => _ => _
        .DependsOn(PackLibs)
        .Description("Publish library NuGet packages to NuGet.org")
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                throw new Exception("API_KEY is required for publishing. Use --api-key parameter.");
            }

            var corePackage = OutputDir.GlobFiles("Morphir.Core.*.nupkg").FirstOrDefault();
            if (corePackage != null)
            {
                Serilog.Log.Information($"Publishing Morphir.Core: {corePackage}");
                DotNetNuGetPush(s => s
                    .SetTargetPath(corePackage)
                    .SetSource(NuGetSource)
                    .SetApiKey(ApiKey)
                    .SetSkipDuplicate(true));
            }
            else
            {
                throw new Exception($"Morphir.Core package not found in {OutputDir}");
            }

            var toolingPackage = OutputDir.GlobFiles("Morphir.Tooling.*.nupkg").FirstOrDefault();
            if (toolingPackage != null)
            {
                Serilog.Log.Information($"Publishing Morphir.Tooling: {toolingPackage}");
                DotNetNuGetPush(s => s
                    .SetTargetPath(toolingPackage)
                    .SetSource(NuGetSource)
                    .SetApiKey(ApiKey)
                    .SetSkipDuplicate(true));
            }
            else
            {
                throw new Exception($"Morphir.Tooling package not found in {OutputDir}");
            }

            // Publish Morphir executable package (not the Tool package)
            var morphirPackage = OutputDir.GlobFiles("Morphir.0.*.nupkg")
                .Where(p => !p.ToString().Contains("Morphir.Core") && 
                           !p.ToString().Contains("Morphir.Tooling") && 
                           !p.ToString().Contains("Morphir.Tool"))
                .FirstOrDefault();
            if (morphirPackage != null)
            {
                Serilog.Log.Information($"Publishing Morphir: {morphirPackage}");
                DotNetNuGetPush(s => s
                    .SetTargetPath(morphirPackage)
                    .SetSource(NuGetSource)
                    .SetApiKey(ApiKey)
                    .SetSkipDuplicate(true));
            }
            else
            {
                throw new Exception($"Morphir package not found in {OutputDir}");
            }
        });

    /// <summary>
    /// Publish the Morphir CLI tool package to NuGet.org
    /// Publishes: Morphir.Tool (dotnet tool package)
    /// Requires: --api-key parameter
    /// Parameters: --nuget-source (default: https://api.nuget.org/v3/index.json)
    /// </summary>
    Target PublishTool => _ => _
        .DependsOn(PackTool)
        .Description("Publish the Morphir CLI tool package to NuGet.org")
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                throw new Exception("API_KEY is required for publishing. Use --api-key parameter.");
            }

            var toolPackage = OutputDir.GlobFiles("Morphir.Tool.*.nupkg")
                .FirstOrDefault();

            if (toolPackage != null)
            {
                Serilog.Log.Information($"Publishing Morphir.Tool CLI tool: {toolPackage}");
                DotNetNuGetPush(s => s
                    .SetTargetPath(toolPackage)
                    .SetSource(NuGetSource)
                    .SetApiKey(ApiKey)
                    .SetSkipDuplicate(true));
            }
            else
            {
                throw new Exception($"Morphir.Tool package not found in {OutputDir}");
            }
        });

    /// <summary>
    /// Publish all packages to NuGet.org (libraries and tool)
    /// Equivalent to running PublishLibs and PublishTool
    /// Requires: --api-key parameter
    /// </summary>
    Target PublishAll => _ => _
        .DependsOn(PublishLibs, PublishTool)
        .Description("Publish all packages (libraries and tool)")
        .Executes(() =>
        {
            Serilog.Log.Information("All packages published successfully");
        });

    // Local Publishing targets

    /// <summary>
    /// Publish library NuGet packages to local feed for testing
    /// Publishes: Morphir.Core, Morphir.Tooling, Morphir
    /// Output: artifacts/local-feed/
    /// Parameters: --local-source (default: artifacts/local-feed)
    /// </summary>
    Target PublishLocalLibs => _ => _
        .DependsOn(PackLibs)
        .Description("Publish library packages to a local NuGet source")
        .Executes(() =>
        {
            LocalSource.CreateOrCleanDirectory();

            // Add local source if it doesn't exist
            try
            {
                DotNet($"nuget list source | grep {LocalSource}");
            }
            catch
            {
                Serilog.Log.Information($"Adding local NuGet source: {LocalSource}");
                DotNet($"nuget add source {LocalSource} --name local-feed");
            }

            var corePackage = OutputDir.GlobFiles("Morphir.Core.*.nupkg").FirstOrDefault();
            if (corePackage != null)
            {
                Serilog.Log.Information("Publishing Morphir.Core to local feed...");
                DotNetNuGetPush(s => s
                    .SetTargetPath(corePackage)
                    .SetSource(LocalSource)
                    .SetSkipDuplicate(true));
            }

            var toolingPackage = OutputDir.GlobFiles("Morphir.Tooling.*.nupkg").FirstOrDefault();
            if (toolingPackage != null)
            {
                Serilog.Log.Information("Publishing Morphir.Tooling to local feed...");
                DotNetNuGetPush(s => s
                    .SetTargetPath(toolingPackage)
                    .SetSource(LocalSource)
                    .SetSkipDuplicate(true));
            }

            var morphirPackage = OutputDir.GlobFiles("Morphir.0.*.nupkg")
                .Where(p => !p.ToString().Contains("Morphir.Core") && 
                           !p.ToString().Contains("Morphir.Tooling") && 
                           !p.ToString().Contains("Morphir.Tool"))
                .FirstOrDefault();
            if (morphirPackage != null)
            {
                Serilog.Log.Information("Publishing Morphir to local feed...");
                DotNetNuGetPush(s => s
                    .SetTargetPath(morphirPackage)
                    .SetSource(LocalSource)
                    .SetSkipDuplicate(true));
            }

            Serilog.Log.Information($"Libraries published to local feed: {LocalSource}");
        });

    /// <summary>
    /// Install Morphir CLI tool locally from the local feed for testing
    /// Installs the dotnet tool globally or locally based on --global parameter
    /// Parameters: --global (default: false), --local-source (default: artifacts/local-feed)
    /// Usage after install: dotnet-morphir [command]
    /// </summary>
    Target PublishLocalTool => _ => _
        .DependsOn(PackTool)
        .Description("Install the Morphir CLI tool locally from the package")
        .Executes(() =>
        {
            var toolPackage = OutputDir.GlobFiles("Morphir.Tool.*.nupkg")
                .FirstOrDefault();

            if (toolPackage == null)
            {
                throw new Exception($"Morphir.Tool package not found in {OutputDir}. Please run PackTool first.");
            }

            var installCommand = Global
                ? "dotnet tool install --global --add-source"
                : "dotnet tool install --add-source";

            var updateCommand = Global
                ? "dotnet tool update --global --add-source"
                : "dotnet tool update --add-source";

            try
            {
                Serilog.Log.Information($"Installing Morphir CLI tool {(Global ? "globally" : "locally")} from: {toolPackage}");
                DotNet($"tool install {(Global ? "--global" : "")} --add-source {OutputDir} Morphir.Tool");
            }
            catch
            {
                Serilog.Log.Information("Tool already installed, updating...");
                DotNet($"tool update {(Global ? "--global" : "")} --add-source {OutputDir} Morphir.Tool");
            }

            Serilog.Log.Information("Morphir CLI tool installed successfully");
        });

    /// <summary>
    /// Publish all packages to local feed and install tool locally for testing
    /// Equivalent to running PublishLocalLibs and PublishLocalTool
    /// Output: artifacts/local-feed/
    /// </summary>
    Target PublishLocalAll => _ => _
        .DependsOn(PublishLocalLibs, PublishLocalTool)
        .Description("Publish all packages locally (libraries to local feed, tool installed locally)")
        .Executes(() =>
        {
            Serilog.Log.Information("All packages published locally successfully");
        });

    // Release Preparation

    [Parameter("Version string for the new release (e.g., 0.3.0 or 0.3.0-beta.1)")]
    readonly string? ReleaseVersion;

    /// <summary>
    /// Prepare a new release by moving [Unreleased] content to a versioned section in CHANGELOG.md
    /// Validates that [Unreleased] has content before preparing the release
    /// Stages changes with git add but does not commit
    /// Parameters: --release-version (required, must be valid SemVer)
    /// </summary>
    Target PrepareRelease => _ => _
        .Description("Prepare a new release by updating CHANGELOG.md")
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(ReleaseVersion))
            {
                throw new Exception("Release version is required. Use --release-version parameter (e.g., --release-version 0.3.0)");
            }

            Serilog.Log.Information($"Preparing release: {ReleaseVersion}");
            
            var changelogFile = new FileInfo(ChangelogFile);
            
            // Validate [Unreleased] has content
            if (!changelogFile.HasUnreleasedContent())
            {
                throw new Exception("[Unreleased] section is empty. Add changes to CHANGELOG.md before preparing a release.");
            }
            
            // Prepare the release (updates CHANGELOG.md)
            changelogFile.PrepareRelease(ReleaseVersion);
            
            Serilog.Log.Information("✓ CHANGELOG.md updated successfully");
            Serilog.Log.Information($"  - Created new [{ReleaseVersion}] section");
            Serilog.Log.Information($"  - Reset [Unreleased] section");
            Serilog.Log.Information($"  - Updated comparison links");
            
            // Stage the changes
            RunCommand("git", "add", ChangelogFile);
            Serilog.Log.Information($"✓ Staged CHANGELOG.md");
            
            // Display next steps
            Serilog.Log.Information("");
            Serilog.Log.Information("Next steps:");
            Serilog.Log.Information("  1. Review changes: git diff --staged");
            Serilog.Log.Information($"  2. Commit: git commit -m \"chore: prepare release {ReleaseVersion}\"");
            Serilog.Log.Information($"  3. Push: git push origin release/{ReleaseVersion}");
            Serilog.Log.Information($"  4. Create PR: gh pr create --title \"chore: prepare release {ReleaseVersion}\"");
            Serilog.Log.Information($"  5. After merge, tag: git tag -a v{ReleaseVersion} -m \"Release {ReleaseVersion}\"");
            Serilog.Log.Information($"  6. Push tag: git push origin v{ReleaseVersion}");
        });

    /// <summary>
    /// Create a GitHub release with platform-specific executable tarballs
    /// Requires: Platform tarballs created by PackPlatformTarballs target
    /// Input: artifacts/morphir-{rid}-v{version}.tar.gz
    /// Output: GitHub release at https://github.com/finos/morphir-dotnet/releases/tag/v{version}
    /// Parameters: --version (required)
    /// </summary>
    Target CreateGitHubRelease => _ => _
        .DependsOn(PackPlatformTarballs)
        .Description("Create a GitHub release with platform-specific executables")
        .Executes(() =>
        {
            var versionString = Version.ToString();
            var releaseTag = $"v{versionString}";
            var artifactsDir = RootDirectory / "artifacts";

            Serilog.Log.Information($"Creating GitHub release for {releaseTag}...");

            // Find all platform tarballs
            var tarballs = Directory.GetFiles(artifactsDir, $"morphir-*-v{versionString}.tar.gz")
                .ToList();

            if (!tarballs.Any())
            {
                throw new Exception($"No platform tarballs found in {artifactsDir}. Run PackPlatformTarballs first.");
            }

            Serilog.Log.Information($"Found {tarballs.Count} platform tarballs");

            // Create release notes file
            var releaseNotesPath = artifactsDir / "release-notes.md";
            var releaseNotes = $@"# Morphir .NET v{versionString}

Platform-specific executables for the Morphir CLI.

## Installation

### Using proto (recommended)

```bash
# Install proto plugin
proto plugin add morphir ""source:https://github.com/finos/morphir-dotnet/releases/download/plugin-v0.1.0/morphir_plugin.wasm""

# Install Morphir
proto install morphir {versionString}
```

### Manual installation

Download the appropriate tarball for your platform and extract it:

```bash
# Linux x64
wget https://github.com/finos/morphir-dotnet/releases/download/v{versionString}/morphir-linux-x64-v{versionString}.tar.gz
tar -xzf morphir-linux-x64-v{versionString}.tar.gz

# macOS ARM64 (Apple Silicon)
wget https://github.com/finos/morphir-dotnet/releases/download/v{versionString}/morphir-osx-arm64-v{versionString}.tar.gz
tar -xzf morphir-osx-arm64-v{versionString}.tar.gz

# Windows x64
wget https://github.com/finos/morphir-dotnet/releases/download/v{versionString}/morphir-win-x64-v{versionString}.tar.gz
tar -xzf morphir-win-x64-v{versionString}.tar.gz
```

## Supported Platforms

- Linux (x64, arm64)
- macOS (x64, arm64/Apple Silicon)
- Windows (x64)

## NuGet Packages

This release is also available on NuGet:
- [Morphir](https://www.nuget.org/packages/Morphir/{versionString}) - CLI tool
- [Morphir.Core](https://www.nuget.org/packages/Morphir.Core/{versionString}) - Core library
- [dotnet-morphir](https://www.nuget.org/packages/dotnet-morphir/{versionString}) - .NET global tool

## Resources

- [Morphir Documentation](https://morphir.finos.org/)
- [GitHub Repository](https://github.com/finos/morphir-dotnet)
- [FINOS Morphir](https://github.com/finos/morphir)
";

            File.WriteAllText(releaseNotesPath, releaseNotes);
            Serilog.Log.Information($"✓ Created release notes: {releaseNotesPath}");

            // Build gh release create command with all tarballs
            var tarballArgs = string.Join(" ", tarballs.Select(t => $"\"{t}\""));
            var ghCommand = $"release create \"{releaseTag}\" " +
                           $"--title \"Morphir .NET v{versionString}\" " +
                           $"--notes-file \"{releaseNotesPath}\" " +
                           tarballArgs;

            Serilog.Log.Information("Creating GitHub release...");
            var exitCode = RunCommand("gh", ghCommand.Split(' '));

            if (exitCode != 0)
            {
                throw new Exception($"Failed to create GitHub release (exit code: {exitCode})");
            }

            Serilog.Log.Information($"✓ GitHub release created successfully");
            Serilog.Log.Information($"  View at: https://github.com/finos/morphir-dotnet/releases/tag/{releaseTag}");
        });
}
