using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ionide.KeepAChangelog;
using SemVersion;
using Microsoft.FSharp.Collections;

/// <summary>
/// Helper class for working with CHANGELOG.md using Keep a Changelog format
/// </summary>
public static class ChangelogHelper
{
    /// <summary>
    /// Gets the latest release version from the changelog
    /// </summary>
    /// <param name="changelogFile">Path to CHANGELOG.md file</param>
    /// <returns>Latest release version as SemanticVersion</returns>
    /// <exception cref="Exception">Thrown when changelog cannot be parsed or has no releases</exception>
    public static SemanticVersion GetVersionFromChangelog(FileInfo changelogFile)
    {
        if (!changelogFile.Exists)
        {
            throw new FileNotFoundException($"CHANGELOG.md not found at {changelogFile.FullName}");
        }

        var result = Parser.parseChangeLog(changelogFile);
        var resultType = result.GetType();
        var isOk = (bool)resultType.GetProperty("IsOk")!.GetValue(result)!;

        if (!isOk)
        {
            var error = resultType.GetProperty("ErrorValue")!.GetValue(result);
            throw new Exception($"Failed to parse CHANGELOG.md: {error}");
        }

        var value = (dynamic)resultType.GetProperty("ResultValue")!.GetValue(result)!;
        var releases = value.Releases as System.Collections.IEnumerable;
        var releasesList = releases?.Cast<object>().ToList();

        if (releasesList == null || !releasesList.Any())
        {
            throw new Exception("No releases found in CHANGELOG.md");
        }

        // Get the first (latest) release
        var firstRelease = (dynamic)releasesList.First();
        var version = firstRelease.Item1 as SemanticVersion;

        if (version == null)
        {
            throw new Exception("Failed to extract version from latest release");
        }

        return version;
    }

    /// <summary>
    /// Gets the release notes for the latest release from the changelog
    /// </summary>
    /// <param name="changelogFile">Path to CHANGELOG.md file</param>
    /// <returns>Formatted markdown release notes</returns>
    public static string GetReleaseNotes(FileInfo changelogFile)
    {
        if (!changelogFile.Exists)
        {
            throw new FileNotFoundException($"CHANGELOG.md not found at {changelogFile.FullName}");
        }

        var result = Parser.parseChangeLog(changelogFile);
        var resultType = result.GetType();
        var isOk = (bool)resultType.GetProperty("IsOk")!.GetValue(result)!;

        if (!isOk)
        {
            var error = resultType.GetProperty("ErrorValue")!.GetValue(result);
            throw new Exception($"Failed to parse CHANGELOG.md: {error}");
        }

        var value = (dynamic)resultType.GetProperty("ResultValue")!.GetValue(result)!;
        var releases = value.Releases as System.Collections.IEnumerable;
        var releasesList = releases?.Cast<object>().ToList();

        if (releasesList == null || !releasesList.Any())
        {
            throw new Exception("No releases found in CHANGELOG.md");
        }

        // Get the first (latest) release
        var firstRelease = (dynamic)releasesList.First();
        object changelogDataOption = firstRelease.Item3;

        // F# Option: get_IsSome is a static method that takes the option instance
        var optionType = changelogDataOption.GetType();
        var isSomeMethod = optionType.GetMethod("get_IsSome");
        if (isSomeMethod == null)
        {
            throw new Exception("Failed to access get_IsSome method on FSharpOption");
        }
        
        var isSome = (bool)isSomeMethod.Invoke(null, new[] { changelogDataOption })!;
        if (!isSome)
        {
            return string.Empty;
        }

        // F# Option: get_Value is an instance method
        var valueMethod = optionType.GetMethod("get_Value");
        if (valueMethod == null)
        {
            throw new Exception("Failed to access get_Value method on FSharpOption");
        }
        
        dynamic data = valueMethod.Invoke(changelogDataOption, Array.Empty<object>())!;

        // Build formatted release notes
        var notes = new StringBuilder();

        void AppendSection(string title, string content)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                if (notes.Length > 0) notes.AppendLine();
                notes.AppendLine($"### {title}");
                notes.AppendLine(content.Trim());
            }
        }

        string added = data.Added;
        string changed = data.Changed;
        string deprecated = data.Deprecated;
        string removed = data.Removed;
        string fixedSection = data.Fixed;
        string security = data.Security;

        // Escape XML special characters for MSBuild
        string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        AppendSection("Added", EscapeXml(added));
        AppendSection("Changed", EscapeXml(changed));
        AppendSection("Deprecated", EscapeXml(deprecated));
        AppendSection("Removed", EscapeXml(removed));
        AppendSection("Fixed", EscapeXml(fixedSection));
        AppendSection("Security", EscapeXml(security));

        return notes.ToString();
    }

    /// <summary>
    /// Checks if the [Unreleased] section has content (bullet points)
    /// </summary>
    /// <param name="changelogFile">Path to CHANGELOG.md file</param>
    /// <returns>True if [Unreleased] has content, false otherwise</returns>
    public static bool HasUnreleasedContent(FileInfo changelogFile)
    {
        if (!changelogFile.Exists)
        {
            throw new FileNotFoundException($"CHANGELOG.md not found at {changelogFile.FullName}");
        }

        var content = File.ReadAllText(changelogFile.FullName);
        
        // Find the [Unreleased] section
        var unreleasedMatch = Regex.Match(content, @"##\s*\[Unreleased\](.*?)(?=##\s*\[|\z)", RegexOptions.Singleline);
        
        if (!unreleasedMatch.Success)
        {
            return false;
        }

        var unreleasedContent = unreleasedMatch.Groups[1].Value;
        
        // Check if there are any bullet points (lines starting with -)
        return Regex.IsMatch(unreleasedContent, @"^\s*-\s+", RegexOptions.Multiline);
    }

    /// <summary>
    /// Prepares a new release by moving [Unreleased] content to a new version section
    /// </summary>
    /// <param name="changelogFile">Path to CHANGELOG.md file</param>
    /// <param name="version">Version string for the new release</param>
    /// <exception cref="Exception">Thrown when validation fails or file cannot be written</exception>
    public static void PrepareRelease(FileInfo changelogFile, string version)
    {
        if (!changelogFile.Exists)
        {
            throw new FileNotFoundException($"CHANGELOG.md not found at {changelogFile.FullName}");
        }

        // Validate version is valid SemVer
        if (!SemanticVersion.TryParse(version, out _))
        {
            throw new ArgumentException($"Invalid semantic version: {version}", nameof(version));
        }

        var content = File.ReadAllText(changelogFile.FullName);
        
        // Extract [Unreleased] section content
        var unreleasedMatch = Regex.Match(content, @"##\s*\[Unreleased\]\s*\n(.*?)(?=##\s*\[|\z)", RegexOptions.Singleline);
        
        if (!unreleasedMatch.Success)
        {
            throw new Exception("Could not find [Unreleased] section in CHANGELOG.md");
        }

        var unreleasedContent = unreleasedMatch.Groups[1].Value.Trim();
        
        // Check if there's actual content (not just empty lines)
        if (string.IsNullOrWhiteSpace(unreleasedContent) || !Regex.IsMatch(unreleasedContent, @"^\s*-\s+", RegexOptions.Multiline))
        {
            throw new Exception("[Unreleased] section is empty. Add changes before preparing a release.");
        }

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var newReleaseSection = $"## [{version}] - {today}\n\n{unreleasedContent}";
        
        // Replace [Unreleased] section with empty one and add new release section
        var newContent = Regex.Replace(
            content,
            @"##\s*\[Unreleased\]\s*\n(.*?)(?=##\s*\[)",
            $"## [Unreleased]\n\n{newReleaseSection}\n\n",
            RegexOptions.Singleline
        );

        // Update comparison links at the bottom
        // Find the [Unreleased] link and update it
        var linkPattern = @"\[Unreleased\]:\s*https://github\.com/([^/]+)/([^/]+)/compare/v([^.]+\.[^.]+\.[^\s]+)\.\.\.HEAD";
        var linkMatch = Regex.Match(newContent, linkPattern);
        
        if (linkMatch.Success)
        {
            var owner = linkMatch.Groups[1].Value;
            var repo = linkMatch.Groups[2].Value;
            var previousVersion = linkMatch.Groups[3].Value;
            
            // Update [Unreleased] link to compare new version with HEAD
            var updatedUnreleasedLink = $"[Unreleased]: https://github.com/{owner}/{repo}/compare/v{version}...HEAD";
            
            // Add new version comparison link
            var newVersionLink = $"[{version}]: https://github.com/{owner}/{repo}/compare/v{previousVersion}...v{version}";
            
            newContent = Regex.Replace(
                newContent,
                linkPattern,
                $"{updatedUnreleasedLink}\n{newVersionLink}"
            );
        }

        // Write the updated content back to the file
        File.WriteAllText(changelogFile.FullName, newContent);
    }

    /// <summary>
    /// Gets the next pre-release version by incrementing the pre-release number
    /// </summary>
    /// <param name="changelogFile">Path to CHANGELOG.md file</param>
    /// <returns>Next pre-release version string</returns>
    public static string GetNextPreReleaseVersion(FileInfo changelogFile)
    {
        var currentVersion = GetVersionFromChangelog(changelogFile);
        
        if (string.IsNullOrEmpty(currentVersion.Prerelease))
        {
            throw new Exception($"Current version {currentVersion} is not a pre-release. Cannot auto-bump.");
        }

        // Parse pre-release identifier (e.g., "beta.2" -> type: "beta", number: 2)
        var match = Regex.Match(currentVersion.Prerelease, @"^([a-z]+)\.(\d+)$");
        
        if (!match.Success)
        {
            throw new Exception($"Pre-release version format not recognized: {currentVersion.Prerelease}. Expected format: type.number (e.g., beta.2)");
        }

        var prereleaseType = match.Groups[1].Value;
        var prereleaseNumber = int.Parse(match.Groups[2].Value);
        var nextNumber = prereleaseNumber + 1;

        return $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Patch}-{prereleaseType}.{nextNumber}";
    }
}
