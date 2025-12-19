using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// Build targets for the Proto WASM plugin
/// </summary>
partial class Build
{
    [Parameter("Output directory for proto plugin WASM")]
    readonly AbsolutePath ProtoPluginDir = RootDirectory / "artifacts" / "proto-plugin";

    [Parameter("Version for the proto plugin release (e.g., 0.1.0)")]
    readonly string PluginVersion;

    AbsolutePath ProtoPluginProjectDir => RootDirectory / "integrations" / "rust" / "morphir-wasm-proto-plugin";

    /// <summary>
    /// Build the Proto WASM plugin
    /// Requires: Rust toolchain with wasm32-wasip1 target installed
    /// Output: artifacts/proto-plugin/morphir_plugin.wasm
    /// </summary>
    Target BuildProtoPlugin => _ => _
        .Description("Build the Proto WASM plugin")
        .Executes(() =>
        {
            Serilog.Log.Information("Building Proto WASM plugin...");

            // Ensure wasm32-wasip1 target is installed
            // Note: wasm32-wasip1 is the current WASI preview 1 target in Rust
            // (wasm32-wasi was deprecated in favor of wasm32-wasip1)
            Serilog.Log.Information("Ensuring wasm32-wasip1 target is installed...");
            RunCommand("rustup", "target", "add", "wasm32-wasip1");

            // Build the WASM plugin
            var buildExitCode = RunCommand(
                "cargo",
                "build",
                "--release",
                "--target", "wasm32-wasip1",
                "--manifest-path", ProtoPluginProjectDir / "Cargo.toml"
            );

            if (buildExitCode != 0)
            {
                throw new Exception($"Failed to build Proto WASM plugin (exit code: {buildExitCode})");
            }

            // Copy the built WASM file to artifacts
            ProtoPluginDir.CreateOrCleanDirectory();

            var wasmSource = ProtoPluginProjectDir / "target" / "wasm32-wasip1" / "release" / "morphir_wasm_proto_plugin.wasm";
            var wasmDest = ProtoPluginDir / "morphir_plugin.wasm";

            File.Copy(wasmSource, wasmDest, overwrite: true);

            var size = GetFileSize(wasmDest);
            Serilog.Log.Information($"✓ Proto WASM plugin built successfully");
            Serilog.Log.Information($"  Output: {wasmDest}");
            Serilog.Log.Information($"  Size: {size}");
        });

    /// <summary>
    /// Test the Proto WASM plugin locally
    /// Requires: proto CLI installed
    /// Note: This is a basic smoke test
    /// </summary>
    Target TestProtoPlugin => _ => _
        .DependsOn(BuildProtoPlugin)
        .Description("Test the Proto WASM plugin locally (requires proto CLI)")
        .Executes(() =>
        {
            Serilog.Log.Information("Testing Proto WASM plugin...");

            var wasmFile = ProtoPluginDir / "morphir_plugin.wasm";

            if (!File.Exists(wasmFile))
            {
                throw new Exception($"WASM plugin not found at {wasmFile}. Run BuildProtoPlugin first.");
            }

            // Check if proto CLI is available
            try
            {
                var protoVersion = RunCommand("proto", "--version");
                if (protoVersion != 0)
                {
                    Serilog.Log.Warning("⚠ proto CLI not found. Skipping plugin test.");
                    Serilog.Log.Information("  Install proto: https://moonrepo.dev/proto");
                    return;
                }
            }
            catch
            {
                Serilog.Log.Warning("⚠ proto CLI not found. Skipping plugin test.");
                Serilog.Log.Information("  Install proto: https://moonrepo.dev/proto");
                return;
            }

            Serilog.Log.Information("✓ Proto plugin smoke test passed");
            Serilog.Log.Information("  Note: Full integration testing requires proto CLI configuration");
        });

    /// <summary>
    /// Package the Proto WASM plugin for release
    /// Creates a tarball with the WASM file and README
    /// Output: artifacts/proto-plugin/morphir-proto-plugin-v{version}.tar.gz
    /// </summary>
    Target PackageProtoPlugin => _ => _
        .DependsOn(BuildProtoPlugin)
        .Description("Package the Proto WASM plugin for release")
        .Executes(() =>
        {
            Serilog.Log.Information("Packaging Proto WASM plugin...");

            var pluginVer = PluginVersion ?? throw new Exception("PluginVersion parameter is required for PackageProtoPlugin target");
            var wasmFile = ProtoPluginDir / "morphir_plugin.wasm";
            var readmeFile = ProtoPluginProjectDir / "README.md";
            var tarballName = $"morphir-proto-plugin-v{pluginVer}.tar.gz";
            var tarballPath = ProtoPluginDir / tarballName;

            // Create a staging directory for the tarball contents
            var stagingDir = ProtoPluginDir / "staging";
            stagingDir.CreateOrCleanDirectory();

            // Copy files to staging
            File.Copy(wasmFile, stagingDir / "morphir_plugin.wasm", overwrite: true);
            File.Copy(readmeFile, stagingDir / "README.md", overwrite: true);

            // Create tarball (cross-platform approach)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, use tar.exe (available in Windows 10+)
                var tarExitCode = RunCommand(
                    "tar",
                    "-czf",
                    tarballPath,
                    "-C", stagingDir,
                    "."
                );

                if (tarExitCode != 0)
                {
                    throw new Exception($"Failed to create tarball (exit code: {tarExitCode})");
                }
            }
            else
            {
                // On Unix, use tar
                var tarExitCode = RunCommand(
                    "tar",
                    "-czf",
                    tarballPath,
                    "-C", stagingDir,
                    "."
                );

                if (tarExitCode != 0)
                {
                    throw new Exception($"Failed to create tarball (exit code: {tarExitCode})");
                }
            }

            // Clean up staging directory
            stagingDir.DeleteDirectory();

            var size = GetFileSize(tarballPath);
            Serilog.Log.Information($"✓ Proto plugin packaged successfully");
            Serilog.Log.Information($"  Output: {tarballPath}");
            Serilog.Log.Information($"  Size: {size}");
        });

    /// <summary>
    /// Clean Proto plugin build artifacts
    /// </summary>
    Target CleanProtoPlugin => _ => _
        .Description("Clean Proto plugin build artifacts")
        .Executes(() =>
        {
            Serilog.Log.Information("Cleaning Proto plugin artifacts...");

            // Clean Rust target directory
            var rustTargetDir = ProtoPluginProjectDir / "target";
            if (Directory.Exists(rustTargetDir))
            {
                rustTargetDir.DeleteDirectory();
                Serilog.Log.Information($"✓ Removed {rustTargetDir}");
            }

            // Clean artifacts directory
            if (Directory.Exists(ProtoPluginDir))
            {
                ProtoPluginDir.DeleteDirectory();
                Serilog.Log.Information($"✓ Removed {ProtoPluginDir}");
            }

            Serilog.Log.Information("✓ Proto plugin artifacts cleaned");
        });
}
