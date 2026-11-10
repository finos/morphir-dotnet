// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO.Compression;
using FluentAssertions;
using Morphir.TestSupport;
using TUnit.Core;

namespace Morphir.Build.Tests;

/// <summary>
/// cli-offering.yml starts the dotnet-tool offering from the package it built. <c>.github/scripts/install-tool-package.ps1</c>
/// installs that package so that a package with the same id and version elsewhere (nuget.org, another configured feed,
/// a machine cache) cannot be chosen, and checks that the tool store holds the built bytes. These tests run the script
/// against two packages with the same id and version and different content: A, the built one, and B, a conflict.
/// </summary>
public class ToolPackageInstallTests
{
    private const string Id = "Probe.Tool";
    private const string Version = "1.2.3";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private static string Script => Path.Combine(TestFixture.GetRepositoryRoot(), ".github", "scripts", "install-tool-package.ps1");

    [Test]
    public async Task OnlyTheBuiltPackageIsInstalledDespiteConflictingFeedsAndCaches()
    {
        await using var sandbox = TestSandbox.Create("tool-install-");
        var conflict = await Conflict.CreateAsync(sandbox);
        var start = conflict.Contained(ChildProcess.StartInfo("pwsh", "-NoProfile", "-NonInteractive", "-File", Script,
            "-Package", conflict.Built, "-ToolPath", sandbox.PathOf("tools"), "-WorkDir", sandbox.PathOf("nuget work")));

        var result = await ChildProcess.RunAsync(start, Timeout);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        var command = result.StandardOutput.Trim().Split('\n').Last().Trim();
        File.Exists(command).Should().BeTrue($"the script prints the installed command; output: {result.CombinedOutput}");
        Marker(sandbox.PathOf("tools")).Should().Be("A-built", "the built package's bytes are installed, not the conflict's");
    }

    [Test]
    public async Task WithoutIsolationTheSameSetupInstallsTheConflict()
    {
        // The control: the conflicts of the test above are real, so its result comes from the isolation.
        await using var sandbox = TestSandbox.Create("tool-install-");
        var conflict = await Conflict.CreateAsync(sandbox);
        var start = conflict.Contained(ChildProcess.StartInfo("dotnet", "tool", "install", Id, "--version", Version,
            "--tool-path", sandbox.PathOf("naive-tools")));

        var result = await ChildProcess.RunAsync(start, Timeout);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        Marker(sandbox.PathOf("naive-tools")).Should().Be("B-conflict");
    }

    [Test]
    public async Task TheCheckRefusesAToolStoreWithOtherBytes()
    {
        await using var sandbox = TestSandbox.Create("tool-install-");
        var conflict = await Conflict.CreateAsync(sandbox);
        var command = $". '{Script.Replace("'", "''")}'; Assert-InstalledToolPackage '{conflict.Built.Replace("'", "''")}' '{conflict.SeededTools.Replace("'", "''")}'";
        var start = conflict.Contained(ChildProcess.StartInfo("pwsh", "-NoProfile", "-NonInteractive", "-Command", command));

        var result = await ChildProcess.RunAsync(start, Timeout);

        result.ExitCode.Should().NotBe(0, "the tool store holds B, and A was built");
        result.CombinedOutput.Should().Contain("SHA-256");
    }

    /// <summary>The marker text of the tool installed under <paramref name="toolPath"/>.</summary>
    private static string Marker(string toolPath) =>
        File.ReadAllText(Directory.GetFiles(Path.Combine(toolPath, ".store"), "marker.txt", SearchOption.AllDirectories).Single()).Trim();

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
            SeededTools = seededTools;
            Cache = cache;
        }

        public TestSandbox Sandbox { get; }

        public string Built { get; }

        /// <summary>A tool path where NuGet installed B.</summary>
        public string SeededTools { get; }

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

        /// <summary>A dotnet tool package whose entry point is a real assembly, with a marker file that tells A from B.</summary>
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
            Write(zip, "tools/net10.0/any/DotnetToolSettings.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <DotNetCliTool Version="1"><Commands><Command Name="probe-tool" EntryPoint="probe.dll" Runner="dotnet" /></Commands></DotNetCliTool>
                """);
            // The install reads the entry point's PE headers to make the command's shim.
            zip.CreateEntryFromFile(typeof(ToolPackageInstallTests).Assembly.Location, "tools/net10.0/any/probe.dll");
            Write(zip, "tools/net10.0/any/marker.txt", marker);
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
