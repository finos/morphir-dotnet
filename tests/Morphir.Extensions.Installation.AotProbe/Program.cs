// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Extensions.Installation;
using Morphir.Mep;

// This C# consumer makes nullability and delegate signatures part of every targeted probe build.
if (!DistributionIdentifiers.TryParseExtensionId("org.finos.morphir.fsharp-frontend", out var id, out var failure))
    throw new InvalidOperationException(failure.ToString());
Console.WriteLine(id.Value);

if (DistributionIdentifiers.TryParseExtensionId("morphir.example", out _, out failure))
    throw new InvalidOperationException("Reserved external id accepted.");
Console.WriteLine(failure.Code);

if (!DistributionIdentifiers.TryParseDeclaredPath("lib/backend.dll", out var path, out failure))
    throw new InvalidOperationException(failure.ToString());
Console.WriteLine(path.Text);

if (!DistributionIdentifiers.TryParseArtifactFileName("backend.exe", out path, out failure))
    throw new InvalidOperationException(failure.ToString());
Console.WriteLine(path.Text);

if (!DistributionIdentifiers.TryParseDigest(new string('a', 64), out var digest, out failure))
    throw new InvalidOperationException(failure.ToString());
Console.WriteLine(digest.Value);

if (!DistributionSelection.TryParse(null, "1.0.0+build.2", out var selection, out failure))
    throw new InvalidOperationException(failure.ToString());
Console.WriteLine(selection.Match(channel => channel, version => version.Text));

if (!InstallationPlatform.TryParse("win-arm64", out var platform, out failure))
    throw new InvalidOperationException(failure.ToString());
Console.WriteLine(platform.DescriptorTriple);
if (!platform.TryGetIndexPlatform(out var index, out failure))
    throw new InvalidOperationException(failure.ToString());
Console.WriteLine($"{index.Os}/{index.Arch}");

foreach (var version in new[] { "0.2.0-draft.1", "0.2.0-draft.2", "0.3.0" })
{
    var availability = MepSupport.Evaluate([ProtocolVersion.Create(version)]);
    var outcome = availability.Match(
        exact => $"runnable:{exact.Value}",
        reason => $"not-implemented:{reason}",
        reason => $"not-supported:{reason}");
    Console.WriteLine(outcome);
}
if (MepSupport.IsSupported(ProtocolVersion.Create("0.2.1-rc.1")))
    throw new InvalidOperationException("npm prerelease rule was bypassed.");

// No real Morphir Home or installed metadata is read. Both generic readers must be covered by future AOT runs.
var probeRoot = Directory.CreateTempSubdirectory("morphir-installation-probe-").FullName;
try
{
    var stagedDirectory = Directory.CreateDirectory(Path.Combine(probeRoot, "tmp", "staging")).FullName;
    var staged = Path.Combine(stagedDirectory, "guest.exe");
    var content = System.Text.Encoding.UTF8.GetBytes("probe content");
    File.WriteAllBytes(staged, content);
    var expected = Sha256Digest.Create(Morphir.Toolchain.Digest.ofBytes(content));
    if (!DistributionIdentifiers.TryParseArtifactFileName("guest.exe", out var fileName, out failure))
        throw new InvalidOperationException(failure.ToString());
    var stored = (await SingleFileStore.PublishAsync(probeRoot, staged, fileName, expected, true, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    Console.WriteLine(stored.RelativePath);

    var zipPath = Path.Combine(probeRoot, "input.zip");
    using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
    using (var output = zip.CreateEntry("guest.exe").Open())
        output.Write(content);
    var tarPath = Path.Combine(probeRoot, "input.tgz");
    using (var output = File.Create(tarPath))
    using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
    using (var tar = new System.Formats.Tar.TarWriter(gzip))
    using (var input = new MemoryStream(content, false))
        tar.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "guest.exe") { DataStream = input });
    foreach (var pathToInspect in new[] { zipPath, tarPath })
    {
        var inspection = (await ArchiveReader.InspectAsync(pathToInspect, CancellationToken.None))
            .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
        Console.WriteLine($"{inspection.Format}:{inspection.Entries[0].Name}");
    }
    using var run = (await RunCopies.CreateAsync(probeRoot, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    var sweep = (await RunCopies.SweepAsync(probeRoot, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    Console.WriteLine($"run-copy sweep:{sweep.Removed.Count}/{sweep.Skipped.Count}");
}
finally
{
    try { Directory.Delete(probeRoot, recursive: true); }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}
