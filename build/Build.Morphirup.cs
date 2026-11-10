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

    AbsolutePath MorphirupProjectDirectory => RootDirectory / "src" / "Morphirup";

    /// <summary>The response file of the Native AOT compiler for the last publish of <c>--rid</c>: what it linked.</summary>
    AbsolutePath MorphirupIlcResponseFile => MorphirupProjectDirectory / "obj" / Configuration.ToString() / "net10.0" / Rid! / "native" / "morphirup.ilc.rsp";

    /// <summary>
    /// The packages the last publish of <c>--rid</c> linked, from the compiler's response file. Fails on a linked file
    /// that is in neither a restored package nor this repository.
    /// </summary>
    IReadOnlyList<LinkedPackage> MorphirupLinkedPackages()
    {
        if (!File.Exists(MorphirupIlcResponseFile))
            throw new Exception($"No Native AOT response file at {MorphirupIlcResponseFile}; run PackMorphirup first.");
        var files = ThirdPartyNotices.LinkedFiles(File.ReadAllText(MorphirupIlcResponseFile), MorphirupProjectDirectory);
        var (packages, unowned) = ThirdPartyNotices.Packages(files, RootDirectory);
        if (unowned.Count > 0)
            throw new Exception($"Native AOT linked files that are in no restored package and not in this repository, so they have no notices:{Environment.NewLine}{string.Join(Environment.NewLine, unowned)}");
        return packages;
    }

    /// <summary>The changed and untracked paths of the working tree, as <c>git status --porcelain</c> lists them.</summary>
    HashSet<string> WorkingTreeChanges() =>
        [.. RunChecked("git", ["status", "--porcelain", "--untracked-files=all"], null, RootDirectory, "git status")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r'))];

    /// <summary>
    /// Publish morphirup with Native AOT for <c>--rid</c>, with the version of CHANGELOG.md, and write
    /// <c>morphirup-{rid}[.exe]</c>, <c>morphirup-{rid}-v{version}.tar.gz</c> and
    /// <c>morphirup-v{version}-THIRD-PARTY-NOTICES.txt</c> into the release assets folder. The third-party notices are
    /// generated from the packages the compiler linked. The publish must leave the working tree as it found it.
    /// </summary>
    Target PackMorphirup => _ => _
        .Description("Publish morphirup with Native AOT for --rid and write its release assets")
        .Requires(() => Rid)
        .Executes(() =>
        {
            if (!TargetPlatform.TryParse(Rid, out var platform) || platform is null)
                throw new Exception($"--rid must be one of {string.Join(", ", TargetPlatform.All.Select(platform => platform.Rid))}; got '{Rid}'.");

            var before = WorkingTreeChanges();
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

            // A RID-specific restore must not rewrite the committed lock files, and the publish writes nothing else that git sees.
            var changed = WorkingTreeChanges().Except(before).Order(StringComparer.Ordinal).ToArray();
            if (changed.Length > 0)
                throw new Exception($"The publish of morphirup for {Rid} changed the working tree:{Environment.NewLine}{string.Join(Environment.NewLine, changed)}");

            var packages = MorphirupLinkedPackages();
            var version = Version.ToString();
            var notices = ThirdPartyNotices.Write(version, Rid!, packages, RootDirectory / "build" / "notices",
                File.ReadAllText(RootDirectory / "tests" / "Fixtures" / "Unicode" / "17.0.0" / "LICENSE.txt"));
            var written = MorphirupRelease.WriteAssets(executable, Rid!, version, ReleaseAssetsDir, RootDirectory / "LICENSE", RootDirectory / "NOTICE", notices);
            Serilog.Log.Information("Linked packages, named in the third-party notices: {Packages}",
                string.Join(", ", packages.Select(package => $"{package.Id} {package.Version}")));
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
        // The packages restore into this machine's NuGet folder, at its own path, so the third-party notices can read
        // the packages that the compiler's response file names after the container is gone.
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        Directory.CreateDirectory(packages);

        const string script = """
            set -eu
            export DEBIAN_FRONTEND=noninteractive
            apt-get update -qq
            apt-get install -y -qq --no-install-recommends clang zlib1g-dev libicu70 libssl3 ca-certificates > /dev/null
            printf '[safe]\n\tdirectory = *\n' > "$HOME/.gitconfig"
            echo "Publishing in $(. /etc/os-release && echo "$PRETTY_NAME"), $(ldd --version | head -n 1)"
            status=0
            dotnet "$@" || status=$?
            chown -R "$MORPHIRUP_OWNER" "$MORPHIRUP_REPOSITORY" "$NUGET_PACKAGES"
            exit "$status"
            """;
        List<string> docker =
        [
            "run", "--rm",
            "--volume", $"{RootDirectory}:{RootDirectory}", "--workdir", RootDirectory,
            "--volume", $"{packages}:{packages}", "--env", $"NUGET_PACKAGES={packages}",
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

            // The tarball holds exactly the executable, LICENSE, NOTICE and the third-party notices, under {rid}/.
            var extracted = root / "from the tarball";
            Directory.CreateDirectory(extracted);
            using (var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress))
                TarFile.ExtractToDirectory(gzip, extracted, overwriteFiles: false);
            var entries = Directory.GetFiles(extracted, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(extracted, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
            if (!entries.SequenceEqual(MorphirupRelease.ArchiveEntries(rid)))
                throw new Exception($"{archive} holds [{string.Join(", ", entries)}], not exactly [{string.Join(", ", MorphirupRelease.ArchiveEntries(rid))}].");
            if (ReleaseArchive.Sha256(extracted / rid / MorphirupRelease.LaunchName(rid)) != ReleaseArchive.Sha256(bare))
                throw new Exception($"The executable in {archive} differs from {bare}.");
            foreach (var name in new[] { "LICENSE", "NOTICE" })
                if (ReleaseArchive.Sha256(extracted / rid / name) != ReleaseArchive.Sha256(RootDirectory / name))
                    throw new Exception($"{name} in {archive} differs from the repository's.");

            // The third-party notices: the same text as the release's notices file, and naming every package linked in.
            var noticesAsset = ReleaseAssetsDir / MorphirupRelease.NoticesFileName(version);
            var notices = File.ReadAllText(extracted / rid / MorphirupRelease.NoticesEntry);
            if (!File.Exists(noticesAsset) || File.ReadAllText(noticesAsset) != notices)
                throw new Exception($"{MorphirupRelease.NoticesEntry} in {archive} differs from {noticesAsset}, or that file is missing.");
            var linked = MorphirupLinkedPackages();
            var unnamed = ThirdPartyNotices.Missing(notices, linked, rid);
            if (linked.Count == 0 || unnamed.Count > 0)
                throw new Exception($"The third-party notices of {rid} do not name every linked package: {(linked.Count == 0 ? "none were found" : string.Join(", ", unnamed))}.");
            Serilog.Log.Information("✓ {Rid}: the archive holds [{Entries}]; the notices name all {Count} linked packages",
                rid, string.Join(", ", entries), linked.Count);

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

            foreach (var (pinned, index) in MorphirupContainers.Select((image, index) => (image, index)))
            {
                // A Linux executable needs glibc and nothing else, so the check removes OpenSSL where the image ships it
                // (ubuntu:22.04 has libssl3), in a derived image; the precondition below then checks what is left.
                var container = $"morphirup-check-{rid}-{index}";
                var image = $"morphirup-check-{rid}:{index}";
                var purge = RunChecked("docker", ["run", "--name", container, pinned, "sh", "-c", """
                    removed=""
                    for package in libssl3 libssl1.1; do
                        if dpkg-query -W -f='${Status}' "$package" 2>/dev/null | grep -q 'install ok installed'; then
                            dpkg --purge --force-depends "$package" > /dev/null 2>&1
                            removed="$removed $package"
                        fi
                    done
                    echo "$removed"
                    """], null, root, $"removing OpenSSL from {pinned}").Trim();
                RunChecked("docker", ["commit", container, image], null, root, $"committing {image}");
                RunChecked("docker", ["rm", container], null, root, $"removing the container {container}");
                Serilog.Log.Information("{Image}: {Removed}", pinned, purge.Length > 0 ? $"removed {purge} (dpkg --purge --force-depends) in {image}" : "ships no OpenSSL package");

                // The image must be the clean environment it claims to be: glibc and no .NET, libicu, libssl or libcrypto.
                var leftovers = RunChecked("docker", ["run", "--rm", image, "sh", "-c",
                    "ls -d /usr/lib/*/libicu* /lib/*/libicu* /usr/lib/libicu* /usr/lib/*/libssl.so* /lib/*/libssl.so* /usr/lib/libssl.so* "
                    + "/usr/lib/*/libcrypto.so* /lib/*/libcrypto.so* /usr/lib/libcrypto.so* /usr/share/dotnet /usr/lib/dotnet 2>/dev/null; command -v dotnet || true"],
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
                Serilog.Log.Information("✓ {Rid} in {Image} ({Glibc}), with no .NET, libicu, libssl or libcrypto: the same version and plan", rid, pinned, glibc);
                RunChecked("docker", ["rmi", image], null, root, $"removing the image {image}");
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

            // One notices file serves every executable: each archive must hold the same text.
            var noticesAsset = ReleaseAssetsDir / MorphirupRelease.NoticesFileName(version);
            if (!File.Exists(noticesAsset))
                throw new Exception($"Missing {noticesAsset}.");
            var released = File.ReadAllText(noticesAsset);
            foreach (var rid in MorphirupPlatforms)
            {
                using var gzip = new GZipStream(File.OpenRead(ReleaseAssetsDir / MorphirupRelease.ArchiveName(rid, version)), CompressionMode.Decompress);
                using var tar = new TarReader(gzip);
                string? inArchive = null;
                while (tar.GetNextEntry() is { } entry)
                    if (entry.Name == $"{rid}/{MorphirupRelease.NoticesEntry}")
                        inArchive = new StreamReader(entry.DataStream!).ReadToEnd();
                if (inArchive != released)
                    throw new Exception($"The third-party notices in the {rid} archive differ from {noticesAsset}.");
            }

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
