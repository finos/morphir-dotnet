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
/// ./build.sh --target PackMorphirup CheckMorphirup --rid linux-x64 --morphirup-build-image &lt;MorphirupRelease.LinuxBuildImage&gt;
///     --morphirup-containers debian:12-slim &lt;MorphirupRelease.LinuxBuildImage&gt;
/// ./build.sh --target MorphirupChecksums --release-assets-dir artifacts/release-assets
/// </code>
/// </summary>
partial class Build
{
    [Parameter("Folder for the morphirup release assets and their checksum file")]
    readonly AbsolutePath ReleaseAssetsDir = RootDirectory / "artifacts" / "release-assets";

    [Parameter("Linux only: publish morphirup inside this image, with this machine's .NET SDK mounted, so the executable needs that image's glibc and no newer")]
    readonly string? MorphirupBuildImage;

    [Parameter("Linux only: container images with glibc and neither .NET nor libicu, in which CheckMorphirup also runs the executable")]
    readonly string[] MorphirupContainers = [];

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
            string[] arguments =
            [
                "publish", RootDirectory / "src" / "Morphirup" / "Morphirup.fsproj",
                "-c", Configuration.ToString(), "-r", Rid!, "-o", publish, "-nologo", "-nodeReuse:false",
                "-p:PublishAot=true", "-p:CopyOutputSymbolsToPublishDirectory=false",
                $"-p:{TargetSettings.RidSpecificPublishProperty}=true",
            ];
            var (exit, log) = string.IsNullOrEmpty(MorphirupBuildImage)
                ? Capture("dotnet", arguments)
                : PublishInContainer(MorphirupBuildImage!, platform, arguments);
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
    /// Runs <c>dotnet {arguments}</c> inside <paramref name="image"/>, which links the executable against that image's
    /// glibc. The repository and this machine's .NET SDK are mounted at their own paths (the SDK read-only), so the
    /// publish uses the SDK of global.json and writes where a publish on the runner would. The image gets clang and the
    /// zlib headers that Native AOT links with, and libicu and the certificates that the SDK needs; the executable needs
    /// none of them. Root in the container owns what it writes, so the script gives the repository back to this user.
    /// </summary>
    (int ExitCode, string Output) PublishInContainer(string image, TargetPlatform platform, string[] arguments)
    {
        if (!OperatingSystem.IsLinux() || platform.OperatingSystem != "linux")
            throw new Exception($"--morphirup-build-image publishes a Linux executable on Linux; this is {Rid} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.");

        var sdk = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        if (!File.Exists(Path.Combine(sdk, "dotnet")))
            throw new Exception($"No dotnet in {sdk}, the .NET root of this build.");
        var owner = $"{RunChecked("id", ["-u"], null, RootDirectory, "id -u").Trim()}:{RunChecked("id", ["-g"], null, RootDirectory, "id -g").Trim()}";

        const string script = """
            set -eu
            export DEBIAN_FRONTEND=noninteractive
            apt-get update -qq
            apt-get install -y -qq --no-install-recommends clang zlib1g-dev libicu70 libssl3 ca-certificates > /dev/null
            printf '[safe]\n\tdirectory = *\n' > "$HOME/.gitconfig"
            echo "Publishing in $(. /etc/os-release && echo "$PRETTY_NAME"), $(ldd --version | head -n 1)"
            status=0
            dotnet "$@" || status=$?
            chown -R "$MORPHIRUP_OWNER" "$MORPHIRUP_REPOSITORY"
            exit "$status"
            """;
        List<string> docker =
        [
            "run", "--rm",
            "--volume", $"{RootDirectory}:{RootDirectory}", "--workdir", RootDirectory,
            "--env", $"MORPHIRUP_OWNER={owner}", "--env", $"MORPHIRUP_REPOSITORY={RootDirectory}",
            "--env", $"DOTNET_ROOT={sdk}", "--env", $"PATH={sdk}:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
            "--env", "DOTNET_NOLOGO=1", "--env", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "--env", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1",
        ];
        if (!sdk.StartsWith(RootDirectory, StringComparison.Ordinal))
            docker.InsertRange(2, ["--volume", $"{sdk}:{sdk}:ro"]);
        docker.AddRange([image, "sh", "-c", script, "sh", .. arguments]);

        Serilog.Log.Information("Publishing morphirup for {Rid} in {Image}, with the .NET SDK in {Sdk}", Rid, image, sdk);
        return Capture("docker", [.. docker]);
    }

    /// <summary>
    /// Check the release assets of <c>--rid</c>, not a source build: the tarball holds exactly the bare executable;
    /// the executable, copied into a folder whose path has a space, runs with <c>PATH</c> reduced to the system folders
    /// and no <c>DOTNET_*</c> variable, prints the release version, and makes the probe's plan-only setup with the
    /// recorded digest (<c>--expect-plan</c>). A Linux executable must need no glibc above
    /// <see cref="MorphirupRelease.GlibcFloor"/>; a macOS executable's minimum macOS version is logged. With
    /// <c>--morphirup-containers</c> on Linux it runs the same commands in each image, after checking that the image has
    /// neither .NET nor libicu, and the outputs must be equal.
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

            if (rid.StartsWith("linux-", StringComparison.Ordinal))
            {
                // The glibc versions the executable needs, from its version needs section; none may be above the floor.
                var readelf = RunChecked("readelf", ["--version-info", "--wide", bare], null, root, $"{rid}: readelf --version-info");
                var needed = MorphirupRelease.HighestGlibc(readelf)
                    ?? throw new Exception($"{rid}: readelf found no GLIBC_ version that {bare} needs.");
                if (MorphirupRelease.IsAboveFloor(needed.Version))
                    throw new Exception($"{rid}: {bare} needs GLIBC_{needed.Version} from {needed.File}, above the floor GLIBC_{MorphirupRelease.GlibcFloor}.{Environment.NewLine}{readelf}");
                Serilog.Log.Information("✓ {Rid}: the highest glibc version needed is GLIBC_{Needed} ({File}); the floor is GLIBC_{Floor}",
                    rid, needed.Version, needed.File, MorphirupRelease.GlibcFloor);
            }
            else if (rid.StartsWith("osx-", StringComparison.Ordinal))
            {
                var minimum = MorphirupRelease.MacOsMinimum(RunChecked("otool", ["-l", bare], null, root, $"{rid}: otool -l"));
                Serilog.Log.Information("{Rid}: LC_BUILD_VERSION declares the minimum macOS {Minimum}", rid, minimum ?? "(none)");
            }

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

            if (MorphirupContainers.Length == 0) return;
            if (!OperatingSystem.IsLinux())
                throw new Exception("--morphirup-containers runs only on Linux.");

            foreach (var image in MorphirupContainers)
            {
                // The image must be the clean environment it claims to be: no .NET and no libicu.
                var leftovers = RunChecked("docker", ["run", "--rm", image, "sh", "-c",
                    "ls /usr/lib/*/libicu* /lib/*/libicu* /usr/lib/libicu* /usr/share/dotnet /usr/lib/dotnet 2>/dev/null; command -v dotnet || true"],
                    null, root, $"the check of the image {image}").Trim();
                if (leftovers.Length > 0)
                    throw new Exception($"The image {image} is not clean: it has {leftovers}.");
                var glibc = RunChecked("docker", ["run", "--rm", image, "sh", "-c", "ldd --version | head -n 1"], null, root, $"glibc of {image}").Trim();

                const string mounted = "/morphirup check";
                string[] Docker(params string[] command) =>
                    ["run", "--rm", "--network", "none", "--volume", $"{root}:{mounted}:ro", "--workdir", mounted, image, .. command];
                var containerVersion = RunChecked("docker", Docker($"{mounted}/morphirup", "--version"), null, root, $"{rid} in {image}: --version");
                var containerPlan = RunChecked("docker", Docker([$"{mounted}/morphirup", .. Plan($"{mounted}/probe inputs", '/')]), null, root,
                    $"{rid} in {image}: setup --plan --expect-plan");
                if (containerVersion != hostVersion)
                    throw new Exception($"In {image}, --version printed '{containerVersion.Trim()}', not '{hostVersion.Trim()}'.");
                if (containerPlan != hostPlan)
                    throw new Exception($"In {image}, the plan output differs from the runner's.");
                Serilog.Log.Information("✓ {Rid} in {Image} ({Glibc}), with no .NET and no libicu: the same version and plan", rid, image, glibc);
            }
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
