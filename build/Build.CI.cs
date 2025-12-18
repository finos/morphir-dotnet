using System;
using Nuke.Common;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// Build targets for CI/CD workflows and local simulation of GitHub Actions pipeline
/// </summary>
partial class Build
{
    /// <summary>
    /// Simulates the lint job from development.yml workflow
    /// Runs: Restore -> Lint (format check)
    /// </summary>
    Target CILint => _ => _
        .DependsOn(Restore, Lint)
        .Description("Run lint checks (simulates GitHub Actions lint job)")
        .Executes(() =>
        {
            Serilog.Log.Information("✓ Lint checks completed successfully");
        });

    /// <summary>
    /// Simulates the test job from development.yml workflow
    /// Runs: Restore -> Compile -> Test
    /// This is the core build and test sequence that runs on all platforms
    /// </summary>
    Target CITest => _ => _
        .DependsOn(Restore, Compile, Test)
        .Description("Run build and tests (simulates GitHub Actions test job)")
        .Executes(() =>
        {
            Serilog.Log.Information("✓ Build and test completed successfully");
        });

    /// <summary>
    /// Simulates the complete GitHub Actions development.yml workflow locally
    /// Runs: CILint -> CITest -> aggregation
    /// This allows developers to verify their changes will pass CI before pushing
    /// </summary>
    Target DevWorkflow => _ => _
        .DependsOn(CILint, CITest)
        .Description("Run complete development workflow (simulates entire GitHub Actions PR build)")
        .Executes(() =>
        {
            Serilog.Log.Information("═══════════════════════════════════════");
            Serilog.Log.Information("✓ All CI checks passed!");
            Serilog.Log.Information("═══════════════════════════════════════");
            Serilog.Log.Information("Your changes are ready for PR submission");
            Serilog.Log.Information("");
            Serilog.Log.Information("What just ran:");
            Serilog.Log.Information("  ✓ Lint (code formatting)");
            Serilog.Log.Information("  ✓ Build (compile all projects)");
            Serilog.Log.Information("  ✓ Test (unit tests)");
            Serilog.Log.Information("");
            Serilog.Log.Information("This simulates the GitHub Actions workflow:");
            Serilog.Log.Information("  .github/workflows/development.yml");
        });
}
