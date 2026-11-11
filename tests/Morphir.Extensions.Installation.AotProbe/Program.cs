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

    // Public C# staging consumer. No internal writer, destination selection or publication.
    var stageHome = Path.Combine(probeRoot, "private-byte-home");
    var stageBytes = System.Text.Encoding.UTF8.GetBytes("C# private staging");
    var stageDigest = Sha256Digest.Create(Morphir.Toolchain.Digest.ofBytes(stageBytes));
    var stageLength = (ulong)stageBytes.Length;
    await using var writable = (await PrivateStaging.BeginAsync(
        stageHome, stageLength, stageDigest, stageLength, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));

    StageCleanupReport? beforeCleanup = writable.CleanupReport;
    if (beforeCleanup is not null)
        throw new InvalidOperationException("Live staging unexpectedly reports physical cleanup.");
    var borrowed = stageBytes.ToArray();
    await writable.WriteAsync(borrowed.AsMemory(0, 3), CancellationToken.None);
    await writable.WriteAsync(borrowed.AsMemory(3), CancellationToken.None);
    Array.Fill(borrowed, (byte)0);
    await using var sealedBytes = (await writable.SealAsync(CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    if (sealedBytes.Length != stageLength || !sealedBytes.Digest.Equals(stageDigest))
        throw new InvalidOperationException("Sealed bytes differ from their expected identity.");
    Console.WriteLine($"staging seal:{sealedBytes.Length}:{sealedBytes.Digest.Value}");

    await ((IAsyncDisposable)writable).DisposeAsync();
    if (sealedBytes.CleanupReport is not null)
        throw new InvalidOperationException("The writable wrapper disposed the transferred owner.");
    var refusedWrite = false;
    try { await writable.WriteAsync(stageBytes, CancellationToken.None); }
    catch (InvalidOperationException) { refusedWrite = true; }
    if (!refusedWrite)
        throw new InvalidOperationException("The transferred writable wrapper admitted bytes.");
    Console.WriteLine("staging ownership:transferred");

    await ((IAsyncDisposable)sealedBytes).DisposeAsync();
    StageCleanupReport cleanup = sealedBytes.CleanupReport
        ?? throw new InvalidOperationException("Awaited disposal omitted cleanup observation.");
    InstallationFailure? cleanupFailure = cleanup.Failure;
    if (cleanupFailure is not null)
        throw new InvalidOperationException(cleanupFailure.ToString());
    if (cleanup.IsDeferred)
        throw new InvalidOperationException("Uncontended owned staging cleanup was deferred.");
    Console.WriteLine("staging cleanup:removed:no-error");

    var nullHome = (await PrivateStaging.BeginAsync(
        null, 0, stageDigest, 0, CancellationToken.None))
        .Match(_ => throw new InvalidOperationException("Null Home was accepted."), error => error.Code);
    if (nullHome != "extension.invalid_path") throw new InvalidOperationException(nullHome);
    Console.WriteLine($"staging null-home:{nullHome}");

    var nullDigest = (await PrivateStaging.BeginAsync(
        stageHome, 0, null!, 0, CancellationToken.None))
        .Match(_ => throw new InvalidOperationException("Null digest was accepted."), error => error.Code);
    if (nullDigest != "extension.invalid_digest") throw new InvalidOperationException(nullDigest);
    Console.WriteLine($"staging null-digest:{nullDigest}");

    var invalidBound = (await PrivateStaging.BeginAsync(
        stageHome, 2, stageDigest, 1, CancellationToken.None))
        .Match(_ => throw new InvalidOperationException("Invalid caller bound was accepted."), error => error.Code);
    if (invalidBound != "extension.artifact_too_large") throw new InvalidOperationException(invalidBound);
    Console.WriteLine($"staging bound:{invalidBound}");

    await using var shortBytes = (await PrivateStaging.BeginAsync(
        stageHome, stageLength + 1, stageDigest, stageLength + 1, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    await shortBytes.WriteAsync(stageBytes, CancellationToken.None);
    var shortSeal = (await shortBytes.SealAsync(CancellationToken.None))
        .Match(_ => throw new InvalidOperationException("Short stream was sealed."), error => error.Code);
    if (shortSeal != "extension.digest_mismatch") throw new InvalidOperationException(shortSeal);
    Console.WriteLine($"staging short:{shortSeal}");

    await using var wrongBytes = (await PrivateStaging.BeginAsync(
        stageHome, stageLength, Sha256Digest.Create(new string('0', 64)), stageLength, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    await wrongBytes.WriteAsync(stageBytes, CancellationToken.None);
    var wrongSeal = (await wrongBytes.SealAsync(CancellationToken.None))
        .Match(_ => throw new InvalidOperationException("Wrong digest was sealed."), error => error.Code);
    if (wrongSeal != "extension.digest_mismatch") throw new InvalidOperationException(wrongSeal);
    Console.WriteLine($"staging digest:{wrongSeal}");

    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var cancelled = false;
    try { await PrivateStaging.BeginAsync(stageHome, stageLength, stageDigest, stageLength, cancellation.Token); }
    catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { cancelled = true; }
    if (!cancelled) throw new InvalidOperationException("Pre-cancelled Begin did not preserve cancellation.");
    Console.WriteLine("staging cancellation:propagated");
}
finally
{
    try { Directory.Delete(probeRoot, recursive: true); }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}
