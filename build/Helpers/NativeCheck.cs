// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.RegularExpressions;

namespace Morphir.Build.Helpers;

/// <summary>What a command of a check expects: success, or only a recorded outcome (report mode).</summary>
public enum CheckExpectation
{
    /// <summary>The command must exit with code 0; anything else fails the check.</summary>
    Succeeds,

    /// <summary>The outcome is recorded and never fails the check.</summary>
    Report,
}

/// <summary>
/// One command that a check of a Native AOT executable runs, from a folder whose path has a space, on the runner and in
/// each clean image. Each executable supplies its own list, with one command for each native dependency it can reach,
/// because a check that runs only <c>--version</c> passes an executable that loads a library later (OpenSSL is loaded
/// at the first digest, not at startup). In <see cref="Arguments"/>, <c>{inputs}</c> is the read-only folder of the
/// check's input files and <c>{scratch}</c> a fresh writable folder of this command.
/// </summary>
/// <param name="Name">A short name for the table, for example <c>config-set</c>.</param>
/// <param name="Exercises">What the command exercises, for example <c>SHA-256 (configuration transaction)</c>.</param>
/// <param name="Arguments">The arguments after the executable.</param>
public sealed record CheckCommand(string Name, IReadOnlyList<string> Exercises, IReadOnlyList<string> Arguments)
{
    /// <summary>Success by default; <see cref="CheckExpectation.Report"/> for a probe.</summary>
    public CheckExpectation Expect { get; init; } = CheckExpectation.Succeeds;

    /// <summary>Text in stdout or stderr that shows the command's path ran, when success alone does not show it.</summary>
    public string? Marker { get; init; }

    /// <summary>Text in stdout or stderr that shows the path did not run, for example "not implemented".</summary>
    public string? NotReachedMarker { get; init; }

    /// <summary>Whether stdout must be the same on the runner and in every image.</summary>
    public bool SameOutputEverywhere { get; init; }

    /// <summary>Run in the command's scratch folder rather than the check folder.</summary>
    public bool InScratch { get; init; }

    /// <summary>Input files copied into the scratch folder first, by name.</summary>
    public IReadOnlyList<string> ScratchFiles { get; init; } = [];

    /// <summary>Variables added to the check environment; values may use <c>{scratch}</c>.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Only on Linux and macOS, for a dependency that Windows does not have.</summary>
    public bool UnixOnly { get; init; }

    /// <summary>
    /// The arguments with the two folders filled in; a <c>/</c> right after a folder token becomes
    /// <paramref name="separator"/>, so a path on Windows uses its own separator.
    /// </summary>
    public IReadOnlyList<string> Expand(string inputs, string scratch, char separator = '/') =>
        [.. Arguments.Select(argument => argument
            .Replace("{inputs}/", inputs + separator, StringComparison.Ordinal)
            .Replace("{scratch}/", scratch + separator, StringComparison.Ordinal)
            .Replace("{inputs}", inputs, StringComparison.Ordinal)
            .Replace("{scratch}", scratch, StringComparison.Ordinal))];
}

/// <summary>What one command did in one environment.</summary>
public sealed record CheckOutcome(
    string Environment, CheckCommand Command, int ExitCode, bool TimedOut, string Stdout, string Stderr, bool? Reached)
{
    /// <summary>Why a <see cref="CheckExpectation.Succeeds"/> command fails the check, or null.</summary>
    public string? Problem =>
        Command.Expect != CheckExpectation.Succeeds ? null
        : TimedOut ? $"{Command.Name} in {Environment}: timed out"
        : ExitCode != 0 ? $"{Command.Name} in {Environment}: exit code {ExitCode}\n{Stdout}{Stderr}"
        : null;
}

/// <summary>A source line that reaches OpenSSL on Linux: a .NET digest, random number, TLS or certificate API.</summary>
public sealed record OpenSslCallSite(string Path, int Line, string Api, string Text);

/// <summary>
/// The parts of a check of a Native AOT executable that do not depend on the executable: the glibc floor and the image
/// the Linux executables are linked in, what <c>readelf</c> says an executable needs, the clean images and the
/// environment the commands run in, and the table a report-mode probe writes.
/// </summary>
public static class NativeCheck
{
    /// <summary>
    /// The highest glibc the Linux executables may need: Ubuntu 22.04's. A Native AOT executable needs the glibc of the
    /// machine that linked it, or newer, so the Linux publish runs in <see cref="LinuxBuildImage"/>, and the check reads
    /// the versions the executable needs and refuses one above this floor.
    /// </summary>
    public static Version GlibcFloor { get; } = new(2, 35);

    /// <summary>
    /// The image the Linux executables are published in: Ubuntu 22.04 (glibc 2.35), pinned by the digest of its
    /// multi-architecture index (linux/amd64 and linux/arm64), so the floor moves only with a commit.
    /// </summary>
    public const string LinuxBuildImage = "ubuntu:22.04@sha256:076e7bf82319619f28dd0aa9c02ab34b055ad37100395b4f49a2ffba04fb5e11";

    /// <summary>
    /// The clean images a Linux executable runs in: Debian 12 (glibc 2.36), and the build image itself, the floor. Each
    /// is checked for no .NET, libicu, libssl or libcrypto, after OpenSSL is purged where the image ships it.
    /// </summary>
    public static IReadOnlyList<string> CleanImages { get; } = ["debian:12-slim", LinuxBuildImage];

    /// <summary>The script that purges OpenSSL from a container of an image and prints the packages it removed.</summary>
    public const string OpenSslPurgeScript = """
        removed=""
        for package in libssl3 libssl1.1; do
            if dpkg-query -W -f='${Status}' "$package" 2>/dev/null | grep -q 'install ok installed'; then
                dpkg --purge --force-depends "$package" > /dev/null 2>&1
                removed="$removed $package"
            fi
        done
        echo "$removed"
        """;

    /// <summary>The script that lists what a clean image must not have: .NET, libicu, libssl and libcrypto. Empty when clean.</summary>
    public const string CleanImageListing =
        "ls -d /usr/lib/*/libicu* /lib/*/libicu* /usr/lib/libicu* /usr/lib/*/libssl.so* /lib/*/libssl.so* /usr/lib/libssl.so* "
        + "/usr/lib/*/libcrypto.so* /lib/*/libcrypto.so* /usr/lib/libcrypto.so* /usr/share/dotnet /usr/lib/dotnet 2>/dev/null; command -v dotnet || true";

    /// <summary>
    /// The highest <c>GLIBC_x.y</c> version in the version needs section (<c>.gnu.version_r</c>) of
    /// <c>readelf --version-info --wide</c> output, with the library that defines it, or null when it needs none.
    /// </summary>
    public static (Version Version, string File)? HighestGlibc(string readelf)
    {
        (Version Version, string File)? highest = null;
        var inNeeds = false;
        var file = "";
        foreach (var line in readelf.Split('\n'))
        {
            if (line.StartsWith("Version ", StringComparison.Ordinal))
            {
                inNeeds = line.StartsWith("Version needs section", StringComparison.Ordinal);
                continue;
            }

            if (!inNeeds) continue;
            var fileMatch = Regex.Match(line, @"\bFile:\s+(\S+)");
            if (fileMatch.Success) file = fileMatch.Groups[1].Value;
            var name = Regex.Match(line, @"\bName:\s+GLIBC_(\d+(?:\.\d+)+)\s");
            if (name.Success && Version.TryParse(name.Groups[1].Value, out var version) && (highest is null || Normal(version) > Normal(highest.Value.Version)))
                highest = (version, file);
        }

        return highest;
    }

    /// <summary>Whether a needed glibc version is above <see cref="GlibcFloor"/>.</summary>
    public static bool IsAboveFloor(Version needed) => Normal(needed) > Normal(GlibcFloor);

    /// <summary>
    /// Every shared library that <c>readelf --dynamic --wide</c> output lists as <c>NEEDED</c>, in its order. A check
    /// reports each by name, so that a new one (such as <c>libgcc_s.so.1</c> from a statically linked native library) is
    /// seen rather than assumed.
    /// </summary>
    public static IReadOnlyList<string> NeededLibraries(string readelfDynamic) =>
        [.. Regex.Matches(readelfDynamic, @"\(NEEDED\)\s+Shared library: \[([^\]]+)\]").Select(match => match.Groups[1].Value)];

    /// <summary>The <c>minos</c> of the <c>LC_BUILD_VERSION</c> load command in <c>otool -l</c> output, or null.</summary>
    public static string? MacOsMinimum(string otool)
    {
        var inBuildVersion = false;
        foreach (var line in otool.Split('\n').Select(line => line.Trim()))
        {
            if (line.StartsWith("cmd ", StringComparison.Ordinal))
                inBuildVersion = line == "cmd LC_BUILD_VERSION";
            else if (inBuildVersion && line.StartsWith("minos ", StringComparison.Ordinal))
                return line["minos ".Length..].Trim();
        }

        return null;
    }

    /// <summary>
    /// The environment of an artifact check: <c>PATH</c> reduced to the system folders, no <c>DOTNET_*</c> variable, and
    /// only the variables that any process needs (on Windows <c>SystemRoot</c>, <c>windir</c>, <c>TEMP</c> and
    /// <c>TMP</c>; elsewhere <c>HOME</c>, <c>TMPDIR</c> and <c>LANG</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> CheckEnvironment(IReadOnlyDictionary<string, string?> inherited, bool windows)
    {
        string[] kept = windows ? ["SystemRoot", "windir", "TEMP", "TMP", "SystemDrive", "ComSpec"] : ["HOME", "TMPDIR", "LANG"];
        var comparer = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var environment = new Dictionary<string, string>(comparer);
        foreach (var (key, value) in inherited)
            if (value is not null && kept.Contains(key, comparer))
                environment[key] = value;

        if (windows)
        {
            var systemRoot = environment.TryGetValue("SystemRoot", out var root) ? root : @"C:\Windows";
            environment["PATH"] = $@"{systemRoot}\System32;{systemRoot}";
        }
        else
        {
            environment["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin";
        }

        return environment;
    }

    /// <summary>
    /// The outcome of one command: whether its path ran is <c>no</c> when <see cref="CheckCommand.NotReachedMarker"/>
    /// appears, <c>yes</c> when <see cref="CheckCommand.Marker"/> appears or the command succeeded, and unknown otherwise.
    /// </summary>
    public static CheckOutcome Outcome(string environment, CheckCommand command, int exitCode, bool timedOut, string stdout, string stderr)
    {
        bool Shows(string? text) => text is not null && (stdout.Contains(text, StringComparison.Ordinal) || stderr.Contains(text, StringComparison.Ordinal));
        bool? reached = Shows(command.NotReachedMarker) ? false
            : Shows(command.Marker) || (exitCode == 0 && !timedOut) ? true
            : null;
        return new CheckOutcome(environment, command, exitCode, timedOut, stdout, stderr, reached);
    }

    /// <summary>An exit code, with the signal's name when a Unix shell reports a signal as 128 plus its number.</summary>
    public static string ExitText(int exitCode)
    {
        string? signal = (exitCode - 128) switch
        {
            4 => "SIGILL", 5 => "SIGTRAP", 6 => "SIGABRT", 7 => "SIGBUS", 8 => "SIGFPE", 9 => "SIGKILL", 11 => "SIGSEGV", 15 => "SIGTERM",
            _ => null,
        };
        return signal is null ? $"{exitCode}" : $"{exitCode} ({signal})";
    }

    /// <summary>A Markdown table of the outcomes: environment, command, what it exercises, exit code, whether its path ran, and the first line of stderr.</summary>
    public static string Table(IEnumerable<CheckOutcome> outcomes)
    {
        var text = new StringBuilder();
        text.Append("| Environment | Command | Exercises | Exit | Path ran | First line of stderr |\n");
        text.Append("| --- | --- | --- | --- | --- | --- |\n");
        foreach (var outcome in outcomes)
        {
            var exit = outcome.TimedOut ? "timed out" : ExitText(outcome.ExitCode);
            var reached = outcome.Reached switch { true => "yes", false => "no", null => "unknown" };
            text.Append($"| {Cell(outcome.Environment)} | {Cell(outcome.Command.Name)} | {Cell(string.Join("; ", outcome.Command.Exercises))} | {exit} | {reached} | {Cell(FirstLine(outcome.Stderr))} |\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// Every source line that calls a .NET API which reaches OpenSSL on Linux (digests, HMAC, random numbers, HTTPS, TLS,
    /// certificates, RSA, ECDSA and AES), sorted by path and line. Comment lines are skipped. A probe lists them all,
    /// because whether the executable may need OpenSSL is decided from the whole list.
    /// </summary>
    public static IReadOnlyList<OpenSslCallSite> OpenSslCallSites(IReadOnlyDictionary<string, string> sources)
    {
        var api = new Regex(@"\b(SHA1|SHA256|SHA384|SHA512|MD5|HMACSHA\d+|IncrementalHash|RandomNumberGenerator|HttpClient|SslStream|X509Certificate2?|ECDsa|RSA|Aes)\b");
        var sites = new List<OpenSslCallSite>();
        foreach (var (path, text) in sources.OrderBy(source => source.Key, StringComparer.Ordinal))
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("(*", StringComparison.Ordinal) || line.StartsWith('*'))
                    continue;
                var match = api.Match(line);
                if (match.Success)
                    sites.Add(new OpenSslCallSite(path, index + 1, match.Groups[1].Value, line));
            }
        }

        return sites;
    }

    private static string FirstLine(string text)
    {
        // Serilog's information, debug and verbose lines on standard error are progress, not the error.
        var plain = Regex.Replace(text, @"\x1b\[[0-9;]*[A-Za-z]", "");
        var lines = plain.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        var line = lines.FirstOrDefault(line => !Regex.IsMatch(line, @"^\[\d\d:\d\d:\d\d (VRB|DBG|INF)\]")) ?? "";
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);

    private static Version Normal(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
