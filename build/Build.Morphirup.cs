// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Formats.Tar;
using Morphir.Build.Helpers;
using Nuke.Common;
using Nuke.Common.IO;

/// <summary>
/// The release artifacts of the morphirup manager, and a check on them. The release workflow and the pull request
/// workflow run the same targets, through .github/workflows/morphirup-build.yml:
/// <code>
/// ./build.sh --target PackMorphirup CheckMorphirup --rid linux-x64 --morphirup-container debian:12-slim
/// ./build.sh --target MorphirupChecksums --release-assets-dir artifacts/release-assets
/// </code>
/// </summary>
partial class Build
{
    [Parameter("Folder for the morphirup release assets and their checksum file")]
    readonly AbsolutePath ReleaseAssetsDir = RootDirectory / "artifacts" / "release-assets";

    [Parameter("Linux only: a container image with glibc and neither .NET nor libicu, in which CheckMorphirup also runs the executable")]
    readonly string? MorphirupContainer;

    [Parameter("The platforms whose morphirup assets MorphirupChecksums requires (default: every platform releases build)")]
    readonly string[] MorphirupPlatforms = [.. MorphirupRelease.Platforms];

    AbsolutePath MorphirupPublishDirectory => RootDirectory / "artifacts" / "morphirup" / "publish" / Rid!;

    /// <summary>
    /// Publish morphirup with Native AOT for <c>--rid</c>, with the version of CHANGELOG.md, and write
    /// <c>morphirup-{rid}[.exe]</c> and <c>morphirup-{rid}-v{version}.tar.gz</c> into the release assets folder.
    /// </summary>
    Target PackMorphirup => _ => _
        .Description("Publish morphirup with Native AOT for --rid and write its release assets")
        .Requires(() => Rid)
        .Executes(() =>
        {
            if (!TargetPlatform.TryParse(Rid, out var platform) || platform is null)
                throw new Exception($"--rid must be one of {string.Join(", ", TargetPlatform.All.Select(platform => platform.Rid))}; got '{Rid}'.");

            var publish = MorphirupPublishDirectory;
            publish.CreateOrCleanDirectory();
            // Native AOT, without the native symbol file; a RID-specific publish keeps its lock files under obj/.
            var (exit, log) = Capture("dotnet", "publish", RootDirectory / "src" / "Morphirup" / "Morphirup.fsproj",
                "-c", Configuration.ToString(), "-r", Rid!, "-o", publish, "-nologo", "-nodeReuse:false",
                "-p:PublishAot=true", "-p:CopyOutputSymbolsToPublishDirectory=false",
                $"-p:{TargetSettings.RidSpecificPublishProperty}=true");
            File.WriteAllText(RootDirectory / "artifacts" / "morphirup" / $"publish-{Rid}.log", log);
            if (exit != 0)
                throw new Exception($"The Native AOT publish of morphirup for {Rid} failed:{Environment.NewLine}{log}");

            var executable = publish / MorphirupRelease.LaunchName(Rid!);
            if (!File.Exists(executable))
                throw new Exception($"The publish wrote no {MorphirupRelease.LaunchName(Rid!)} into {publish}.");

            var version = Version.ToString();
            var written = MorphirupRelease.WriteAssets(executable, Rid!, version, ReleaseAssetsDir);
            foreach (var name in written)
                Serilog.Log.Information("✓ {Asset}: {Bytes:N0} bytes", ReleaseAssetsDir / name, new FileInfo(ReleaseAssetsDir / name).Length);
        });

    /// <summary>
    /// Check the release assets of <c>--rid</c>, not a source build: the tarball holds exactly the bare executable;
    /// the executable, copied into a folder whose path has a space, runs with <c>PATH</c> reduced to the system folders
    /// and no <c>DOTNET_*</c> variable, prints the release version, and makes the probe's plan-only setup with the
    /// recorded digest (<c>--expect-plan</c>). With <c>--morphirup-container</c> on Linux it runs the same commands in that
    /// image, after checking that the image has neither .NET nor libicu, and the outputs must be equal.
    /// </summary>
    Target CheckMorphirup => _ => _
        .Description("Run the morphirup release asset of --rid in a clean environment and check its output")
        .Requires(() => Rid)
        .After(PackMorphirup)
        .Executes(() =>
        {
            var rid = Rid!;
            var version = Version.ToString();
            var bare = ReleaseAssetsDir / MorphirupRelease.ExecutableName(rid);
            var archive = ReleaseAssetsDir / MorphirupRelease.ArchiveName(rid, version);
            if (!File.Exists(bare) || !File.Exists(archive))
                throw new Exception($"No morphirup release assets for {rid} {version} in {ReleaseAssetsDir}; run PackMorphirup first.");

            var root = RootDirectory / "artifacts" / "morphirup" / "check" / $"{rid} with space";
            root.CreateOrCleanDirectory();
            var launch = root / MorphirupRelease.LaunchName(rid);
            File.Copy(bare, launch);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(launch, ReleaseArchive.ExecutableMode);

            // The tarball holds exactly the bare executable, at {rid}/{name}, executable.
            var extracted = root / "from the tarball";
            Directory.CreateDirectory(extracted);
            using (var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress))
                TarFile.ExtractToDirectory(gzip, extracted, overwriteFiles: false);
            var entries = Directory.GetFiles(extracted, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(extracted, path).Replace('\\', '/')).ToArray();
            if (entries.Length != 1 || entries[0] != $"{rid}/{MorphirupRelease.LaunchName(rid)}")
                throw new Exception($"{archive} holds [{string.Join(", ", entries)}], not only {rid}/{MorphirupRelease.LaunchName(rid)}.");
            if (ReleaseArchive.Sha256(extracted / rid / MorphirupRelease.LaunchName(rid)) != ReleaseArchive.Sha256(bare))
                throw new Exception($"The executable in {archive} differs from {bare}.");

            var inputs = root / "probe inputs";
            Directory.CreateDirectory(inputs);
            foreach (var file in new[] { "toolchain.toml", "recipe.json", "artifact-sizes.json" })
                File.Copy(MorphirupProbeDirectory / file, inputs / file);
            var expectedPlan = File.ReadAllText(MorphirupProbeDirectory / "plan-sha256.txt").Trim();
            string[] Plan(string folder, char separator) =>
            [
                "setup", "--plan", "--platform", "linux-x64",
                "--request", $"{folder}{separator}toolchain.toml",
                "--recipe", $"{folder}{separator}recipe.json",
                "--repository-id", new string('b', 64),
                "--artifact-sizes", $"{folder}{separator}artifact-sizes.json",
                "--expect-plan", expectedPlan,
            ];

            var inherited = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value);
            var environment = MorphirupRelease.CheckEnvironment(inherited, OperatingSystem.IsWindows());

            var hostVersion = RunChecked(launch, ["--version"], environment, root, $"{rid}: --version");
            if (hostVersion.Trim().Split('+')[0] != version)
                throw new Exception($"{rid}: --version printed '{hostVersion.Trim()}', not the release version {version}.");
            var hostPlan = RunChecked(launch, Plan(inputs, Path.DirectorySeparatorChar), environment, root, $"{rid}: setup --plan --expect-plan");
            Serilog.Log.Information("✓ {Rid}: {Version}; the plan-only setup matched the recorded digest, from '{Folder}' with PATH={Path}",
                rid, hostVersion.Trim(), root, environment["PATH"]);

            if (string.IsNullOrEmpty(MorphirupContainer)) return;
            if (!OperatingSystem.IsLinux())
                throw new Exception("--morphirup-container runs only on Linux.");

            // The image must be the clean environment it claims to be: no .NET and no libicu.
            var leftovers = RunChecked("docker", ["run", "--rm", MorphirupContainer!, "sh", "-c",
                "ls /usr/lib/*/libicu* /lib/*/libicu* /usr/lib/libicu* /usr/share/dotnet /usr/lib/dotnet 2>/dev/null; command -v dotnet || true"],
                null, root, "the container image check").Trim();
            if (leftovers.Length > 0)
                throw new Exception($"The image {MorphirupContainer} is not clean: it has {leftovers}.");

            const string mounted = "/morphirup check";
            string[] Docker(params string[] command) =>
                ["run", "--rm", "--network", "none", "--volume", $"{root}:{mounted}:ro", "--workdir", mounted, MorphirupContainer!, .. command];
            var containerVersion = RunChecked("docker", Docker($"{mounted}/morphirup", "--version"), null, root, $"{rid} in {MorphirupContainer}: --version");
            var containerPlan = RunChecked("docker", Docker([$"{mounted}/morphirup", .. Plan($"{mounted}/probe inputs", '/')]), null, root,
                $"{rid} in {MorphirupContainer}: setup --plan --expect-plan");
            if (containerVersion != hostVersion)
                throw new Exception($"In {MorphirupContainer}, --version printed '{containerVersion.Trim()}', not '{hostVersion.Trim()}'.");
            if (containerPlan != hostPlan)
                throw new Exception($"In {MorphirupContainer}, the plan output differs from the runner's.");
            Serilog.Log.Information("✓ {Rid} in {Image}, with no .NET and no libicu: the same version and plan", rid, MorphirupContainer);
        });

    /// <summary>
    /// Write <c>morphirup-v{version}.sha256</c> over every morphirup asset in the release assets folder, then read it
    /// back and check it against the files. Fails when an asset of a required platform is missing.
    /// </summary>
    Target MorphirupChecksums => _ => _
        .Description("Write and verify the checksum file of the morphirup release assets")
        .Executes(() =>
        {
            var version = Version.ToString();
            var missing = MorphirupPlatforms
                .SelectMany(rid => new[] { MorphirupRelease.ExecutableName(rid), MorphirupRelease.ArchiveName(rid, version) })
                .Where(name => !File.Exists(ReleaseAssetsDir / name))
                .ToArray();
            if (missing.Length > 0)
                throw new Exception($"Missing morphirup assets in {ReleaseAssetsDir}: {string.Join(", ", missing)}");

            var checksums = MorphirupRelease.WriteChecksums(ReleaseAssetsDir, version);
            var problems = MorphirupRelease.VerifyChecksums(ReleaseAssetsDir, checksums);
            if (problems.Count > 0)
                throw new Exception($"{checksums} does not match the assets:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
            Serilog.Log.Information("✓ {File}:{NewLine}{Text}", checksums, Environment.NewLine, File.ReadAllText(checksums));
        });

    /// <summary>
    /// Runs a command with an explicit environment (or the inherited one when null), no shell and no window, kills its
    /// process tree after five minutes, and returns stdout. Fails with the output on a non-zero exit code.
    /// </summary>
    static string RunChecked(string command, string[] arguments, IReadOnlyDictionary<string, string>? environment, string workingDirectory, string what)
    {
        var start = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
        {
            start.Environment.Clear();
            foreach (var (key, value) in environment) start.Environment[key] = value;
        }

        using var process = Process.Start(start) ?? throw new Exception($"{what}: could not start {command}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new Exception($"{what}: timed out after five minutes.");
        }

        if (process.ExitCode != 0)
            throw new Exception($"{what}: exit code {process.ExitCode}{Environment.NewLine}{stdout.Result}{stderr.Result}");
        return stdout.Result;
    }
}
