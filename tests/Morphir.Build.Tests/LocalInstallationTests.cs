using System.Diagnostics;
using FluentAssertions;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// Tests for validating local NuGet package installation (requires dotnet CLI)
/// </summary>
public class LocalInstallationTests
{
    [Test]
    [Skip("Requires packages to be built first and is a potentially destructive test")]
    public async Task ToolPackage_InstallsFromLocalFolder()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"morphir-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var localSource = Path.Combine(tempDir, "feed");
            Directory.CreateDirectory(localSource);
            
            var toolPackage = TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg");
            toolPackage.Should().NotBeNull("Tool package should exist after build");
            
            // Copy package to local feed
            var packageName = Path.GetFileName(toolPackage!);
            File.Copy(toolPackage, Path.Combine(localSource, packageName));
            
            // Act - Install tool from local source
            var installResult = await RunDotnetCommand(
                $"tool install Morphir.Tool --tool-path {tempDir} --add-source {localSource}");
            
            // Assert
            installResult.ExitCode.Should().Be(0, 
                $"Tool installation should succeed. Output: {installResult.Output}");
            
            var toolPath = Path.Combine(tempDir, "morphir");
            if (OperatingSystem.IsWindows())
            {
                toolPath += ".exe";
            }
            
            File.Exists(toolPath).Should().BeTrue("Tool executable should be created after installation");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
    
    [Test]
    [Skip("Requires packages to be built first and is a potentially destructive test")]
    public async Task ToolCommand_AvailableAfterInstall()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"morphir-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var localSource = Path.Combine(tempDir, "feed");
            Directory.CreateDirectory(localSource);
            
            var toolPackage = TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg");
            toolPackage.Should().NotBeNull("Tool package should exist after build");
            
            // Copy package to local feed
            var packageName = Path.GetFileName(toolPackage!);
            File.Copy(toolPackage, Path.Combine(localSource, packageName));
            
            // Install tool
            await RunDotnetCommand(
                $"tool install Morphir.Tool --tool-path {tempDir} --add-source {localSource}");
            
            // Act - Run the tool command
            var toolPath = Path.Combine(tempDir, "morphir");
            if (OperatingSystem.IsWindows())
            {
                toolPath += ".exe";
            }
            
            var commandResult = await RunCommand(toolPath, "--version");
            
            // Assert
            commandResult.ExitCode.Should().Be(0,
                $"Tool command should execute successfully. Output: {commandResult.Output}");
            commandResult.Output.Should().MatchRegex(@"\d+\.\d+\.\d+",
                "Tool --version should output a semantic version number");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
    
    [Test]
    [Skip("Requires packages to be built first and is a potentially destructive test")]
    public async Task ToolPackage_UninstallsSuccessfully()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"morphir-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var localSource = Path.Combine(tempDir, "feed");
            Directory.CreateDirectory(localSource);
            
            var toolPackage = TestFixture.FindLatestPackage("Morphir.Tool.*.nupkg");
            toolPackage.Should().NotBeNull("Tool package should exist after build");
            
            // Copy package and install
            var packageName = Path.GetFileName(toolPackage!);
            File.Copy(toolPackage, Path.Combine(localSource, packageName));
            await RunDotnetCommand(
                $"tool install Morphir.Tool --tool-path {tempDir} --add-source {localSource}");
            
            // Act - Uninstall tool
            var uninstallResult = await RunDotnetCommand(
                $"tool uninstall Morphir.Tool --tool-path {tempDir}");
            
            // Assert
            uninstallResult.ExitCode.Should().Be(0,
                $"Tool uninstallation should succeed. Output: {uninstallResult.Output}");
            
            var toolPath = Path.Combine(tempDir, "morphir");
            if (OperatingSystem.IsWindows())
            {
                toolPath += ".exe";
            }
            
            File.Exists(toolPath).Should().BeFalse("Tool executable should be removed after uninstallation");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
    
    // Helper methods
    
    private async Task<CommandResult> RunDotnetCommand(string arguments)
    {
        return await RunCommand("dotnet", arguments);
    }
    
    private async Task<CommandResult> RunCommand(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        
        await process.WaitForExitAsync();
        
        return new CommandResult
        {
            ExitCode = process.ExitCode,
            Output = string.IsNullOrEmpty(error) ? output : $"{output}\n{error}"
        };
    }
    
    private record CommandResult
    {
        public int ExitCode { get; init; }
        public string Output { get; init; } = string.Empty;
    }
}
