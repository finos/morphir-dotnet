#!/usr/bin/env dotnet run
// Runs every group of the black-box tests (tests/Morphir.BlackBox.Tests) against a published or packaged target.
// Usage: dotnet run scripts/run-e2e-tests.cs [TARGET] [CONFIGURATION]
//
// TARGET is a value of MORPHIR_TEST_TARGET (dev, aot, trimmed, untrimmed, native-aot, self-contained,
// framework-dependent, dotnet-tool, executable), or one of:
//   tool       the development build with the tool host as the default host (MORPHIR_TEST_HOST=tool)
//   all        aot, trimmed, untrimmed and tool
//   offerings  the four packaged offerings
// For executable, set MORPHIR_TEST_EXECUTABLE to the path of the executable and MORPHIR_TEST_HOST to its host.
//
// A published executable that is not there is published first (PublishExecutable, PublishSingleFile,
// PublishSingleFileUntrimmed). A packaged offering with no artifact in artifacts/release (or MORPHIR_RELEASE_DIR) is
// packed first. An artifact packed from another commit than HEAD is refused, unless
// MORPHIR_REUSE_PACKAGED_ARTIFACTS=true says to test it anyway. Each run has MORPHIR_TEST_LANE=published, so the
// tests with no lane and the tests of the published lane run, and each test that the target does not support is
// skipped with its reason. The TRX report of each group is in artifacts/test-results/black-box/<target>/<group>.

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;

var scriptArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var requested = scriptArgs.Length > 0 ? scriptArgs[0] : "all";
var config = scriptArgs.Length > 1 ? scriptArgs[1] : "Release";

// The groups of the black-box project (build/Helpers/TestPlan.cs, TestPlan.Groups).
string[] groups = ["integration", "e2e", "smoke", "journey"];

static string GetProjectRoot()
{
    var currentDir = Directory.GetCurrentDirectory();
    if (Directory.Exists(Path.Combine(currentDir, "scripts"))) return currentDir;
    if (Path.GetFileName(currentDir) == "scripts") return Directory.GetParent(currentDir)!.FullName;
    return currentDir;
}

static string GetCurrentRid() => RuntimeInformation.OSArchitecture switch
{
    Architecture.X64 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64" :
                       RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-x64" : "linux-x64",
    Architecture.Arm64 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-arm64" :
                        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-arm64" : "linux-arm64",
    _ => throw new NotSupportedException($"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
};

static int RunCommand(string command, IReadOnlyDictionary<string, string>? environment, params string[] args)
{
    var start = new ProcessStartInfo(command) { UseShellExecute = false };
    foreach (var argument in args) start.ArgumentList.Add(argument);
    foreach (var (name, value) in environment ?? new Dictionary<string, string>()) start.Environment[name] = value;
    using var process = Process.Start(start)!;
    process.WaitForExit();
    return process.ExitCode;
}

var projectRoot = GetProjectRoot();
Directory.SetCurrentDirectory(projectRoot);
var rid = GetCurrentRid();
var buildScript = Path.Combine(projectRoot, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "build.cmd" : "build.sh");

Console.WriteLine($"Detected platform RID: {rid}");
Console.WriteLine($"Target: {requested}");
Console.WriteLine($"Configuration: {config}");

string[] packagedTargets = ["native-aot", "self-contained", "framework-dependent", "dotnet-tool"];
string[] knownTargets = ["dev", "aot", "trimmed", "untrimmed", "tool", "executable", .. packagedTargets];
var targets = requested switch
{
    "all" => new List<string> { "aot", "trimmed", "untrimmed", "tool" },
    "offerings" => [.. packagedTargets],
    _ when knownTargets.Contains(requested) => [requested],
    _ => throw new ArgumentException($"'{requested}' is not a target. The values are: {string.Join(", ", knownTargets)}, all, offerings."),
};

// The one release folder of this run: PackRelease writes into it (--release-dir) and the tests read from it
// (MORPHIR_RELEASE_DIR). No trailing separator, so the quoted argument of a path with spaces cannot end in \".
var releaseDir = Path.TrimEndingDirectorySeparator(Environment.GetEnvironmentVariable("MORPHIR_RELEASE_DIR") is { Length: > 0 } customRelease
    ? Path.GetFullPath(customRelease)
    : Path.Combine(projectRoot, "artifacts", "release"));
var packCommand = (string deployment) =>
    $"build --target PackRelease --deployment {deployment}{(deployment == "dotnet-tool" ? "" : $" --rid {rid}")} --release-dir \"{releaseDir}\"";

// The launch metadata files that PackRelease wrote for this deployment on this platform (or the portable tool).
List<string> PackagedLaunchFiles(string deployment) =>
    !Directory.Exists(releaseDir) ? new List<string>() : Directory.GetFiles(releaseDir, "*.build-launch.json").Where(file =>
    {
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        var platform = json.RootElement.GetProperty("platform").GetString();
        return json.RootElement.GetProperty("deployment").GetString() == deployment && (platform == rid || platform == "any");
    }).ToList();

static string GitHead(string root)
{
    var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add("rev-parse");
    start.ArgumentList.Add("HEAD");
    using var process = Process.Start(start)!;
    var head = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit();
    return process.ExitCode == 0 ? head : "";
}

var reusePackaged = Environment.GetEnvironmentVariable("MORPHIR_REUSE_PACKAGED_ARTIFACTS") == "true";

// Prints what is under test, and refuses an artifact that was packed from another commit than HEAD: after a code
// change its old bytes would be tested and reported as found.
bool CheckPackagedArtifact(string deployment, string launchFile)
{
    using var launch = JsonDocument.Parse(File.ReadAllText(launchFile));
    var artifact = launch.RootElement.GetProperty("artifact").GetString()!;
    var contentsFile = Path.Combine(releaseDir, artifact + ".build-contents.json");
    string? commit = null, packedAt = null;
    bool? clean = null;
    if (File.Exists(contentsFile))
    {
        using var contents = JsonDocument.Parse(File.ReadAllText(contentsFile));
        if (contents.RootElement.TryGetProperty("origin", out var origin) && origin.ValueKind == JsonValueKind.Object)
        {
            commit = origin.GetProperty("commit").GetString();
            clean = origin.GetProperty("workingTreeClean").GetBoolean();
            packedAt = origin.GetProperty("packedAt").GetString();
        }
    }

    Console.WriteLine($"Packaged {deployment} under test: {artifact}");
    Console.WriteLine($"  release {launch.RootElement.GetProperty("release").GetString()}, SHA-256 {launch.RootElement.GetProperty("sha256").GetString()}");
    Console.WriteLine($"  packed from commit {commit ?? "(not recorded)"}{(clean == false ? " with uncommitted changes" : "")} at {packedAt ?? "(not recorded)"}");

    var head = GitHead(projectRoot);
    if (commit is not null && commit == head)
    {
        // Same commit. Uncommitted changes at pack time cannot be compared, so they are only reported.
        if (clean == false) Console.WriteLine("⚠ It was packed with uncommitted changes, which this check cannot compare.");
        return true;
    }

    var why = commit is null ? "its pack commit is not recorded" : $"it was packed from {commit}, and HEAD is {head}";
    if (reusePackaged)
    {
        Console.WriteLine($"⚠ Testing it anyway, as MORPHIR_REUSE_PACKAGED_ARTIFACTS=true says: {why}.");
        return true;
    }

    Console.Error.WriteLine($"✗ Error: the packaged {deployment} artifact may be stale: {why}. Pack it again " +
        $"({packCommand(deployment)}) or set MORPHIR_REUSE_PACKAGED_ARTIFACTS=true to test it as it is.");
    return false;
}

// Each published executable is in the folder of its own publish target. A target never uses another form.
var published = new Dictionary<string, (string Folder, string Target)>
{
    ["aot"] = ("executables", "PublishExecutable"),
    ["trimmed"] = ("single-file", "PublishSingleFile"),
    ["untrimmed"] = ("single-file-untrimmed", "PublishSingleFileUntrimmed"),
};

foreach (var target in targets)
{
    if (published.TryGetValue(target, out var publish))
    {
        var executable = Path.Combine(projectRoot, "artifacts", publish.Folder, rid, rid.StartsWith("win-") ? "morphir.exe" : "morphir");
        if (File.Exists(executable))
        {
            Console.WriteLine($"✓ The {target} executable is there: {executable}");
            continue;
        }

        Console.WriteLine($"Publishing the {target} executable for {rid}...");
        if (RunCommand(buildScript, null, "--target", publish.Target, "--configuration", config, "--rid", rid) != 0)
        {
            Console.Error.WriteLine($"✗ Error: {publish.Target} failed for {rid}");
            Environment.Exit(1);
        }
    }
    else if (packagedTargets.Contains(target))
    {
        if (PackagedLaunchFiles(target).Count == 0)
        {
            Console.WriteLine($"Packing the {target} release profile for {rid} into {releaseDir}...");
            var packArgs = new List<string> { "--target", "PackRelease", "--deployment", target, "--configuration", config, "--release-dir", releaseDir };
            if (target != "dotnet-tool") packArgs.AddRange(["--rid", rid]);
            if (RunCommand(buildScript, null, [.. packArgs]) != 0)
            {
                Console.Error.WriteLine($"✗ Error: Failed to pack the {target} release profile");
                Environment.Exit(1);
            }
        }

        var launchFiles = PackagedLaunchFiles(target);
        if (launchFiles.Count != 1)
        {
            Console.Error.WriteLine($"✗ Error: {launchFiles.Count} packaged {target} artifacts for {rid} in {releaseDir}; keep exactly one.");
            Environment.Exit(1);
        }

        if (!CheckPackagedArtifact(target, launchFiles[0])) Environment.Exit(1);
    }
    else if (target == "executable")
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MORPHIR_TEST_EXECUTABLE"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MORPHIR_TEST_HOST")))
        {
            Console.Error.WriteLine("✗ Error: the target executable needs MORPHIR_TEST_EXECUTABLE (the path) and MORPHIR_TEST_HOST (native, managed or tool).");
            Environment.Exit(1);
        }
    }
}

// The build of the black-box project (BuildE2ETests) also builds the three hosts of the development build.
var testDllPath = Path.Combine(projectRoot, "tests", "Morphir.BlackBox.Tests", "bin", config, "net11.0", "Morphir.BlackBox.Tests.dll");
if (!File.Exists(testDllPath))
{
    Console.Error.WriteLine($"✗ Error: the black-box test project is not built ({testDllPath}). Run 'build.sh --target BuildE2ETests' first.");
    Environment.Exit(1);
}

var failed = new List<string>();
var passed = new List<string>();
foreach (var target in targets)
{
    var environment = new Dictionary<string, string>
    {
        ["MORPHIR_TEST_TARGET"] = target == "tool" ? "dev" : target,
        ["MORPHIR_TEST_LANE"] = "published",
        ["MORPHIR_RELEASE_DIR"] = releaseDir,
    };
    if (target == "tool") environment["MORPHIR_TEST_HOST"] = "tool";

    foreach (var group in groups)
    {
        Console.WriteLine("");
        Console.WriteLine("================================================");
        Console.WriteLine($"Target {target}, group {group}");
        Console.WriteLine("================================================");
        var results = Path.Combine(projectRoot, "artifacts", "test-results", "black-box", target, group);
        // One group filter for each process. The published lane needs no lane exclusion: the eligibility decision
        // runs the tests with no lane and the tests of the published lane, and skips the others with their reason.
        var exitCode = RunCommand("dotnet", environment, "exec", testDllPath,
            "--treenode-filter", $"/*/*/*/*[Category={group}]",
            "--results-directory", results, "--report-trx", "--report-trx-filename", $"{target}-{group}.trx");
        var name = $"{target}/{group}";
        if (exitCode == 0)
        {
            Console.WriteLine($"✓ {name} PASSED");
            passed.Add(name);
        }
        else
        {
            Console.WriteLine($"✗ {name} FAILED (exit code {exitCode})");
            failed.Add(name);
        }
    }
}

Console.WriteLine("");
Console.WriteLine("================================================");
Console.WriteLine("Black-box test summary");
Console.WriteLine("================================================");
if (passed.Count > 0) Console.WriteLine($"✓ Passed: {string.Join(" ", passed)}");
if (failed.Count > 0) Console.WriteLine($"✗ Failed: {string.Join(" ", failed)}");
Console.WriteLine("================================================");
Environment.Exit(failed.Count > 0 ? 1 : 0);
