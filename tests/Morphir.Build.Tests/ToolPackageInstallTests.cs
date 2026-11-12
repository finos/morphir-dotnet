// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// cli-offering.yml, and the parent's release qualification, start the dotnet-tool offering from the package that was
/// built. <c>.github/scripts/install-tool-package.ps1</c> installs that package, globally into a tool path or locally
/// into a tool manifest, so that a package with the same id and version elsewhere (nuget.org, another configured feed,
/// a machine cache) cannot be chosen, and checks that what the SDK installed is the built bytes. These tests use two
/// packages with the same id and version and different content: A, the built one, and B, a conflict that is on every
/// other path NuGet reads. The entry point of both is the stub morphir-mck, which reports the folder it runs from.
/// </summary>
[Category("PlatformSpecific")]
public class ToolPackageInstallTests
{
    private const string Id = "Probe.Tool";
    private const string Version = "1.2.3";
    private const string Command = "dotnet-probe";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private static string Script => Path.Combine(TestFixture.GetRepositoryRoot(), ".github", "scripts", "install-tool-package.ps1");

    [Test]
    public async Task AToolPathInstallTakesOnlyTheBuiltPackageAndRunsIt()
    {
        await using var sandbox = TestSandbox.Create("tool-install-");
        var conflict = await Conflict.CreateAsync(sandbox);
        var tools = sandbox.PathOf("tools");
        var resultFile = sandbox.PathOf("result.json");

        var result = await RunScript(conflict, "-Package", conflict.Built, "-ToolPath", tools, "-WorkDir", sandbox.PathOf("nuget work"), "-ResultFile", resultFile);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var command = LastLine(result.StandardOutput);
        command.Should().Be(Path.Combine(tools, OperatingSystem.IsWindows() ? Command + ".exe" : Command), "stdout is the command path");
        Marker(Path.Combine(tools, ".store")).Should().Be("A-built");

        using var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile));
        var root = evidence.RootElement;
        root.GetProperty("mode").GetString().Should().Be("tool-path");
        AssertStoredAreBuilt(root, conflict.BuiltSha256, under: Path.Combine(tools, ".store"));
        root.GetProperty("install").GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString())
            .Should().Contain("--configfile").And.Contain("--tool-path").And.NotContain("--add-source");
        root.GetProperty("invocation").GetProperty("file").GetString().Should().Be(command);
        root.GetProperty("invocation").GetProperty("arguments").ValueKind.Should().Be(JsonValueKind.Array, "callers pass it through, not a shell string");
        root.GetProperty("invocation").GetProperty("arguments").GetArrayLength().Should().Be(0);
        root.GetProperty("invocation").GetProperty("environment").ValueKind.Should().Be(JsonValueKind.Object);

        // The command runs the built bytes: the stub reports the folder it ran from.
        var run = await ChildProcess.RunAsync(conflict.Contained(ChildProcess.StartInfo(command, "a", "b c")), Timeout);
        run.ExitCode.Should().Be(0, run.CombinedOutput);
        StubBase(run.StandardOutput).Should().Be(Folder(root.GetProperty("packageFolder").GetString()!, "tools", "net11.0", "any"));
    }

    [Test]
    public async Task ALocalInstallTakesOnlyTheBuiltPackageAndDotnetRunsIt()
    {
        await using var sandbox = TestSandbox.Create("tool-install-");
        var conflict = await Conflict.CreateAsync(sandbox);
        var manifest = sandbox.PathOf("manifest");
        var resultFile = sandbox.PathOf("result.json");

        var result = await RunScript(conflict, "-Package", conflict.Built, "-ToolPath", manifest, "-WorkDir", sandbox.PathOf("nuget work"), "-Local", "-ResultFile", resultFile);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        LastLine(result.StandardOutput).Should().Be(manifest, "in local mode stdout is the manifest folder");
        using var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile));
        var root = evidence.RootElement;
        root.GetProperty("mode").GetString().Should().Be("local");
        var packages = root.GetProperty("caches").GetProperty("packagesFolder").GetString()!;
        Marker(packages).Should().Be("A-built");
        AssertStoredAreBuilt(root, conflict.BuiltSha256, under: packages);
        root.GetProperty("install").GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString())
            .Should().Contain("--local").And.Contain("--configfile").And.NotContain("--add-source");

        // The normal invocation: dotnet <command without dotnet->, from the manifest folder, with the recorded environment.
        var invocation = root.GetProperty("invocation");
        invocation.GetProperty("file").GetString().Should().Be("dotnet");
        string[] words = [.. invocation.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!)];
        words.Should().Equal("probe");
        invocation.GetProperty("workingDirectory").GetString().Should().Be(manifest);
        invocation.GetProperty("entryAssembly").GetString().Should().EndWith("morphir-mck.dll", "the entry assembly is recorded, not run as a command");
        var start = conflict.Contained(ChildProcess.StartInfo("dotnet", [.. words, "a", "b c"]));
        start.WorkingDirectory = manifest;
        foreach (var variable in invocation.GetProperty("environment").EnumerateObject()) start.Environment[variable.Name] = variable.Value.GetString();
        var run = await ChildProcess.RunAsync(start, Timeout);
        run.ExitCode.Should().Be(0, run.CombinedOutput);
        StubBase(run.StandardOutput).Should().Be(Folder(root.GetProperty("packageFolder").GetString()!, "tools", "net11.0", "any"));
    }

    [Test]
    public async Task WithoutIsolationTheSameSetupInstallsTheConflictAndTheCheckRefusesIt()
    {
        // The control: the conflicts are real, so the results above come from the isolation. And the check refuses what
        // a plain install produced.
        await using var sandbox = TestSandbox.Create("tool-install-");
        var conflict = await Conflict.CreateAsync(sandbox);

        var check = await RunCheck(conflict, $"Assert-InstalledToolPackage {Quote(conflict.Built)} {Quote(conflict.SeededTools)}");

        Marker(Path.Combine(conflict.SeededTools, ".store")).Should().Be("B-conflict", "a plain --tool-path install took B");
        check.ExitCode.Should().NotBe(0, "the tool store holds B, and A was built");
        check.CombinedOutput.Should().Contain("SHA-256");
    }

    [Test]
    public async Task WithoutIsolationALocalInstallTakesTheCachedConflictAndTheCheckRefusesIt()
    {
        await using var sandbox = TestSandbox.Create("tool-install-");
        var conflict = await Conflict.CreateAsync(sandbox);
        var manifest = Directory.CreateDirectory(sandbox.PathOf("plain manifest")).FullName;
        await File.WriteAllTextAsync(Path.Combine(manifest, "dotnet-tools.json"), """{ "version": 1, "isRoot": true, "tools": {} }""");
        var start = conflict.Contained(ChildProcess.StartInfo("dotnet", "tool", "install", Id, "--local", "--version", Version));
        start.WorkingDirectory = manifest;

        var install = await ChildProcess.RunAsync(start, Timeout);

        install.ExitCode.Should().Be(0, install.CombinedOutput);
        using (var resolver = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(sandbox.DotnetCliHome, ".dotnet", "toolResolverCache", "1", Id.ToLowerInvariant()))))
        {
            var entry = Path.GetFullPath(resolver.RootElement.EnumerateArray().Single().GetProperty("PathToExecutable").GetString()!);
            entry.Should().StartWith(Path.GetFullPath(conflict.Cache), "a plain --local install resolves the command into the cached B");
        }
        Marker(conflict.Cache).Should().Be("B-conflict");
        var check = await RunCheck(conflict, $"Assert-LocalToolPackage {Quote(conflict.Built)} {Quote(manifest)} {Quote(conflict.Cache)} {Quote(sandbox.DotnetCliHome)}");
        check.ExitCode.Should().NotBe(0);
        check.CombinedOutput.Should().Contain("SHA-256");
    }

    [Test]
    public async Task AnExtractedFileThatTheBuiltPackageLacksFailsTheCheck()
    {
        await using var sandbox = TestSandbox.Create("tool-install-");
        var conflict = await Conflict.CreateAsync(sandbox);
        var tools = sandbox.PathOf("tools");
        var installed = await RunScript(conflict, "-Package", conflict.Built, "-ToolPath", tools, "-WorkDir", sandbox.PathOf("nuget work"));
        installed.ExitCode.Should().Be(0, installed.CombinedOutput);
        var any = Directory.GetDirectories(Path.Combine(tools, ".store"), "any", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(Path.Combine(any, "planted.dll"), "not from the built package");

        var check = await RunCheck(conflict, $"Assert-InstalledToolPackage {Quote(conflict.Built)} {Quote(tools)}");

        check.ExitCode.Should().NotBe(0);
        check.CombinedOutput.Should().Contain("planted.dll");
    }

    [Test]
    public async Task EveryPathInTheResultIsAbsoluteWhenTheArgumentsAreRelative()
    {
        await using var sandbox = TestSandbox.Create("tool-install-");
        var conflict = await Conflict.CreateAsync(sandbox);
        var start = conflict.Contained(ChildProcess.StartInfo("pwsh", "-NoProfile", "-NonInteractive", "-File", Script,
            "-Package", Path.Combine("built", $"{Id}.{Version}.nupkg"), "-ToolPath", "manifest", "-WorkDir", "nuget work", "-Local", "-ResultFile", "result.json"));
        start.WorkingDirectory = sandbox.Root;

        var result = await ChildProcess.RunAsync(start, Timeout);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(sandbox.PathOf("result.json")));
        var root = evidence.RootElement;
        var invocation = root.GetProperty("invocation");
        string?[] paths =
        [
            root.GetProperty("package").GetProperty("path").GetString(),
            root.GetProperty("toolPath").GetString(),
            root.GetProperty("packageFolder").GetString(),
            root.GetProperty("install").GetProperty("workingDirectory").GetString(),
            root.GetProperty("install").GetProperty("configFile").GetString(),
            root.GetProperty("install").GetProperty("feed").GetString(),
            .. root.GetProperty("caches").EnumerateObject().Select(cache => cache.Value.GetString()),
            .. root.GetProperty("stored").EnumerateArray().Select(archive => archive.GetProperty("path").GetString()),
            invocation.GetProperty("workingDirectory").GetString(),
            invocation.GetProperty("manifest").GetString(),
            invocation.GetProperty("resolverCache").GetString(),
            invocation.GetProperty("entryAssembly").GetString(),
            .. invocation.GetProperty("environment").EnumerateObject().Select(variable => variable.Value.GetString()),
        ];
        foreach (var path in paths)
            Path.IsPathFullyQualified(path!).Should().BeTrue($"{path} must not depend on a later working directory");
        invocation.GetProperty("arguments").ValueKind.Should().Be(JsonValueKind.Array);
        invocation.GetProperty("environment").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Test]
    public async Task AFailedRunLeavesNoResultFile()
    {
        await using var sandbox = TestSandbox.Create("tool-install-");
        var conflict = await Conflict.CreateAsync(sandbox);
        var resultFile = sandbox.PathOf("result.json");
        await File.WriteAllTextAsync(resultFile, """{ "mode": "tool-path", "stale": true }""");
        // An existing tool path makes the run fail.
        var tools = Directory.CreateDirectory(sandbox.PathOf("tools")).FullName;

        var result = await RunScript(conflict, "-Package", conflict.Built, "-ToolPath", tools, "-WorkDir", sandbox.PathOf("nuget work"), "-ResultFile", resultFile);

        result.ExitCode.Should().NotBe(0);
        File.Exists(resultFile).Should().BeFalse("a stale or partial result must not be read as success");
    }

    private static Task<ChildProcessResult> RunScript(Conflict conflict, params string[] arguments) =>
        ChildProcess.RunAsync(conflict.Contained(ChildProcess.StartInfo("pwsh", ["-NoProfile", "-NonInteractive", "-File", Script, .. arguments])), Timeout);

    private static Task<ChildProcessResult> RunCheck(Conflict conflict, string call) =>
        ChildProcess.RunAsync(conflict.Contained(ChildProcess.StartInfo("pwsh", "-NoProfile", "-NonInteractive", "-Command", $". {Quote(Script)}; {call}")), Timeout);

    private static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    private static string LastLine(string text) => text.Trim().Split('\n').Last().Trim();

    private static string Folder(params string[] parts) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(parts)));

    private static string StubBase(string standardOutput)
    {
        using var report = JsonDocument.Parse(LastLine(standardOutput));
        return Folder(report.RootElement.GetProperty("base").GetString()!);
    }

    /// <summary>The marker text of the one package extracted under <paramref name="root"/>.</summary>
    private static string Marker(string root) =>
        File.ReadAllText(Directory.GetFiles(root, "marker.txt", SearchOption.AllDirectories).Single()).Trim();

    private static void AssertStoredAreBuilt(JsonElement root, string builtSha256, string under)
    {
        root.GetProperty("package").GetProperty("sha256").GetString().Should().Be(builtSha256);
        var stored = root.GetProperty("stored").EnumerateArray().ToList();
        stored.Should().NotBeEmpty();
        foreach (var archive in stored)
        {
            var path = archive.GetProperty("path").GetString()!;
            archive.GetProperty("sha256").GetString().Should().Be(builtSha256);
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).Should().Be(builtSha256, path);
            Path.GetFullPath(path).Should().StartWith(Path.GetFullPath(under));
        }
    }

    /// <summary>
    /// The built package A in its own folder, and B on every other path NuGet reads: a user-level NuGet.Config and a
    /// NuGet.config in the current directory that list a feed with B, and a global packages folder and a fallback
    /// folder that hold B in the layout NuGet writes.
    /// </summary>
    private sealed class Conflict
    {
        private Conflict(TestSandbox sandbox, string built, string seededTools, string cache)
        {
            Sandbox = sandbox;
            Built = built;
            BuiltSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(built)));
            SeededTools = seededTools;
            Cache = cache;
        }

        public TestSandbox Sandbox { get; }

        public string Built { get; }

        public string BuiltSha256 { get; }

        /// <summary>A tool path where a plain install took B.</summary>
        public string SeededTools { get; }

        /// <summary>A global packages folder that holds B.</summary>
        public string Cache { get; }

        public static async Task<Conflict> CreateAsync(TestSandbox sandbox)
        {
            var built = Package(sandbox.PathOf("built", $"{Id}.{Version}.nupkg"), "A-built");
            var feed = Path.GetDirectoryName(Package(sandbox.PathOf("conflict feed", $"{Id}.{Version}.nupkg"), "B-conflict"))!;
            var config = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="conflict" value="{feed}" />
                  </packageSources>
                </configuration>
                """;
            foreach (var folder in new[] { Path.Combine(sandbox.AppData, "NuGet"), Path.Combine(sandbox.Home, ".nuget", "NuGet"), sandbox.Work })
            {
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(Path.Combine(folder, folder == sandbox.Work ? "NuGet.config" : "NuGet.Config"), config);
            }

            // NuGet itself installs B once, and its store folder becomes the conflicting cache.
            var seeded = sandbox.PathOf("seeded-tools");
            var cache = sandbox.PathOf("conflict cache");
            var conflict = new Conflict(sandbox, built, seeded, cache);
            var seedStart = conflict.Contained(ChildProcess.StartInfo("dotnet", "tool", "install", Id, "--version", Version, "--tool-path", seeded));
            seedStart.Environment["NUGET_PACKAGES"] = sandbox.PathOf("seed packages");
            seedStart.Environment.Remove("NUGET_FALLBACK_PACKAGES");
            var seed = await ChildProcess.RunAsync(seedStart, Timeout);
            if (seed.ExitCode != 0) throw new InvalidOperationException($"Seeding B failed: {seed.CombinedOutput}");
            CopyDirectory(Path.Combine(seeded, ".store", Id.ToLowerInvariant(), Version, Id.ToLowerInvariant()), Path.Combine(cache, Id.ToLowerInvariant()));
            return conflict;
        }

        /// <summary>A start info in the sandbox, with the conflicting cache as the global and fallback packages folders.</summary>
        public ProcessStartInfo Contained(ProcessStartInfo start)
        {
            start.WorkingDirectory = Sandbox.Work;
            Sandbox.Apply(start);
            start.Environment["NUGET_PACKAGES"] = Cache;
            start.Environment["NUGET_FALLBACK_PACKAGES"] = Cache;
            return start;
        }

        /// <summary>A dotnet tool package whose entry point is the stub morphir-mck, with a marker file that tells A from B.</summary>
        private static string Package(string path, string marker)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            Write(zip, $"{Id}.nuspec", $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{Id}</id>
                    <version>{Version}</version>
                    <authors>morphir-dotnet tests</authors>
                    <description>A tool package for the install tests.</description>
                    <packageTypes><packageType name="DotnetTool" /></packageTypes>
                  </metadata>
                </package>
                """);
            Write(zip, "tools/net11.0/any/DotnetToolSettings.xml", $"""
                <?xml version="1.0" encoding="utf-8"?>
                <DotNetCliTool Version="1"><Commands><Command Name="{Command}" EntryPoint="morphir-mck.dll" Runner="dotnet" /></Commands></DotNetCliTool>
                """);
            foreach (var file in new[] { "morphir-mck.dll", "morphir-mck.runtimeconfig.json" })
                zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, file), $"tools/net11.0/any/{file}");
            Write(zip, "tools/net11.0/any/marker.txt", marker);
            return path;
        }

        private static void Write(ZipArchive zip, string name, string text)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(text);
        }

        private static void CopyDirectory(string source, string target)
        {
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(directory.Replace(source, target));
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(source, target));
        }
    }
}
