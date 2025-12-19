using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Morphir.Build.Helpers;

/// <summary>
/// Validates NuGet package structure and metadata
/// </summary>
public static class PackageValidator
{
    /// <summary>
    /// Validation result containing success status and any error messages
    /// </summary>
    public record ValidationResult(bool IsValid, List<string> Errors)
    {
        public static ValidationResult Success() => new(true, new List<string>());
        public static ValidationResult Failure(params string[] errors) => new(false, errors.ToList());
    }

    /// <summary>
    /// Validates a dotnet tool package structure
    /// Checks for required tool files and directory structure
    /// </summary>
    /// <param name="packagePath">Full path to the .nupkg file</param>
    /// <returns>ValidationResult with success status and any error messages</returns>
    /// <exception cref="FileNotFoundException">Thrown when package file does not exist</exception>
    public static ValidationResult ValidateToolPackage(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException($"Package file not found: {packagePath}", packagePath);
        }

        var errors = new List<string>();

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entries = archive.Entries.Select(e => e.FullName).ToList();

            // Check for tools/ directory structure
            if (!entries.Any(e => e.StartsWith("tools/")))
            {
                errors.Add("Tool package must contain 'tools/' directory");
            }

            // Check for DotnetToolSettings.xml
            if (!entries.Any(e => e.EndsWith("DotnetToolSettings.xml")))
            {
                errors.Add("Tool package must contain 'DotnetToolSettings.xml'");
            }

            // Check for main tool DLL
            var toolDlls = entries.Where(e => e.EndsWith(".dll") && e.Contains("tools/")).ToList();
            if (!toolDlls.Any())
            {
                errors.Add("Tool package must contain at least one .dll file in tools/ directory");
            }

            // Check that PDB files are not included (should be in symbols package)
            var pdbFiles = entries.Where(e => e.EndsWith(".pdb")).ToList();
            if (pdbFiles.Any())
            {
                errors.Add($"Tool package should not contain .pdb files (found: {string.Join(", ", pdbFiles)})");
            }

            // Validate DotnetToolSettings.xml if present
            var toolSettingsEntry = entries.FirstOrDefault(e => e.EndsWith("DotnetToolSettings.xml"));
            if (toolSettingsEntry != null)
            {
                var entry = archive.GetEntry(toolSettingsEntry);
                if (entry != null)
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    var xml = reader.ReadToEnd();
                    var settingsValidation = ValidateDotnetToolSettings(xml);
                    if (!settingsValidation.IsValid)
                    {
                        errors.AddRange(settingsValidation.Errors);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to read package: {ex.Message}");
        }

        return errors.Any() ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <summary>
    /// Validates a library package structure
    /// Checks for required lib files and directory structure
    /// </summary>
    /// <param name="packagePath">Full path to the .nupkg file</param>
    /// <returns>ValidationResult with success status and any error messages</returns>
    /// <exception cref="FileNotFoundException">Thrown when package file does not exist</exception>
    public static ValidationResult ValidateLibraryPackage(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException($"Package file not found: {packagePath}", packagePath);
        }

        var errors = new List<string>();

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entries = archive.Entries.Select(e => e.FullName).ToList();

            // Check for lib/ directory structure
            if (!entries.Any(e => e.StartsWith("lib/")))
            {
                errors.Add("Library package must contain 'lib/' directory");
            }

            // Check for at least one assembly
            var libDlls = entries.Where(e => e.EndsWith(".dll") && e.StartsWith("lib/")).ToList();
            if (!libDlls.Any())
            {
                errors.Add("Library package must contain at least one .dll file in lib/ directory");
            }

            // Library packages should NOT have tools/ directory
            if (entries.Any(e => e.StartsWith("tools/")))
            {
                errors.Add("Library package should not contain 'tools/' directory");
            }

            // Check that PDB files are not included (should be in symbols package)
            var pdbFiles = entries.Where(e => e.EndsWith(".pdb")).ToList();
            if (pdbFiles.Any())
            {
                errors.Add($"Library package should not contain .pdb files (found: {string.Join(", ", pdbFiles)})");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to read package: {ex.Message}");
        }

        return errors.Any() ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <summary>
    /// Validates the DotnetToolSettings.xml content
    /// Checks for required Command element with Name and EntryPoint attributes
    /// </summary>
    /// <param name="xml">XML content of DotnetToolSettings.xml</param>
    /// <returns>ValidationResult with success status and any error messages</returns>
    public static ValidationResult ValidateDotnetToolSettings(string xml)
    {
        var errors = new List<string>();

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;

            if (root == null || root.Name != "DotNetCliTool")
            {
                errors.Add("DotnetToolSettings.xml must have <DotNetCliTool> as root element");
                return ValidationResult.Failure(errors.ToArray());
            }

            var commands = root.Element("Commands");
            if (commands == null)
            {
                errors.Add("DotnetToolSettings.xml must contain <Commands> element");
                return ValidationResult.Failure(errors.ToArray());
            }

            var command = commands.Element("Command");
            if (command == null)
            {
                errors.Add("DotnetToolSettings.xml must contain <Command> element");
                return ValidationResult.Failure(errors.ToArray());
            }

            var nameAttr = command.Attribute("Name");
            if (nameAttr == null || string.IsNullOrWhiteSpace(nameAttr.Value))
            {
                errors.Add("Command element must have a non-empty 'Name' attribute");
            }

            var entryPointAttr = command.Attribute("EntryPoint");
            if (entryPointAttr == null || string.IsNullOrWhiteSpace(entryPointAttr.Value))
            {
                errors.Add("Command element must have a non-empty 'EntryPoint' attribute");
            }

            // Validate EntryPoint ends with .dll
            if (entryPointAttr != null && !entryPointAttr.Value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"EntryPoint must be a .dll file (found: {entryPointAttr.Value})");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to parse DotnetToolSettings.xml: {ex.Message}");
        }

        return errors.Any() ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <summary>
    /// Validates package metadata from .nuspec file
    /// Checks for required metadata fields
    /// </summary>
    /// <param name="nuspec">XML content of the .nuspec file</param>
    /// <returns>ValidationResult with success status and any error messages</returns>
    public static ValidationResult ValidatePackageMetadata(string nuspec)
    {
        var errors = new List<string>();

        try
        {
            var doc = XDocument.Parse(nuspec);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var metadata = doc.Root?.Element(ns + "metadata");

            if (metadata == null)
            {
                errors.Add(".nuspec file must contain <metadata> element");
                return ValidationResult.Failure(errors.ToArray());
            }

            // Check required fields
            var requiredFields = new[] { "id", "version", "authors", "description" };
            foreach (var field in requiredFields)
            {
                var element = metadata.Element(ns + field);
                if (element == null || string.IsNullOrWhiteSpace(element.Value))
                {
                    errors.Add($"Package metadata must contain non-empty '{field}' field");
                }
            }

            // Check license (should have either licenseUrl or license element)
            var licenseUrl = metadata.Element(ns + "licenseUrl");
            var license = metadata.Element(ns + "license");
            if ((licenseUrl == null || string.IsNullOrWhiteSpace(licenseUrl.Value)) &&
                (license == null || string.IsNullOrWhiteSpace(license.Value)))
            {
                errors.Add("Package metadata should contain either 'licenseUrl' or 'license' field");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to parse .nuspec file: {ex.Message}");
        }

        return errors.Any() ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }
}
