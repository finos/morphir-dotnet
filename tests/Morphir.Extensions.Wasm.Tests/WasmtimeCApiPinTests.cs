using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 5.1 of add-wasm-extension-host: the Wasmtime C API archives that a Native AOT static link uses are
/// pinned in <c>build/wasmtime/WasmtimeCApi.props</c>. The pin must name the same Wasmtime release as the
/// managed <c>Wasmtime</c> package in <c>Directory.Packages.props</c>, so that moving one without the other fails
/// here, without a download. The build checks the downloaded archive's own header version again.
/// </summary>
public partial class WasmtimeCApiPinTests
{
    /// <summary>The six runtime identifiers the <c>Wasmtime</c> package ships a native library for, and the
    /// target triple of the Wasmtime release asset for each.</summary>
    private static readonly Dictionary<string, string> Platforms = new(StringComparer.Ordinal)
    {
        ["linux-arm64"] = "aarch64-linux",
        ["linux-x64"] = "x86_64-linux",
        ["osx-arm64"] = "aarch64-macos",
        ["osx-x64"] = "x86_64-macos",
        ["win-arm64"] = "aarch64-windows",
        ["win-x64"] = "x86_64-windows",
    };

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Morphir.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No Morphir.slnx above {AppContext.BaseDirectory}: the test does not run inside the repository.");
    }

    private static XDocument Pin() => XDocument.Load(Path.Combine(RepositoryRoot(), "build", "wasmtime", "WasmtimeCApi.props"));

    private static string PinnedVersion(XDocument pin) =>
        pin.Descendants("MorphirWasmtimeCApiVersion").Single().Value.Trim();

    private static string ManagedPackageVersion()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot(), "Directory.Packages.props"));
        return props.Descendants("PackageVersion")
            .Single(item => (string?)item.Attribute("Include") == "Wasmtime")
            .Attribute("Version")!.Value;
    }

    [Test]
    public async Task The_pinned_C_API_version_equals_the_managed_Wasmtime_package_version()
    {
        await Assert.That(PinnedVersion(Pin())).IsEqualTo(ManagedPackageVersion());
    }

    [Test]
    public async Task The_pin_lists_exactly_the_six_platforms_of_the_managed_package()
    {
        var platforms = Pin().Descendants("MorphirWasmtimeCApiArchive")
            .Select(item => (string?)item.Attribute("Include") ?? "")
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(platforms).IsEquivalentTo(Platforms.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Test]
    public async Task Every_archive_is_the_release_asset_of_the_pinned_version_with_a_SHA256()
    {
        var pin = Pin();
        var version = PinnedVersion(pin);
        var problems = new List<string>();
        foreach (var item in pin.Descendants("MorphirWasmtimeCApiArchive"))
        {
            var rid = (string?)item.Attribute("Include") ?? "";
            if (!Platforms.TryGetValue(rid, out var triple))
            {
                problems.Add($"{rid}: not one of the six platforms");
                continue;
            }

            var root = $"wasmtime-v{version}-{triple}-c-api";
            var extension = rid.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.xz";
            var expectedUrl = $"https://github.com/bytecodealliance/wasmtime/releases/download/v{version}/{root}{extension}";
            var expectedLibrary = rid.StartsWith("win-", StringComparison.Ordinal) ? "lib/wasmtime.lib" : "lib/libwasmtime.a";

            var url = item.Element("Url")?.Value.Trim();
            var sha256 = item.Element("Sha256")?.Value.Trim() ?? "";
            var archiveRoot = item.Element("Root")?.Value.Trim();
            var library = item.Element("StaticLibrary")?.Value.Trim();
            if (url != expectedUrl)
            {
                problems.Add($"{rid}: URL {url}, expected {expectedUrl}");
            }

            if (!Sha256Hex().IsMatch(sha256))
            {
                problems.Add($"{rid}: SHA-256 '{sha256}' is not 64 lowercase hex digits");
            }

            if (archiveRoot != root)
            {
                problems.Add($"{rid}: root {archiveRoot}, expected {root}");
            }

            if (library != expectedLibrary)
            {
                problems.Add($"{rid}: static library {library}, expected {expectedLibrary}");
            }
        }

        await Assert.That(problems).IsEmpty();
    }
}
