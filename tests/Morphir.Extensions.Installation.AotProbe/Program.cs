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

// Public sealed-owner publication consumer; the original 24 output cases above stay unchanged.
var publicationRoot = Directory.CreateTempSubdirectory("morphir-sealed-publication-probe-").FullName;
try
{
    var publicationBytes = System.Text.Encoding.UTF8.GetBytes("C# sealed publication");
    var publicationDigest = Sha256Digest.Create(Morphir.Toolchain.Digest.ofBytes(publicationBytes));
    var publicationLength = (ulong)publicationBytes.Length;
    if (!DistributionIdentifiers.TryParseArtifactFileName("sealed.exe", out var publicationName, out failure))
        throw new InvalidOperationException(failure.ToString());
    await using var publicationWriter = (await PrivateStaging.BeginAsync(
        publicationRoot, publicationLength, publicationDigest, publicationLength, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    await publicationWriter.WriteAsync(publicationBytes, CancellationToken.None);
    await using var publicationStage = (await publicationWriter.SealAsync(CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    var publication = (await SingleFileStore.PublishAsync(
        publicationStage, publicationName, false, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    var publishedBytes = await File.ReadAllBytesAsync(publication.AbsolutePath);
    if (!publishedBytes.AsSpan().SequenceEqual(publicationBytes)
        || publication.Length != publicationBytes.LongLength
        || !publication.Digest.Equals(publicationDigest)
        || Morphir.Toolchain.Digest.ofBytes(publishedBytes) != publication.Digest.Value)
        throw new InvalidOperationException("Published bytes, length or digest differ from the sealed owner.");
    Console.WriteLine($"sealed publication:verified:{publication.Length}:{publication.Digest.Value}");

    StageCleanupReport publicationCleanup = publicationStage.CleanupReport
        ?? throw new InvalidOperationException("Publication omitted awaited cleanup observation.");
    InstallationFailure? publicationCleanupFailure = publicationCleanup.Failure;
    if (publicationCleanupFailure is not null)
        throw new InvalidOperationException(publicationCleanupFailure.ToString());
    if (publicationCleanup.IsDeferred)
        throw new InvalidOperationException("Uncontended publication cleanup was deferred.");
    Console.WriteLine("sealed publication cleanup:removed:no-error");

    var refusedDuplicatePublication = false;
    try
    {
        await SingleFileStore.PublishAsync(publicationStage, publicationName, false, CancellationToken.None);
    }
    catch (InvalidOperationException) { refusedDuplicatePublication = true; }
    if (!refusedDuplicatePublication)
        throw new InvalidOperationException("Publication admitted the consumed sealed owner twice.");
    Console.WriteLine("sealed publication ownership:consumed-once");
}
finally
{
    try { Directory.Delete(publicationRoot, recursive: true); }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

// Public package/state consumer. This fixture is never launched and claims no readiness.
var packageProbeRoot = Directory.CreateTempSubdirectory("morphir-package-state-probe-").FullName;
Exception? packageProbeFailure = null;
try
{
    var contractCasesPath = Path.Combine(Environment.CurrentDirectory, "tests", "Fixtures",
        "ExtensionAcquisition", "parent-65bbbc96", "package-cases.json");
    using var contractCases = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(contractCasesPath));
    var descriptor = System.Text.Json.Nodes.JsonNode.Parse(contractCases.RootElement.GetProperty("cases")[0]
        .GetProperty("schemaDocuments")[0].GetProperty("document").GetRawText())
        ?? throw new InvalidOperationException("Missing fixed package descriptor.");
    var packagePlatform = InstallationPlatform.Create(System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);
    var packageEntry = OperatingSystem.IsWindows() ? "run.exe" : "run";
    var packagePayload = System.Text.Encoding.UTF8.GetBytes("guest");
    var packagePayloadDigest = Morphir.Toolchain.Digest.ofBytes(packagePayload);
    var packageManifestBytes = System.Text.Encoding.UTF8.GetBytes(
        $"morphir-package-tree 0.1.0-preview.1\nx 5 {packagePayloadDigest} {packageEntry}\n");
    var packageManifestDigest = Sha256Digest.Create(Morphir.Toolchain.Digest.ofBytes(packageManifestBytes));
    var packageArchive = Path.Combine(packageProbeRoot, "consumer.ZIP");
    using (var zip = System.IO.Compression.ZipFile.Open(packageArchive, System.IO.Compression.ZipArchiveMode.Create))
    {
        await using var entryOutput = zip.CreateEntry(packageEntry).Open();
        await entryOutput.WriteAsync(packagePayload);
    }
    var packageArchiveDigest = Sha256Digest.Create(Morphir.Toolchain.Digest.ofBytes(await File.ReadAllBytesAsync(packageArchive)));
    var artifactNode = descriptor["artifacts"]?[0]
        ?? throw new InvalidOperationException("Missing fixed artifact.");
    artifactNode["filename"] = "consumer.ZIP";
    artifactNode["platform"] = packagePlatform.DescriptorTriple;
    artifactNode["sha256"] = packageArchiveDigest.Value;
    var packageNode = artifactNode["package"]?.AsObject()
        ?? throw new InvalidOperationException("Missing fixed package declaration.");
    packageNode.Remove("root");
    packageNode.Remove("executables");
    packageNode["entryPoint"] = packageEntry;
    packageNode["treeSha256"] = packageManifestDigest.Value;
    var packagePlan = PackagePlan.Read(System.Text.Encoding.UTF8.GetBytes(descriptor.ToJsonString()), packagePlatform)
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    var packageHome = Path.Combine(packageProbeRoot, "home");
    var installedPackage = (await PackageInstaller.InstallAsync(packageHome, packagePlan, packageArchive, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    if (!installedPackage.Manifest.ToBytes().AsSpan().SequenceEqual(packageManifestBytes)
        || installedPackage.Manifest.Files.Count != 1
        || installedPackage.Manifest.Files[0].Path.Text != packageEntry
        || installedPackage.Manifest.Files[0].Length != packagePayload.Length
        || !installedPackage.Manifest.Files[0].Executable
        || !installedPackage.TreeDigest.Equals(packageManifestDigest))
        throw new InvalidOperationException("Installed package observations differ from the fixed fixture.");
    Console.WriteLine("package publication:manifest-and-identity-verified");

    await using (var packageLease = (await PackageInstaller.AcquireAsync(packageHome, packagePlan, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString())))
    {
        if (!(await File.ReadAllBytesAsync(packageLease.EntryPointPath)).AsSpan().SequenceEqual(packagePayload)
            || packageLease.Package.ArchiveDigest.Value != packageArchiveDigest.Value)
            throw new InvalidOperationException("Owned package activation differs from publication.");
        foreach (var diagnostic in new[] { packagePlan.ToString(), installedPackage.ToString(),
            installedPackage.Manifest.ToString(), installedPackage.Manifest.Files[0].ToString(), packageLease.ToString() })
            if (string.IsNullOrWhiteSpace(diagnostic)) throw new InvalidOperationException("Empty package diagnostic.");
    }
    var verifiedPackage = (await PackageInstaller.VerifyAsync(packageHome, packagePlan, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    if (!verifiedPackage.TreeDigest.Equals(packageManifestDigest)) throw new InvalidOperationException("Full verification changed tree identity.");
    Console.WriteLine("package activation:owned-and-full-verification-passed");

    if (!DistributionSelection.TryParse(null, "0.4.0-alpha.4", out var hostVersionSelection, out var hostVersionFailure))
        throw new InvalidOperationException(hostVersionFailure.ToString());
    var packageHostVersion = hostVersionSelection.Match<Morphir.Toolchain.SemVer>(
        _ => throw new InvalidOperationException("Expected exact host version."), version => version);
    await using var capturedPackage = (await InstalledExtensions.InstallLocalUncheckedAsync(packageHome, packagePlan,
        packageArchive, Path.Combine(packageProbeRoot, "repository-a"), Array.AsReadOnly(new[] { "--consumer-A" }),
        packageHostVersion, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    var activationRecord = capturedPackage.Record;
    var catalogSnapshot = activationRecord.CatalogSnapshotBytes;
    var lockSnapshot = activationRecord.LockBytes;
    var changedSnapshot = activationRecord.CatalogSnapshotBytes;
    changedSnapshot[0] ^= 1;
    if (!activationRecord.CatalogSnapshotBytes.AsSpan().SequenceEqual(catalogSnapshot)
        || !activationRecord.LockBytes.AsSpan().SequenceEqual(lockSnapshot)
        || activationRecord.Arguments.Count != 1 || activationRecord.Arguments[0] != "--consumer-A"
        || !activationRecord.IsLocalDeveloper)
        throw new InvalidOperationException("Activation record is mutable or loses local provenance.");
    using var capturedLock = System.Text.Json.JsonDocument.Parse(lockSnapshot);
    Console.WriteLine("package registration:local-unchecked-defensive-record");

    await using (var expectedCapture = (await InstalledExtensions.CaptureExpectedAsync(packageHome,
        catalogSnapshot, lockSnapshot, packageHostVersion, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString())))
    {
        if (!expectedCapture.Record.LockBytes.AsSpan().SequenceEqual(lockSnapshot))
            throw new InvalidOperationException("Expected-current capture changed exact lock bytes.");
    }
    await using (var currentCapture = (await InstalledExtensions.CaptureAsync(packageHome,
        activationRecord.ExtensionId, packageHostVersion, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString())))
    {
        if (!currentCapture.Record.CatalogSnapshotBytes.AsSpan().SequenceEqual(catalogSnapshot))
            throw new InvalidOperationException("Current capture changed the selected record.");
    }
    Console.WriteLine("package capture:current-and-expected-export-agree");

    using (var packageCancellation = new CancellationTokenSource())
    {
        packageCancellation.Cancel();
        var cancelledCapture = InstalledExtensions.CaptureAsync(packageHome, activationRecord.ExtensionId,
            packageHostVersion, packageCancellation.Token);
        var cancelledResult = (await cancelledCapture)
            .Match<(CapturedExtension? Value, string Code)>(value => (value, string.Empty), error => (null, error.Code));
        if (cancelledResult.Value is { } unexpectedCapture)
        {
            await ((IAsyncDisposable)unexpectedCapture).DisposeAsync();
            throw new InvalidOperationException("Precancelled package capture returned ownership.");
        }
        if (cancelledResult.Code != "extension.cancelled") throw new InvalidOperationException(cancelledResult.Code);
    }
    Console.WriteLine("package cancellation:task-returned-structured-refusal");

    await using var replacementCapture = (await InstalledExtensions.InstallLocalUncheckedAsync(packageHome, packagePlan,
        packageArchive, Path.Combine(packageProbeRoot, "repository-b"), Array.AsReadOnly(new[] { "--consumer-B" }),
        packageHostVersion, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    var replacementResult = (await InstalledExtensions.AcquireCurrentAsync(packageHome, activationRecord,
        packageHostVersion, CancellationToken.None))
        .Match<(PackageActivation? Lease, string Code)>(value => (value, string.Empty), error => (null, error.Code));
    if (replacementResult.Lease is { } unexpectedLease)
    {
        await ((IAsyncDisposable)unexpectedLease).DisposeAsync();
        throw new InvalidOperationException("Unpublished old record reacquired after replacement.");
    }
    if (replacementResult.Code != "extension.current_record_changed") throw new InvalidOperationException(replacementResult.Code);
    if (!(await File.ReadAllBytesAsync(capturedPackage.Activation.EntryPointPath)).AsSpan().SequenceEqual(packagePayload))
        throw new InvalidOperationException("Already held package lifetime changed after record replacement.");
    Console.WriteLine("package replacement:old-current-refused-held-owner-valid");

    var compatible = (await InstalledState.CheckCompatibilityAsync(packageHome, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    var recovery = (await InstalledState.RecoverAsync(packageHome, false, CancellationToken.None))
        .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
    if (!compatible || recovery.Recovered) throw new InvalidOperationException("Unexpected recovery in settled current state.");
    Console.WriteLine("package state:compatible-no-recovery-required");
}
catch (Exception error)
{
    packageProbeFailure = error;
    throw;
}
finally
{
    try
    {
        foreach (var ownedFile in Directory.EnumerateFiles(packageProbeRoot, "*",
            new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            File.SetAttributes(ownedFile, File.GetAttributes(ownedFile) & ~FileAttributes.ReadOnly);
        Directory.Delete(packageProbeRoot, recursive: true);
    }
    catch (Exception cleanupError) when (packageProbeFailure is not null)
    {
        packageProbeFailure.Data["packageProbeCleanup"] = cleanupError;
    }
}
