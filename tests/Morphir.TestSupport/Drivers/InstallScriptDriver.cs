// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// One run of an install script: its result, the files in its install folder after the run, and the download it
/// started, from the line <c>morphirup-install: downloading &lt;file&gt; &lt;version&gt; from &lt;url&gt;</c>.
/// </summary>
public sealed record InstallScriptRun(CommandResult Result, IReadOnlyList<string> InstalledFiles)
{
    private static readonly Regex Downloading = new(
        @"^morphirup-install: downloading (?<file>morphirup-\S+) (?<version>\S+) from (?<url>https?://\S+)$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex Space = new(@"\s+", RegexOptions.CultureInvariant);

    // PowerShell 7 wraps the message of an error in lines that start with "|".
    private static readonly Regex ErrorFrame = new(@"^\s*\|", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    // An ANSI escape sequence (CSI): PowerShell 7 colors an error record, also when standard error is redirected.
    private static readonly Regex Escape = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.CultureInvariant);

    private Match Download => Downloading.Match(Result.StandardError.Replace("\r\n", "\n"));

    /// <summary>The release that the script chose, without a leading <c>v</c>, or null when it started no download.</summary>
    public string? ChosenVersion => Download is { Success: true } match ? match.Groups["version"].Value : null;

    /// <summary>The URL of the morphirup file that the script downloads, or null when it started no download.</summary>
    public string? DownloadUrl => Download is { Success: true } match ? match.Groups["url"].Value : null;

    /// <summary>
    /// Standard error as one line of text: with no ANSI escape sequence, no frame of a wrapped PowerShell error, and
    /// each run of white space as one space. PowerShell 7 renders a thrown refusal as a colored error record and wraps
    /// its message in lines that start with <c>|</c>, so a check of a message reads this text. The <c>sh</c> script
    /// prints its refusals itself, so its text is unchanged here.
    /// </summary>
    public string Messages => Normalize(Result.StandardError);

    /// <summary>The text of <see cref="Messages"/> for <paramref name="standardError"/>.</summary>
    public static string Normalize(string standardError) =>
        Space.Replace(ErrorFrame.Replace(Escape.Replace(standardError, "").Replace("\r\n", "\n"), " "), " ");
}

/// <summary>
/// What one run of an install script reads. A run with no <see cref="Releases"/> cannot read the release list: the
/// GitHub API is behind the refusing proxy, so the script falls back to its release feed.
/// </summary>
public sealed record InstallScriptInputs
{
    /// <summary>The version to install (<c>--version</c> / <c>-Version</c>), or null for the newest.</summary>
    public string? Version { get; init; }

    /// <summary>The release list in the form of the GitHub API (<c>MORPHIRUP_RELEASES_FILE</c>).</summary>
    public WorkspaceFile? Releases { get; init; }

    /// <summary>The release feed in the form of <c>releases.atom</c> (<c>MORPHIRUP_FEED_FILE</c>).</summary>
    public WorkspaceFile? Feed { get; init; }

    /// <summary>The mirror of the release downloads (<c>MORPHIRUP_DOWNLOAD_URL</c>). With none, each download goes to the refusing proxy.</summary>
    public ReleaseMirror? Mirror { get; init; }
}

/// <summary>
/// A mirror of release downloads on a local port, laid out as <c>&lt;url&gt;/v&lt;version&gt;/&lt;file&gt;</c>. It
/// answers 200 for its files and 404 for any other path, and records each request. On Windows it serves HTTP; on
/// another machine it serves HTTPS with a certificate made for the test, which the run trusts through
/// <c>CURL_CA_BUNDLE</c>, because the <c>sh</c> script accepts HTTPS only.
/// </summary>
public sealed class ReleaseMirror : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IReadOnlyDictionary<string, byte[]> _files;
    private readonly X509Certificate2? _certificate;
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;

    private ReleaseMirror(IReadOnlyDictionary<string, byte[]> files, X509Certificate2? certificate, string? certificateFile)
    {
        _files = files;
        _certificate = certificate;
        CertificateFile = certificateFile;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Url = $"{(certificate is null ? "http" : "https")}://127.0.0.1:{port}";
        _accepting = AcceptAsync();
    }

    /// <summary>The base URL, with no final <c>/</c>.</summary>
    public string Url { get; }

    /// <summary>The PEM file of the certificate to trust, or null for an HTTP mirror.</summary>
    public string? CertificateFile { get; }

    /// <summary>Each request so far, as <c>&lt;method&gt; &lt;path&gt;</c>, with the path relative to <see cref="Url"/>.</summary>
    public IReadOnlyList<string> Requests => [.. _requests];

    /// <summary>
    /// The files of a release that has <c>morphirup</c> for each of the five platforms: a small shell script that
    /// prints the version for each executable, and <c>morphirup-v&lt;version&gt;.sha256</c> with their digests.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> MorphirupRelease(string version)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var sums = new StringBuilder();
        foreach (var name in (string[])["morphirup-linux-arm64", "morphirup-linux-x64", "morphirup-osx-arm64", "morphirup-win-arm64.exe", "morphirup-win-x64.exe"])
        {
            var bytes = Encoding.UTF8.GetBytes($"#!/bin/sh\necho {version}\n");
            files[$"v{version}/{name}"] = bytes;
            sums.Append(Convert.ToHexStringLower(SHA256.HashData(bytes))).Append("  ").Append(name).Append('\n');
        }

        files[$"v{version}/morphirup-v{version}.sha256"] = Encoding.UTF8.GetBytes(sums.ToString());
        return files;
    }

    /// <summary>Starts a mirror of <paramref name="files"/>; with <paramref name="tls"/>, its certificate is written to <paramref name="certificateFile"/>.</summary>
    public static ReleaseMirror Start(IReadOnlyDictionary<string, byte[]> files, bool tls, string certificateFile)
    {
        if (!tls) return new ReleaseMirror(files, null, null);
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.KeyCertSign, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(2));
        File.WriteAllText(certificateFile, created.ExportCertificatePem());
        // A server certificate needs a key that the platform can use: on macOS, the one of an imported PKCS#12.
        var certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        return new ReleaseMirror(files, certificate, certificateFile);
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                Stream stream = client.GetStream();
                if (_certificate is not null)
                {
                    var tls = new SslStream(stream, leaveInnerStreamOpen: false);
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate }, _stop.Token)
                        .ConfigureAwait(false);
                    stream = tls;
                }

                await using (stream.ConfigureAwait(false))
                {
                    var buffer = new byte[16 * 1024];
                    var count = 0;
                    while (count < buffer.Length)
                    {
                        var read = await stream.ReadAsync(buffer.AsMemory(count), _stop.Token).ConfigureAwait(false);
                        if (read == 0) break;
                        count += read;
                        if (Encoding.ASCII.GetString(buffer, 0, count).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
                    }

                    var line = Encoding.ASCII.GetString(buffer, 0, count).Split("\r\n")[0].Split(' ');
                    if (line.Length < 2) return;
                    var path = Uri.UnescapeDataString(line[1].Split('?')[0].TrimStart('/'));
                    _requests.Enqueue($"{line[0]} {path}");
                    var found = _files.TryGetValue(path, out var body);
                    var head = $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Type: application/octet-stream\r\n" +
                               $"Content-Length: {(found ? body!.Length : 0)}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _stop.Token).ConfigureAwait(false);
                    if (found && line[0] != "HEAD") await stream.WriteAsync(body, _stop.Token).ConfigureAwait(false);
                    await stream.FlushAsync(_stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException
                                              or System.Security.Authentication.AuthenticationException or ObjectDisposedException)
            {
                // A client that leaves early, or a run that ends, is not a failure of the mirror.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _accepting.ConfigureAwait(false);
        _certificate?.Dispose();
        _stop.Dispose();
    }
}

/// <summary>
/// Runs an install script of morphirup, as a user runs it, on this machine: <c>install-morphirup.ps1</c> in
/// PowerShell on Windows, <c>install-morphirup.sh</c> in <c>sh</c> elsewhere. The script reads the release list or
/// the release feed of the scenario through its testing hooks, and downloads from the scenario's mirror. Any other
/// request goes to an HTTPS proxy that refuses the connection, so no request leaves the machine.
/// </summary>
public interface IInstallScriptDriver
{
    /// <summary>The file name of the script that this machine runs.</summary>
    string ScriptName { get; }

    /// <summary>Starts a mirror with the files of a <c>morphirup</c> release for each of <paramref name="versions"/>. The end of the test stops it.</summary>
    ReleaseMirror StartMirror(IReadOnlyCollection<string> versions);

    /// <summary>Runs <paramref name="script"/> with <paramref name="inputs"/>, into a new folder of the workspace.</summary>
    Task<InstallScriptRun> InstallAsync(WorkspaceFile script, InstallScriptInputs inputs, CancellationToken cancellation = default);
}

/// <summary>The install script driver on the shell of this machine.</summary>
public sealed class InstallScriptDriver(IScenarioWorkspace workspace, ProcessGate? gate = null) : IInstallScriptDriver, IAsyncDisposable
{
    private readonly List<ReleaseMirror> _mirrors = [];

    /// <summary>The time limit of a run.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>A proxy on a local port on which nothing listens (the discard port), so each connection is refused.</summary>
    public const string RefusingProxy = "http://127.0.0.1:9";

    public string ScriptName => ScriptFor(OperatingSystem.IsWindows());

    /// <summary>The script of a Windows machine or of another machine.</summary>
    public static string ScriptFor(bool windows) => windows ? "install-morphirup.ps1" : "install-morphirup.sh";

    public ReleaseMirror StartMirror(IReadOnlyCollection<string> versions)
    {
        var files = versions.SelectMany(version => ReleaseMirror.MorphirupRelease(version)).ToDictionary(StringComparer.Ordinal);
        var mirror = ReleaseMirror.Start(files, tls: !OperatingSystem.IsWindows(), workspace.File($"mirror-{_mirrors.Count}.pem").Path);
        lock (_mirrors) _mirrors.Add(mirror);
        return mirror;
    }

    public async Task<InstallScriptRun> InstallAsync(WorkspaceFile script, InstallScriptInputs inputs, CancellationToken cancellation = default)
    {
        var folder = workspace.Directory("installed");
        var windows = OperatingSystem.IsWindows();
        var info = StartInfo(windows, script.Path, folder.Path, windows ? PowerShell() : "sh", inputs.Version);
        var options = new CliRunOptions
        {
            Environment = EnvironmentFor(windows, inputs.Releases?.Path, inputs.Feed?.Path, inputs.Mirror?.Url, inputs.Mirror?.CertificateFile),
            Timeout = Timeout,
        };
        var result = await ProductProcess.RunAsync(info, CliDriver.CommandLineOf(info), workspace, gate, options, Timeout, cancellation)
            .ConfigureAwait(false);
        return new InstallScriptRun(result, workspace.EntriesIn(folder));
    }

    /// <summary>The process of a run: <paramref name="shell"/> runs <paramref name="script"/> into <paramref name="installDirectory"/>, for <paramref name="version"/> or the newest.</summary>
    public static ProcessStartInfo StartInfo(bool windows, string script, string installDirectory, string shell, string? version = null)
    {
        var info = new ProcessStartInfo(shell);
        List<string> arguments = windows
            ? ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-InstallDir", installDirectory, "-NoModifyPath"]
            : [script, "--install-dir", installDirectory];
        if (version is not null) arguments.AddRange(windows ? ["-Version", version] : ["--version", version]);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    /// <summary>
    /// The variables of a run: its inputs, the refusing proxy for both spellings that the tools read, with the local
    /// mirror outside it, and no other setting of the scripts or of the developer, so the run takes each other default.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> EnvironmentFor(
        bool windows, string? releases, string? feed = null, string? mirror = null, string? certificateFile = null)
    {
        var variables = new Dictionary<string, string?>(windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var name in (string[])
                 ["MORPHIRUP_VERSION", "MORPHIRUP_INSTALL_DIR", "MORPHIRUP_DOWNLOADER", "MORPHIRUP_API_URL", "MORPHIRUP_SHA256_FILE",
                  "GITHUB_TOKEN", "ALL_PROXY", "CURL_CA_BUNDLE", "SSL_CERT_FILE"])
            variables[name] = null;
        variables["MORPHIRUP_RELEASES_FILE"] = releases;
        variables["MORPHIRUP_FEED_FILE"] = feed;
        variables["MORPHIRUP_DOWNLOAD_URL"] = mirror;
        variables["MORPHIRUP_NO_MODIFY_PATH"] = "1";
        variables["HTTPS_PROXY"] = RefusingProxy;
        variables["NO_PROXY"] = "127.0.0.1";
        if (certificateFile is not null)
        {
            variables["CURL_CA_BUNDLE"] = certificateFile;
            variables["SSL_CERT_FILE"] = certificateFile;
        }

        // Linux and macOS have case-sensitive names, and wget reads only the lowercase ones.
        if (!windows)
        {
            variables["https_proxy"] = RefusingProxy;
            variables["no_proxy"] = "127.0.0.1";
            variables["all_proxy"] = null;
        }

        return variables;
    }

    public async ValueTask DisposeAsync()
    {
        ReleaseMirror[] mirrors;
        lock (_mirrors) mirrors = [.. _mirrors];
        foreach (var mirror in mirrors) await mirror.DisposeAsync().ConfigureAwait(false);
    }

    // PowerShell 7 when it is on PATH, else Windows PowerShell, which each Windows has. Both run the script.
    private static string PowerShell() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim('"'), "pwsh.exe"))
            .FirstOrDefault(File.Exists) ?? "powershell.exe";
}
