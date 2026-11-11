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
