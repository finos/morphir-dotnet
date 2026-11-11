// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Morphir.Build.Helpers;
using Fallout.Common.IO;

/// <summary>
/// The shared check of a Native AOT executable, used by CheckMorphirup and ProbeMorphirNative: the Linux publish inside the
/// pinned build image, the copy into a folder whose path has a space, what <c>readelf</c> says the executable needs, the
/// clean images, and the runs of an executable's own commands on the runner and in each image. The pure parts are in
/// <see cref="NativeCheck"/>.
/// </summary>
partial class Build
{
    /// <summary>What a Linux executable needs: the highest glibc version and every shared library by name.</summary>
    sealed record LinuxNeeds((Version Version, string File)? HighestGlibc, IReadOnlyList<string> Libraries, string VersionInfo);

    /// <summary>A clean image prepared from a pinned one: OpenSSL purged in a derived image, what is left that must not be, and its glibc.</summary>
    sealed record CleanImage(string Pinned, string Image, string Removed, string Leftovers, string Glibc);

    /// <summary>
    /// Runs <c>dotnet {arguments}</c> inside <paramref name="image"/>, which links the executable against that image's
    /// glibc. The repository and this machine's .NET SDK are mounted at their own paths (the SDK read-only), so the
    /// publish uses the SDK of global.json and writes where a publish on the runner would. The image gets clang and the
    /// zlib headers that Native AOT links with, and libicu and the certificates that the SDK needs. The probe measures
    /// the executable's runtime dependencies separately. Root in the container owns what it writes, so the script gives the repository back to this user.
    /// </summary>
    (int ExitCode, string Output) PublishInContainer(string image, TargetPlatform platform, string[] arguments)
    {
        if (!OperatingSystem.IsLinux() || platform.OperatingSystem != "linux")
            throw new Exception($"A publish inside an image builds a Linux executable on Linux; this is {Rid} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.");

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
            . "$MORPHIRUP_REPOSITORY/build/ubuntu-native-dependencies.sh"
            ubuntu_native_dependencies
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

        Serilog.Log.Information("Publishing for {Rid} in {Image}, with the .NET SDK in {Sdk}", Rid, image, sdk);
        return Capture("docker", [.. docker]);
    }

    /// <summary>The declarations of native libraries that no package provides: <c>build/notices/native/</c>.</summary>
    AbsolutePath NativeLibraryDeclarations => RootDirectory / "build" / "notices" / "native";

    /// <summary>
    /// The native libraries that <paramref name="project"/> adds to its Native AOT link for <paramref name="rid"/> (its
    /// <c>NativeLibrary</c> items), each checked against the packages and the declarations in
    /// <see cref="NativeLibraryDeclarations"/>. Returns the declared entries the link includes, for the notices; fails the
    /// build when a library is in no package and has no declared entry, or has another SHA-256.
    /// </summary>
    IReadOnlyList<DeclaredNativeLibrary> CheckNativeLibraries(AbsolutePath project, string rid)
    {
        var json = RunChecked("dotnet", ["msbuild", project, "-getItem:NativeLibrary", $"-p:RuntimeIdentifier={rid}", "-p:PublishAot=true",
            $"-p:{TargetSettings.RidSpecificPublishProperty}=true", "-nologo"], null, RootDirectory, $"the native libraries of {project.Name}");
        var linked = new List<string>();
        using (var document = System.Text.Json.JsonDocument.Parse(json))
            if (document.RootElement.TryGetProperty("Items", out var items) && items.TryGetProperty("NativeLibrary", out var libraries))
                foreach (var library in libraries.EnumerateArray())
                    linked.Add(library.GetProperty("FullPath").GetString()!);

        var declared = ThirdPartyNotices.ReadDeclaredNativeLibraries(NativeLibraryDeclarations);
        var (matched, problems) = ThirdPartyNotices.MatchNativeLibraries(linked, RootDirectory, declared);
        if (problems.Count > 0)
            throw new Exception($"The native libraries of {project.Name} are not all covered by notices:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
        Serilog.Log.Information("{Project}: {Count} native libraries added to the link{Names}", project.Name, linked.Count,
            linked.Count == 0 ? "" : $": {string.Join(", ", linked.Select(Path.GetFileName))}");
        return matched;
    }

    /// <summary>
    /// The <c>embedded-resource</c> declarations in <see cref="NativeLibraryDeclarations"/> whose file a linked assembly
    /// embeds, by name and SHA-256. The assemblies are those of the ILC response file, so a resource of a referenced project
    /// (such as Morphir.Extensions.Wasm) is seen as well as one of the executable's own. The check runs one way: an
    /// undeclared resource needs nothing, a declaration that matches nothing is not listed, and a declared name embedded
    /// with another SHA-256 fails the build.
    /// </summary>
    IReadOnlyList<DeclaredNativeLibrary> CheckEmbeddedResources(IReadOnlyList<string> linkedAssemblies)
    {
        var (matched, problems) = ThirdPartyNotices.MatchEmbeddedResources(ThirdPartyNotices.EmbeddedResources(linkedAssemblies),
            ThirdPartyNotices.ReadDeclaredNativeLibraries(NativeLibraryDeclarations));
        if (problems.Count > 0)
            throw new Exception($"Embedded resources do not match their declarations:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
        Serilog.Log.Information("{Count} declared embedded resources are linked{Names}", matched.Count,
            matched.Count == 0 ? "" : $": {string.Join(", ", matched.Select(native => native.Name))}");
        return matched;
    }

    /// <summary>Copies <paramref name="executable"/> into <paramref name="folder"/> as <paramref name="launchName"/>, executable, and returns its path.</summary>
    static AbsolutePath CopyForCheck(AbsolutePath executable, AbsolutePath folder, string launchName)
    {
        Directory.CreateDirectory(folder);
        var launch = folder / launchName;
        File.Copy(executable, launch, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(launch, ReleaseArchive.ExecutableMode);
        return launch;
    }

    /// <summary>The glibc versions and the shared libraries a Linux executable needs, from <c>readelf</c>.</summary>
    static LinuxNeeds ReadLinuxNeeds(string executable, string workingDirectory)
    {
        var versionInfo = RunChecked("readelf", ["--version-info", "--wide", executable], null, workingDirectory, "readelf --version-info");
        var dynamic = RunChecked("readelf", ["--dynamic", "--wide", executable], null, workingDirectory, "readelf --dynamic");
        return new LinuxNeeds(NativeCheck.HighestGlibc(versionInfo), NativeCheck.NeededLibraries(dynamic), versionInfo);
    }

    /// <summary>
    /// A clean image from <paramref name="pinned"/>: OpenSSL purged in a derived image named <paramref name="tag"/>
    /// (ubuntu:22.04 ships libssl3), then what the image has of .NET, libicu, libssl and libcrypto (empty when clean), and
    /// its glibc. The caller decides whether leftovers fail it.
    /// </summary>
    static CleanImage PrepareCleanImage(string pinned, string tag, string workingDirectory, string? setup = null)
    {
        // An optional setup runs first in the same container, for example installing libicu; its output is not kept.
        var container = tag.Replace(':', '-');
        var script = setup is null ? NativeCheck.OpenSslPurgeScript : $"{{ {setup}; }} > /dev/null 2>&1\n{NativeCheck.OpenSslPurgeScript}";
        // A setup such as apt-get update and install depends on the distribution's mirror, which can take minutes on a
        // slow day; the purge alone takes seconds. A failed or timed-out preparation removes its container, because
        // killing the docker client does not stop the container it started.
        var timeout = setup is null ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(15);
        string removed;
        try
        {
            removed = RunChecked("docker", ["run", "--name", container, pinned, "sh", "-c", script], null, workingDirectory,
                $"preparing {pinned}", timeout).Trim();
            RunChecked("docker", ["commit", container, tag], null, workingDirectory, $"committing {tag}");
        }
        catch
        {
            RunCaptured("docker", ["rm", "--force", container], null, workingDirectory, TimeSpan.FromMinutes(1));
            throw;
        }

        RunChecked("docker", ["rm", container], null, workingDirectory, $"removing the container {container}");
        var leftovers = RunChecked("docker", ["run", "--rm", tag, "sh", "-c", NativeCheck.CleanImageListing], null, workingDirectory,
            $"the check of the image {tag}").Trim();
        var glibc = RunChecked("docker", ["run", "--rm", tag, "sh", "-c", "ldd --version | head -n 1"], null, workingDirectory, $"glibc of {tag}").Trim();
        Serilog.Log.Information("{Image}: {Removed}", pinned, removed.Length > 0 ? $"removed {removed} (dpkg --purge --force-depends) in {tag}" : "ships no OpenSSL package");
        return new CleanImage(pinned, tag, removed, leftovers, glibc);
    }

    /// <summary>
    /// Runs each command on this machine with the check environment (see <see cref="NativeCheck.CheckEnvironment"/>)
    /// plus the command's own variables, from <paramref name="folder"/> or the command's scratch folder, and records the
    /// outcome. Never throws for a command's outcome; the caller decides what fails.
    /// </summary>
    static IReadOnlyList<CheckOutcome> RunOnRunner(AbsolutePath launch, AbsolutePath folder, AbsolutePath inputs, AbsolutePath scratchRoot,
        IEnumerable<CheckCommand> commands, string label = "runner", IReadOnlyDictionary<string, string>? extra = null)
    {
        var inherited = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value);
        var outcomes = new List<CheckOutcome>();
        foreach (var command in commands.Where(command => !command.UnixOnly || !OperatingSystem.IsWindows()))
        {
            var scratch = Scratch(scratchRoot, label, command, inputs);
            var environment = new Dictionary<string, string>(NativeCheck.CheckEnvironment(inherited, OperatingSystem.IsWindows()), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var (key, value) in (extra ?? new Dictionary<string, string>()).Concat(command.Environment))
                environment[key] = value.Replace("{scratch}", scratch, StringComparison.Ordinal);
            var (exit, stdout, stderr, timedOut) = RunCaptured(launch, [.. command.Expand(inputs, scratch, Path.DirectorySeparatorChar)], environment,
                command.InScratch ? scratch : folder, TimeSpan.FromMinutes(5));
            outcomes.Add(NativeCheck.Outcome(label, command, exit, timedOut, stdout, stderr));
        }

        return outcomes;
    }

    /// <summary>
    /// Runs each command in <paramref name="image"/> with no network: the check folder is mounted read-only at
    /// <c>/check</c> and the command's scratch folder read-write at <c>/scratch</c>.
    /// </summary>
    static IReadOnlyList<CheckOutcome> RunInImage(CleanImage image, AbsolutePath folder, string launchName, AbsolutePath inputs,
        AbsolutePath scratchRoot, IEnumerable<CheckCommand> commands, string label, IReadOnlyDictionary<string, string>? extra = null)
    {
        const string mounted = "/check";
        var mountedInputs = $"{mounted}/{Path.GetRelativePath(folder, inputs).Replace('\\', '/')}";
        var outcomes = new List<CheckOutcome>();
        foreach (var command in commands)
        {
            var scratch = Scratch(scratchRoot, label, command, inputs);
            List<string> docker = ["--network", "none", "--volume", $"{folder}:{mounted}:ro", "--volume", $"{scratch}:/scratch",
                "--workdir", command.InScratch ? "/scratch" : mounted];
            foreach (var (key, value) in (extra ?? new Dictionary<string, string>()).Concat(command.Environment))
                docker.AddRange(["--env", $"{key}={value.Replace("{scratch}", "/scratch", StringComparison.Ordinal)}"]);
            docker.AddRange([image.Image, $"{mounted}/{launchName}", .. command.Expand(mountedInputs, "/scratch")]);
            var (exit, stdout, stderr, timedOut) = NativeCheck.RunContainer(docker,
                arguments => RunCaptured("docker", arguments, null, folder, TimeSpan.FromMinutes(5)),
                arguments => RunChecked("docker", arguments, null, folder, $"removing the check container {arguments[2]}", TimeSpan.FromMinutes(1)));
            outcomes.Add(NativeCheck.Outcome(label, command, exit, timedOut, stdout, stderr));
        }

        return outcomes;
    }

    /// <summary>A fresh scratch folder of one command in one environment, with the command's scratch files copied in.</summary>
    static string Scratch(AbsolutePath scratchRoot, string label, CheckCommand command, AbsolutePath inputs)
    {
        var scratch = ScratchPath(scratchRoot, label, command.Name);
        scratch.CreateOrCleanDirectory();
        foreach (var file in command.ScratchFiles)
        {
            File.Copy(inputs / file, scratch / file);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(scratch / file, File.GetUnixFileMode(inputs / file));
        }

        return scratch;
    }

    /// <summary>The scratch folder of one command in one environment.</summary>
    static AbsolutePath ScratchPath(AbsolutePath scratchRoot, string label, string command) =>
        scratchRoot / string.Concat(label.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '-')) / command;

    /// <summary>
    /// Runs a command with an explicit environment (or the inherited one when null), no shell and no window, kills its
    /// process tree after the timeout, and returns its exit code and output. Never throws for the exit code.
    /// </summary>
    static (int ExitCode, string Stdout, string Stderr, bool TimedOut) RunCaptured(string command, string[] arguments,
        IReadOnlyDictionary<string, string>? environment, string workingDirectory, TimeSpan timeout)
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

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException($"could not start {command}");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, "", $"could not start {command}: {error.Message}", false);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return (-1, stdout.Result, stderr.Result, true);
            }

            return (process.ExitCode, stdout.Result, stderr.Result, false);
        }
    }

    /// <summary>
    /// Runs a command with an explicit environment (or the inherited one when null), no shell and no window, kills its
    /// process tree after the timeout (five minutes by default), and returns stdout. Fails with the output on a non-zero exit code.
    /// </summary>
    static string RunChecked(string command, string[] arguments, IReadOnlyDictionary<string, string>? environment, string workingDirectory, string what,
        TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromMinutes(5);
        var (exit, stdout, stderr, timedOut) = RunCaptured(command, arguments, environment, workingDirectory, limit);
        if (timedOut)
            throw new Exception($"{what}: timed out after {limit.TotalMinutes:0} minutes.");
        if (exit != 0)
            throw new Exception($"{what}: exit code {exit}{Environment.NewLine}{stdout}{stderr}");
        return stdout;
    }
}
