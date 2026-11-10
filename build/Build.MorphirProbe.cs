// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Morphir.Build.Helpers;
using Nuke.Common;
using Nuke.Common.IO;

/// <summary>
/// A report-mode probe of the Native AOT <c>morphir</c>: what it needs from a machine, measured, before the legacy
/// <c>morphir-{rid}</c> names carry it. It publishes <c>src/Morphir</c> with the native-aot release profile (on Linux
/// inside the pinned ubuntu:22.04, as morphirup is), then runs a fixed command set, one command for each native
/// dependency found in the code, from a folder whose path has a space, on the runner and in each clean image. It records
/// each command's exit code and standard error and never stops at a failure. It changes no product behavior.
/// <code>
/// ./build.sh --target ProbeMorphirNative --rid linux-x64
/// </code>
/// Output: <c>artifacts/morphir-probe/{rid}/probe.md</c>, and each command's full output under <c>outputs/</c>.
/// </summary>
partial class Build
{
    AbsolutePath MorphirProbeDirectory => RootDirectory / "artifacts" / "morphir-probe" / Rid!;

    /// <summary>The command set of the probe: one command for each native dependency that the code can reach.</summary>
    static IReadOnlyList<CheckCommand> MorphirProbeCommands { get; } =
    [
        new("version", ["startup", "ICU at startup (no invariant globalization)"], ["--version"]) { Expect = CheckExpectation.Report },
        new("extension-list", ["registry"], ["extension", "list", "--json"]) { Expect = CheckExpectation.Report },
        new("generate-csharp", ["C# generator: names upper-cased with ToUpperInvariant; CurrentCulture title-casing (Morphir.Models Name.toTitleCase) in display text", "lstat (libSystem.Native SystemNative_LStat, model capture)"],
            ["generate", "--input", "{inputs}/daily-rate-v3.json", "--target", "csharp", "--output", "{scratch}/out", "--json"])
            { Expect = CheckExpectation.Report },
        new("generate-fsharp", ["F# generator: names capitalized with ToUpperInvariant; CurrentCulture title-casing (Morphir.Models Name.toTitleCase) in display text"],
            ["generate", "--input", "{inputs}/daily-rate-v3.json", "--target", "fsharp", "--output", "{scratch}/out", "--json"])
            { Expect = CheckExpectation.Report },
        new("validate", ["model reading and analyzers"], ["validate", "--input", "{inputs}/daily-rate-v3.json", "--json"])
            { Expect = CheckExpectation.Report },
        new("config-set", ["SHA-256 (configuration transaction, ConfigurationTransaction.Hash)"], ["config", "set", "custom.caption", "probe", "--json"])
            { Expect = CheckExpectation.Report, InScratch = true, ScratchFiles = ["morphir.toml"] },
        new("ion-tree", ["SHA-256 (Ion tree cut stem, TreeStems.digestOf)"],
            ["generate", "--input", "{inputs}/ion tree", "--input-format", "ion", "--target", "fsharp", "--output", "{scratch}/out", "--json"])
            { Expect = CheckExpectation.Report, Marker = "does not belong to the stem" },
        new("dist-install", ["HTTPS (HttpClient in the dist, tool and extension handlers), no network"],
            ["dist", "install", "https://example.invalid/morphir.tar.gz", "1.0.0"])
            { Expect = CheckExpectation.Report, NotReachedMarker = "not implemented" },
        new("compile-project-file", ["kill (libc, BoundedProcess stops a child that writes too much)"],
            // No --language and no [frontend] language: with either, compile refuses for want of a frontend before it
            // selects sources; without them it expands the project file through the MSBuild provider first.
            ["compile", "--project-file", "probe.fsproj", "--package-name", "acme/probe", "--json"])
        {
            Expect = CheckExpectation.Report, InScratch = true, UnixOnly = true, Marker = "was stopped",
            ScratchFiles = ["morphir.toml", "probe.fsproj", "fake-dotnet"],
            Environment = new Dictionary<string, string> { ["DOTNET_HOST_PATH"] = "{scratch}/fake-dotnet" },
        },
    ];

    Target ProbeMorphirNative => _ => _
        .Description("Report what the Native AOT morphir needs from a machine: publish for --rid, run its command set on the runner and in clean images")
        .Requires(() => Rid)
        .Executes(() =>
        {
            if (!TargetPlatform.TryParse(Rid, out var platform) || platform is null)
                throw new Exception($"--rid must be one of {string.Join(", ", TargetPlatform.All.Select(platform => platform.Rid))}; got '{Rid}'.");
            var rid = Rid!;
            var probe = MorphirProbeDirectory;
            probe.CreateOrCleanDirectory();

            // The native-aot release profile's publish of src/Morphir, as PublishRelease does it; on Linux in the pinned image.
            var publish = probe / "publish";
            string[] arguments =
            [
                "publish", RootDirectory / Deployment.NativeAot.Project, "-c", "Release", "-r", rid, "-o", publish, "-nologo", "-nodeReuse:false",
                .. TargetSettings.ForRidSpecificPublish(Deployment.NativeAot.BuildProperties).Select(property => $"-p:{property.Key}={property.Value}"),
            ];
            var linux = platform.OperatingSystem == "linux";
            var (exit, log) = linux ? PublishInContainer(NativeCheck.LinuxBuildImage, platform, arguments) : Capture("dotnet", arguments);
            File.WriteAllText(probe / "publish.log", log);
            if (exit != 0)
                throw new Exception($"The Native AOT publish of morphir for {rid} failed; see {probe / "publish.log"}.");
            var launchName = platform.OperatingSystem == "windows" ? "morphir.exe" : "morphir";
            var executable = publish / launchName;

            var folder = probe / "check" / $"{rid} with space";
            var launch = CopyForCheck(executable, folder, launchName);
            var inputs = folder / "probe inputs";
            WriteMorphirProbeInputs(inputs);
            var scratchRoot = probe / "scratch";

            var report = new StringBuilder();
            report.Append($"# Native AOT morphir probe: {rid}\n\n");
            report.Append($"Version {Version}, commit {GitCommit()}, published with the native-aot release profile");
            report.Append(linux ? $" inside `{NativeCheck.LinuxBuildImage}`" : " on the runner");
            report.Append($". Executable: {new FileInfo(executable).Length:N0} bytes. Runner: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.\n\n");
            report.Append("The WASM engine is not linked on this branch, so what `morphir` needs with the engine is **not measured** here (see #570).\n\n");

            if (linux)
            {
                var needs = ReadLinuxNeeds(executable, probe);
                report.Append("## What the executable needs from a Linux machine\n\n");
                report.Append(needs.HighestGlibc is { } glibc
                    ? $"- Highest glibc version: `GLIBC_{glibc.Version}` from `{glibc.File}` (floor `GLIBC_{NativeCheck.GlibcFloor}`: {(NativeCheck.IsAboveFloor(glibc.Version) ? "**above**" : "within")}).\n"
                    : "- No glibc version needed.\n");
                report.Append($"- Shared libraries (`NEEDED`): {string.Join(", ", needs.Libraries.Select(library => $"`{library}`"))}.\n");
                report.Append("- Loaded at run time and not in `NEEDED` (by .NET): ICU (`libicu*`) unless invariant globalization is on, and OpenSSL (`libssl`, `libcrypto`) at the first digest, random number or TLS call. The table shows which commands reach them.\n\n");
            }
            else if (platform.OperatingSystem == "macos")
            {
                var minimum = NativeCheck.MacOsMinimum(RunChecked("otool", ["-l", executable], null, probe, "otool -l"));
                report.Append($"## What the executable needs from a Mac\n\n- `LC_BUILD_VERSION` declares the minimum macOS {minimum ?? "(none)"}.\n\n");
            }

            // The runner as it is (ICU where the platform has it), the runner in invariant globalization mode, and on Linux
            // and macOS the runner under the Turkish culture, whose casing of i differs from the invariant culture's.
            var notPrepared = new List<(string Environment, string Reason)>();
            var outcomes = new List<CheckOutcome>(RunOnRunner(launch, folder, inputs, scratchRoot, MorphirProbeCommands));
            outcomes.AddRange(RunOnRunner(launch, folder, inputs, scratchRoot, MorphirProbeCommands, "runner, invariant", InvariantVariable));
            if (!OperatingSystem.IsWindows())
                outcomes.AddRange(RunOnRunner(launch, folder, inputs, scratchRoot, MorphirProbeCommands, "runner, tr-TR", TurkishCulture));

            if (linux)
            {
                report.Append("## Clean images\n\n");
                var variableIgnored = false;
                var variableMeasured = false;
                foreach (var (pinned, index) in NativeCheck.CleanImages.Select((image, index) => (image, index)))
                {
                    var label = pinned.Split('@')[0];
                    var image = TryPrepare(pinned, $"morphir-probe-{rid}:{index}", probe, [label, $"{label}, invariant"], notPrepared);
                    if (image is null)
                    {
                        report.Append($"### `{label}`\n\n- not prepared: {notPrepared[^1].Reason}\n\n");
                    }
                    else
                    {
                        var (_, ldd, lddError, _) = RunCaptured("docker", ["run", "--rm", "--volume", $"{folder}:/check:ro", image.Image, "ldd", $"/check/{launchName}"],
                            null, probe, TimeSpan.FromMinutes(2));
                        report.Append($"### `{label}`\n\n- {image.Glibc}\n- OpenSSL removed: {(image.Removed.Length > 0 ? image.Removed : "none shipped")}\n");
                        report.Append($"- Left of .NET, libicu, libssl, libcrypto: {(image.Leftovers.Length > 0 ? image.Leftovers.Replace('\n', ' ') : "nothing")}\n");
                        report.Append($"- `ldd`:\n\n```text\n{(ldd + lddError).Trim()}\n```\n\n");
                        outcomes.AddRange(RunInImage(image, folder, launchName, inputs, scratchRoot, MorphirProbeCommands, label));
                        var underVariable = RunInImage(image, folder, launchName, inputs, scratchRoot, MorphirProbeCommands, $"{label}, invariant", InvariantVariable);
                        outcomes.AddRange(underVariable);
                        variableMeasured = true;
                        variableIgnored |= underVariable.All(outcome => outcome.Stderr.Contains(IcuMissing, StringComparison.Ordinal));
                        RunChecked("docker", ["rmi", image.Image], null, probe, $"removing the image {image.Image}");
                    }

                    // The same image with libicu installed and still no OpenSSL, so the two prerequisites are seen apart.
                    var withIcu = TryPrepare(pinned, $"morphir-probe-icu-{rid}:{index}", probe, [$"{label} + libicu"], notPrepared, IcuInstall);
                    if (withIcu is null)
                    {
                        report.Append($"### `{label} + libicu`\n\n- not prepared: {notPrepared[^1].Reason}\n\n");
                        continue;
                    }

                    report.Append($"### `{label} + libicu`\n\n- libicu installed with apt-get, OpenSSL removed: {(withIcu.Removed.Length > 0 ? withIcu.Removed : "none shipped")}\n");
                    report.Append($"- Left of .NET, libicu, libssl, libcrypto (libicu is expected here): {(withIcu.Leftovers.Length > 0 ? withIcu.Leftovers.Replace('\n', ' ') : "nothing: libicu did not install")}\n\n");
                    outcomes.AddRange(RunInImage(withIcu, folder, launchName, inputs, scratchRoot, MorphirProbeCommands, $"{label} + libicu"));
                    RunChecked("docker", ["rmi", withIcu.Image], null, probe, $"removing the image {withIcu.Image}");
                }

                // If the variable did not take effect, the switch is fixed at build time: publish the probe's own invariant
                // executable (InvariantGlobalization=true, for the probe only, not the release profile) and run that.
                report.Append(!variableMeasured
                    ? "Whether `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` takes effect is **not measured**: no clean image was prepared.\n\n"
                    : variableIgnored
                    ? "`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` did **not** take effect in the clean images, so the probe also ran an executable published with `InvariantGlobalization=true`.\n\n"
                    : "`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` took effect: under it, no command stopped for want of ICU.\n\n");
                if (variableIgnored)
                {
                    var invariantPublish = probe / "publish-invariant";
                    string[] invariantArguments =
                        [.. arguments.Select(argument => argument == publish.ToString() ? invariantPublish.ToString() : argument), "-p:InvariantGlobalization=true"];
                    var (invariantExit, invariantLog) = PublishInContainer(NativeCheck.LinuxBuildImage, platform, invariantArguments);
                    File.WriteAllText(probe / "publish-invariant.log", invariantLog);
                    if (invariantExit != 0)
                        throw new Exception($"The invariant publish of morphir for {rid} failed; see {probe / "publish-invariant.log"}.");
                    var invariantFolder = probe / "check" / $"{rid} invariant build";
                    CopyForCheck(invariantPublish / launchName, invariantFolder, launchName);
                    WriteMorphirProbeInputs(invariantFolder / "probe inputs");
                    foreach (var (pinned, index) in NativeCheck.CleanImages.Select((image, index) => (image, index)))
                    {
                        var invariantLabel = $"{pinned.Split('@')[0]}, InvariantGlobalization build";
                        var image = TryPrepare(pinned, $"morphir-probe-invariant-{rid}:{index}", probe, [invariantLabel], notPrepared);
                        if (image is null) continue;
                        outcomes.AddRange(RunInImage(image, invariantFolder, launchName, invariantFolder / "probe inputs", scratchRoot, MorphirProbeCommands,
                            invariantLabel));
                        RunChecked("docker", ["rmi", image.Image], null, probe, $"removing the image {image.Image}");
                    }
                }
            }

            report.Append(CompareGeneratedFiles(outcomes, scratchRoot));

            report.Append("## Commands\n\nReport mode: no outcome fails the probe. *Path ran* is `yes` when the command succeeded or printed the marker of its path, `no` when it printed that the path is not implemented, and `unknown` otherwise.\n\n");
            if (notPrepared.Count > 0)
                report.Append($"Environments not prepared, so not run: {string.Join("; ", notPrepared.Select(entry => $"`{entry.Environment}` ({entry.Reason})"))}.\n\n");
            report.Append(NativeCheck.Table(outcomes));
            foreach (var (environment, reason) in notPrepared)
                report.Append($"| {environment} | (every command) | image preparation | not prepared | no | {reason.Replace("|", "\\|")} |\n");
            var outputs = probe / "outputs";
            foreach (var outcome in outcomes)
            {
                var file = outputs / string.Concat(outcome.Environment.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '-')) / $"{outcome.Command.Name}.txt";
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, $"exit {NativeCheck.ExitText(outcome.ExitCode)}{(outcome.TimedOut ? " (timed out)" : "")}\n--- stdout\n{outcome.Stdout}\n--- stderr\n{outcome.Stderr}");
            }

            var sites = NativeCheck.OpenSslCallSites(MorphirSources());
            report.Append($"\n## Call sites that reach OpenSSL on Linux\n\n{sites.Count} lines in the projects that `src/Morphir` references, found by name (comments skipped):\n\n");
            report.Append("| File | Line | API | Code |\n| --- | --- | --- | --- |\n");
            foreach (var site in sites)
                report.Append($"| `{site.Path}` | {site.Line} | `{site.Api}` | `{site.Text.Replace("|", "\\|").Replace("`", "'")}` |\n");

            var text = report.ToString();
            File.WriteAllText(probe / "probe.md", text);
            Serilog.Log.Information("{Report}", text);
            if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
                File.AppendAllText(summary, text + "\n");
        });

    /// <summary>
    /// Prepares a clean image, or records why it could not be prepared against each environment that would have run in it
    /// and returns null. In report mode a failed preparation (a slow mirror, a missing image) is a finding, not a reason to
    /// stop the probe.
    /// </summary>
    static CleanImage? TryPrepare(string pinned, string tag, AbsolutePath probe, IReadOnlyList<string> environments,
        List<(string Environment, string Reason)> notPrepared, string? setup = null)
    {
        try
        {
            return PrepareCleanImage(pinned, tag, probe, setup);
        }
        catch (Exception error)
        {
            var reason = error.Message.Replace("\r\n", "\n").Split('\n')[0].Trim();
            foreach (var environment in environments)
                notPrepared.Add((environment, reason));
            Serilog.Log.Warning("Not prepared: {Image} ({Reason})", pinned, reason);
            return null;
        }
    }

    /// <summary>Invariant globalization mode, set at run time.</summary>
    static IReadOnlyDictionary<string, string> InvariantVariable { get; } =
        new Dictionary<string, string> { ["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1" };

    /// <summary>The Turkish culture, whose title-casing of <c>i</c> is <c>İ</c>, not <c>I</c>.</summary>
    static IReadOnlyDictionary<string, string> TurkishCulture { get; } =
        new Dictionary<string, string> { ["LANG"] = "tr_TR.UTF-8", ["LC_ALL"] = "tr_TR.UTF-8" };

    /// <summary>What .NET prints when it stops at startup for want of ICU.</summary>
    const string IcuMissing = "Couldn't find a valid ICU package";

    /// <summary>Installs the image's own libicu package (libicu72 in Debian 12, libicu70 in Ubuntu 22.04) with apt-get.</summary>
    const string IcuInstall =
        "export DEBIAN_FRONTEND=noninteractive; apt-get update -qq && "
        + "package=$(apt-cache search --names-only '^libicu[0-9]+$' | cut -d' ' -f1 | sort -V | tail -n 1) && "
        + "apt-get install -y -qq --no-install-recommends \"$package\"";

    /// <summary>The commands whose generated files are compared across environments, for what invariant mode changes.</summary>
    static readonly string[] GenerationCommands = ["generate-csharp", "generate-fsharp"];

    /// <summary>
    /// Compares, byte for byte, the files that each generation command wrote on the runner with ICU against the files it
    /// wrote in every other environment where it succeeded, and returns the Markdown section.
    /// </summary>
    static string CompareGeneratedFiles(IReadOnlyList<CheckOutcome> outcomes, AbsolutePath scratchRoot)
    {
        var text = new StringBuilder("\n## Generated files compared with the runner's\n\n");
        text.Append("Byte for byte, against the files the command wrote on the runner (ICU where the platform has it, the runner's culture):\n\n");
        text.Append("| Command | Environment | Result |\n| --- | --- | --- |\n");
        foreach (var command in GenerationCommands)
        {
            var baseline = outcomes.FirstOrDefault(outcome => outcome.Environment == "runner" && outcome.Command.Name == command && outcome.ExitCode == 0);
            if (baseline is null)
            {
                text.Append($"| {command} | runner | no baseline: the command did not succeed on the runner |\n");
                continue;
            }

            var reference = ScratchPath(scratchRoot, "runner", command) / "out";
            foreach (var other in outcomes.Where(outcome => outcome.Command.Name == command && outcome.Environment != "runner"))
            {
                if (other.ExitCode != 0)
                {
                    text.Append($"| {command} | {other.Environment} | not compared: exit {NativeCheck.ExitText(other.ExitCode)} |\n");
                    continue;
                }

                text.Append($"| {command} | {other.Environment} | {TreeDifference(reference, ScratchPath(scratchRoot, other.Environment, command) / "out")} |\n");
            }
        }

        return text.ToString();
    }

    /// <summary>"identical (n files)", or the files that are missing, extra or different.</summary>
    static string TreeDifference(AbsolutePath left, AbsolutePath right)
    {
        static Dictionary<string, string> Files(AbsolutePath root) =>
            Directory.Exists(root)
                ? Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .ToDictionary(file => Path.GetRelativePath(root, file).Replace('\\', '/'), file => ReleaseArchive.Sha256(file), StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        var a = Files(left);
        var b = Files(right);
        var differences = a.Keys.Union(b.Keys).Order(StringComparer.Ordinal)
            .Where(file => !a.TryGetValue(file, out var x) || !b.TryGetValue(file, out var y) || x != y)
            .ToList();
        return differences.Count == 0
            ? $"identical ({a.Count} files)"
            : $"**differ**: {string.Join(", ", differences.Select(file => $"`{file}`"))}";
    }

    /// <summary>
    /// The probe's inputs: the daily-rate model; a copy of the greeting Ion tree in which one type file states its name and
    /// has a cut stem, so that reading it computes the stem's SHA-256; a project configuration; a project file; and, for the
    /// kill path, a <c>dotnet</c> stand-in that writes more than the MSBuild provider's output budget.
    /// </summary>
    void WriteMorphirProbeInputs(AbsolutePath inputs)
    {
        inputs.CreateOrCleanDirectory();
        File.Copy(RootDirectory / "tests" / "Fixtures" / "MorphirIR" / "daily-rate-v3.json", inputs / "daily-rate-v3.json");

        // The daily-rate tree generates; its value file states its name and takes the cut stem daily__<digest>, which the
        // reader accepts only after it has computed the SHA-256 of the name and found it equal.
        var tree = inputs / "ion tree";
        var source = RootDirectory / "tests" / "Fixtures" / "MorphirIon" / "trees" / "v3-daily-rate";
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = tree / Path.GetRelativePath(source, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        var stated = Directory.GetFiles(tree, "daily-rate.value.ion", SearchOption.AllDirectories).Single();
        var lines = File.ReadAllLines(stated).ToList();
        lines.Insert(1, "  name: \"daily-rate\",");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("daily-rate")))[..8].ToLowerInvariant();
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(stated)!, $"daily__{digest}.value.ion"), lines);
        File.Delete(stated);

        // A project with no frontend language, so that compile reaches source selection (see compile-project-file).
        File.WriteAllText(inputs / "morphir.toml", "[project]\nname = \"acme/probe\"\n");
        File.WriteAllText(inputs / "probe.fsproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        File.WriteAllText(inputs / "fake-dotnet", "#!/bin/sh\nexec yes morphir-probe\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(inputs / "fake-dotnet", ReleaseArchive.ExecutableMode);
    }

    /// <summary>The C# and F# sources of <c>src/Morphir</c> and every project it references, by path relative to the repository.</summary>
    Dictionary<string, string> MorphirSources()
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([Path.GetFullPath(RootDirectory / Deployment.NativeAot.Project)]);
        while (pending.Count > 0)
        {
            var project = pending.Pop();
            if (!projects.Add(project)) continue;
            // A reference with ReferenceOutputAssembly="false" is a build-time task or generator, not linked code.
            foreach (Match reference in Regex.Matches(File.ReadAllText(project), @"<ProjectReference\s+Include=""([^""]+)""[^>]*>"))
                if (!reference.Value.Contains("ReferenceOutputAssembly=\"false\"", StringComparison.Ordinal))
                    pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))));
        }

        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in projects.Select(project => Path.GetDirectoryName(project)!))
            foreach (var file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                         .Where(file => file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".fs", StringComparison.Ordinal))
                         .Where(file => !Regex.IsMatch(file, @"[\\/](obj|bin)[\\/]")))
                sources[Path.GetRelativePath(RootDirectory, file).Replace('\\', '/')] = File.ReadAllText(file);
        return sources;
    }

    /// <summary>The commit this build is at, for the report.</summary>
    string GitCommit() => RunChecked("git", ["rev-parse", "--short", "HEAD"], null, RootDirectory, "git rev-parse").Trim();
}
