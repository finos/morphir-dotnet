// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// One run of an install script: its result, the files in its install folder after the run, and the download it
/// started, from the line <c>morphirup-install: downloading &lt;file&gt; &lt;version&gt; from &lt;url&gt;</c>.
/// </summary>
public sealed record InstallScriptRun(CommandResult Result, IReadOnlyList<string> InstalledFiles)
{
    private static readonly Regex Downloading = new(
        @"^morphirup-install: downloading (?<file>morphirup-\S+) (?<version>\S+) from (?<url>https://\S+)$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex Space = new(@"\s+", RegexOptions.CultureInvariant);

    // PowerShell 7 wraps the message of an error in lines that start with "|".
    private static readonly Regex ErrorFrame = new(@"^\s*\|", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private Match Download => Downloading.Match(Result.StandardError.Replace("\r\n", "\n"));

    /// <summary>The release that the script chose, without a leading <c>v</c>, or null when it started no download.</summary>
    public string? ChosenVersion => Download is { Success: true } match ? match.Groups["version"].Value : null;

    /// <summary>The URL of the morphirup file that the script downloads, or null when it started no download.</summary>
    public string? DownloadUrl => Download is { Success: true } match ? match.Groups["url"].Value : null;

    /// <summary>
    /// Standard error with the frame of a wrapped PowerShell error removed and each run of white space as one space.
    /// PowerShell wraps the message of an error that a script throws, so a check of a message reads this text.
    /// </summary>
    public string Messages => Space.Replace(ErrorFrame.Replace(Result.StandardError.Replace("\r\n", "\n"), " "), " ");
}

/// <summary>
/// Runs an install script of morphirup, as a user runs it, on this machine: <c>install-morphirup.ps1</c> in
/// PowerShell on Windows, <c>install-morphirup.sh</c> in <c>sh</c> elsewhere. The script reads the release list of
/// the scenario through its testing hook <c>MORPHIRUP_RELEASES_FILE</c>, and each download goes to an HTTPS proxy
/// that refuses the connection, so no request leaves the machine and the script installs nothing.
/// </summary>
public interface IInstallScriptDriver
{
    /// <summary>The file name of the script that this machine runs.</summary>
    string ScriptName { get; }

    /// <summary>Runs <paramref name="script"/> with no version, with the release list <paramref name="releases"/>, into a new folder of the workspace.</summary>
    Task<InstallScriptRun> InstallNewestAsync(WorkspaceFile script, WorkspaceFile releases, CancellationToken cancellation = default);
}

/// <summary>The install script driver on the shell of this machine.</summary>
public sealed class InstallScriptDriver(IScenarioWorkspace workspace, ProcessGate? gate = null) : IInstallScriptDriver
{
    /// <summary>The time limit of a run. The script fails at its first download.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>A proxy on a local port on which nothing listens (the discard port), so each connection is refused.</summary>
    public const string RefusingProxy = "http://127.0.0.1:9";

    public string ScriptName => ScriptFor(OperatingSystem.IsWindows());

    /// <summary>The script of a Windows machine or of another machine.</summary>
    public static string ScriptFor(bool windows) => windows ? "install-morphirup.ps1" : "install-morphirup.sh";

    public async Task<InstallScriptRun> InstallNewestAsync(WorkspaceFile script, WorkspaceFile releases, CancellationToken cancellation = default)
    {
        var folder = workspace.Directory("installed");
        var windows = OperatingSystem.IsWindows();
        var info = StartInfo(windows, script.Path, folder.Path, windows ? PowerShell() : "sh");
        var options = new CliRunOptions { Environment = EnvironmentFor(releases.Path), Timeout = Timeout };
        var result = await ProductProcess.RunAsync(info, CliDriver.CommandLineOf(info), workspace, gate, options, Timeout, cancellation)
            .ConfigureAwait(false);
        return new InstallScriptRun(result, workspace.EntriesIn(folder));
    }

    /// <summary>The process of a run with no version: <paramref name="shell"/> runs <paramref name="script"/> into <paramref name="installDirectory"/>.</summary>
    public static ProcessStartInfo StartInfo(bool windows, string script, string installDirectory, string shell)
    {
        var info = new ProcessStartInfo(shell);
        string[] arguments = windows
            ? ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-InstallDir", installDirectory, "-NoModifyPath"]
            : [script, "--install-dir", installDirectory];
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    /// <summary>
    /// The variables of a run: the release list, the refusing proxy for both spellings that the tools read, and no
    /// other setting of the scripts or of the developer, so the run takes each default of the script.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> EnvironmentFor(string releases)
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in (string[])
                 ["MORPHIRUP_VERSION", "MORPHIRUP_INSTALL_DIR", "MORPHIRUP_DOWNLOADER", "MORPHIRUP_DOWNLOAD_URL", "MORPHIRUP_API_URL",
                  "MORPHIRUP_SHA256_FILE", "GITHUB_TOKEN", "ALL_PROXY", "NO_PROXY"])
            variables[name] = null;
        variables["MORPHIRUP_RELEASES_FILE"] = releases;
        variables["MORPHIRUP_NO_MODIFY_PATH"] = "1";
        variables["HTTPS_PROXY"] = RefusingProxy;
        // Linux and macOS have case-sensitive names, and wget reads only the lowercase ones.
        if (!OperatingSystem.IsWindows())
        {
            variables = new Dictionary<string, string?>(variables, StringComparer.Ordinal)
            {
                ["https_proxy"] = RefusingProxy, ["all_proxy"] = null, ["no_proxy"] = null,
            };
        }

        return variables;
    }

    // PowerShell 7 when it is on PATH, else Windows PowerShell, which each Windows has. Both run the script.
    private static string PowerShell() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim('"'), "pwsh.exe"))
            .FirstOrDefault(File.Exists) ?? "powershell.exe";
}
