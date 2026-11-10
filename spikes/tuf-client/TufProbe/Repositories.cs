// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
#if !PROBE_BASELINE
using Tuf;
#endif

namespace TufProbe;

/// <summary>
/// An in-memory TUF repository built from a fixture: numbered roots, the current timestamp, snapshot and targets
/// envelopes, and target bytes. Metadata is served as the canonical bytes of each envelope, as the fixtures specify.
/// </summary>
sealed class FixtureRepository
{
    public JsonObject TrustedRoot = null!;
    public List<JsonObject> RootUpdates = [];
    public Dictionary<string, JsonObject> Roles = new(StringComparer.Ordinal);
    public Dictionary<string, byte[]> Targets = new(StringComparer.Ordinal);
    /// <summary>Bytes served instead of a role's canonical envelope, such as a padded file.</summary>
    public Dictionary<string, byte[]> Overrides = new(StringComparer.Ordinal);
    /// <summary>Envelopes served under another version's file name, for mix-and-match cases.</summary>
    public Dictionary<(string Role, int Version), JsonObject> Extra = new();

    public FixtureRepository Clone() => new()
    {
        TrustedRoot = (JsonObject)TrustedRoot.DeepClone(),
        RootUpdates = RootUpdates.Select(r => (JsonObject)r.DeepClone()).ToList(),
        Roles = Roles.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone(), StringComparer.Ordinal),
        Targets = new(Targets, StringComparer.Ordinal),
        Overrides = new(Overrides, StringComparer.Ordinal),
        Extra = Extra.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone()),
    };

    public byte[] TrustedRootBytes() => Canonical.Encode(TrustedRoot);

    static int VersionOf(JsonObject envelope) => envelope["signed"]!["version"]!.GetValue<int>();

    public byte[]? Metadata(string role, int? version)
    {
        if (Extra.TryGetValue((role, version ?? 0), out var extra))
            return Canonical.Encode(extra);

        if (role == "root")
        {
            var root = new[] { TrustedRoot }.Concat(RootUpdates)
                .FirstOrDefault(r => VersionOf(r) == version);
            // A test may serve a root whose signed version differs from its file name.
            root ??= RootUpdates.FirstOrDefault(r => r["fileVersion"]?.GetValue<int>() == version);
            if (root is null) return null;
            var copy = (JsonObject)root.DeepClone();
            copy.Remove("fileVersion");
            return Overrides.TryGetValue($"root/{version}", out var padded) ? padded : Canonical.Encode(copy);
        }

        if (!Roles.TryGetValue(role, out var envelope)) return null;
        if (role != "timestamp" && version is { } v && VersionOf(envelope) != v) return null;
        return Overrides.TryGetValue(role, out var bytes) ? bytes : Canonical.Encode(envelope);
    }

    /// <summary>Finds a target by any consistent-snapshot spelling: <c>dir/hash.name</c> (TUF) or <c>hash.path</c>.</summary>
    public byte[]? Target(string requested)
    {
        var normalized = requested.Replace('\\', '/');
        foreach (var (path, bytes) in Targets)
        {
            var hash = Hex(SHA256.HashData(Declared(path) ?? bytes));
            var slash = path.LastIndexOf('/');
            var tufSpelling = slash < 0 ? $"{hash}.{path}" : $"{path[..(slash + 1)]}{hash}.{path[(slash + 1)..]}";
            if (normalized == tufSpelling || normalized == $"{hash}.{path}" || normalized == path)
                return bytes;
        }

        return null;
    }

    /// <summary>The bytes whose digest the targets metadata declares, when a mutation substituted the served bytes.</summary>
    public Dictionary<string, byte[]> DeclaredBytes = new(StringComparer.Ordinal);
    byte[]? Declared(string path) => DeclaredBytes.TryGetValue(path, out var b) ? b : null;

    public static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}

#if !PROBE_BASELINE
/// <summary>The fixture repository behind the library's transport interface, recording every request.</summary>
sealed class FixtureTransport(FixtureRepository repository) : ITufRepository
{
    public readonly List<string> Requests = [];
    public bool Offline;

    public Task<byte[]?> FetchMetadataAsync(string role, int? version = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(version is { } v ? $"metadata {v}.{role}.json" : $"metadata {role}.json");
        return Task.FromResult(Offline ? null : repository.Metadata(role, version));
    }

    public Task<byte[]?> FetchTargetAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        Requests.Add($"target {targetPath}");
        return Task.FromResult(Offline ? null : repository.Target(targetPath));
    }
}

/// <summary>
/// The profile's download bounds, supplied around the library: a transport decorator that refuses a file over its
/// limit before the client parses it. A real transport would stop reading at the limit.
/// </summary>
sealed class BoundedTransport(ITufRepository inner) : ITufRepository
{
    public static long LimitFor(string role) => role switch
    {
        "root" or "timestamp" => 64 * 1024,
        "snapshot" => 1024 * 1024,
        _ => 8 * 1024 * 1024,
    };

    public async Task<byte[]?> FetchMetadataAsync(string role, int? version = null, CancellationToken cancellationToken = default)
    {
        var bytes = await inner.FetchMetadataAsync(role, version, cancellationToken);
        if (bytes is not null && bytes.LongLength > LimitFor(role))
            throw new DownloadLimitException(role, bytes.LongLength, LimitFor(role));
        return bytes;
    }

    public Task<byte[]?> FetchTargetAsync(string targetPath, CancellationToken cancellationToken = default) =>
        inner.FetchTargetAsync(targetPath, cancellationToken);
}

sealed class DownloadLimitException(string role, long length, long limit)
    : Exception($"{role} metadata of {length} bytes exceeds the {limit}-byte limit")
{
    public string Role { get; } = role;
}

/// <summary>
/// Offline use from durable state, supplied around the library: serve the client's own persisted metadata back to
/// it as if it were the repository. The client re-verifies every signature, link, version and expiry, and fetches
/// nothing.
/// </summary>
sealed class CacheReplayTransport(ITufCache cache) : ITufRepository
{
    public readonly List<string> Requests = [];

    public Task<byte[]?> FetchMetadataAsync(string role, int? version = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(version is { } v ? $"metadata {v}.{role}.json" : $"metadata {role}.json");
        if (role == "root")
            return Task.FromResult<byte[]?>(null); // The persisted root is already the newest trusted root.
        return Task.FromResult(cache.LoadMetadata(role));
    }

    public Task<byte[]?> FetchTargetAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        Requests.Add($"target {targetPath}");
        return Task.FromResult<byte[]?>(null);
    }
}

/// <summary>An <see cref="ITufCache"/> that records the verified metadata bytes the client stores.</summary>
sealed class RecordingCache : ITufCache
{
    readonly InMemoryTufCache inner = new();
    public readonly Dictionary<string, byte[]> Stored = new(StringComparer.Ordinal);
    public byte[]? LoadMetadata(string role) => inner.LoadMetadata(role);
    public void StoreMetadata(string role, byte[] data) { Stored[role] = data; inner.StoreMetadata(role, data); }
    public byte[]? LoadTarget(string targetPath) => inner.LoadTarget(targetPath);
    public void StoreTarget(string targetPath, byte[] data) => inner.StoreTarget(targetPath, data);
}
#endif

/// <summary>Ed25519 signing for re-signing mutated fixture metadata with the fixtures' public TEST-ONLY seeds.</summary>
static class Signing
{
    public sealed record Key(string Role, string KeyId, byte[] Seed);

    public static Key FromLabel(string role, string keyId, string label) =>
        new(role, keyId, SHA256.HashData(Encoding.UTF8.GetBytes(label)));

#if !PROBE_BASELINE
    public static string PublicHex(Key key) =>
        FixtureRepository.Hex(new Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters(key.Seed, 0).GeneratePublicKey().GetEncoded());

    public static string Sign(Key key, byte[] message)
    {
        var signer = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
        signer.Init(true, new Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters(key.Seed, 0));
        signer.BlockUpdate(message, 0, message.Length);
        return FixtureRepository.Hex(signer.GenerateSignature());
    }

    /// <summary>Replaces an envelope's signatures with signatures by <paramref name="keys"/> over its canonical signed object.</summary>
    public static void Resign(JsonObject envelope, params Key[] keys)
    {
        var message = Canonical.Encode(envelope["signed"]);
        envelope["signatures"] = new JsonArray(keys
            .Select(k => (JsonNode)new JsonObject { ["keyid"] = k.KeyId, ["sig"] = Sign(k, message) })
            .ToArray());
    }
#endif
}
