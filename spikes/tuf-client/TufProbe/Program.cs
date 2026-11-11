// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// Spike: drive the NuGet package Tuf over the finos/morphir tool release metadata v1 conformance fixture and the
// signed toolchain fixture, through an in-memory transport. Evaluation only.
//
//   TufProbe refresh <v1 conformance.json>                      full refresh with root rotation, then every target
//   TufProbe conformance <v1 conformance.json> <signed dir> [<report.md>]   every case, as a table
//
// ProbeVariant=baseline builds the same console without the library, to size what the library adds.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TufProbe;
#if !PROBE_BASELINE
using Tuf;
#endif

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: TufProbe refresh <conformance.json> | conformance <conformance.json> <signed dir> [<report.md>]");
    return 2;
}

var v1 = Fixtures.LoadV1(args[1]);

#if PROBE_BASELINE
// The same fixture work without the library: parse, encode canonically, hash every target.
Console.WriteLine($"baseline: root {Canonical.Encode(v1.Repository.TrustedRoot).Length} bytes");
foreach (var (path, bytes) in v1.Repository.Targets)
    Console.WriteLine($"baseline: {path} {Convert.ToHexStringLower(SHA256.HashData(bytes))}");
#if PROBE_ED25519
// The primitive a direct implementation needs: Ed25519 verification of each root signature, with BouncyCastle
// used directly (no key factory, no other scheme).
JsonObject? previous = null;
foreach (var root in new[] { v1.Repository.TrustedRoot }.Concat(v1.Repository.RootUpdates))
{
    var message = Canonical.Encode(root["signed"]);
    // A rotated root is signed by keys of the previous root as well as its own.
    var keys = new JsonObject();
    foreach (var source in new[] { previous, root })
        foreach (var (id, key) in source?["signed"]?["keys"]?.AsObject() ?? new JsonObject())
            keys[id] = key!.DeepClone();
    previous = root;
    foreach (var signature in root["signatures"]!.AsArray())
    {
        var keyId = signature!["keyid"]!.GetValue<string>();
        var publicKey = Convert.FromHexString(keys[keyId]!["keyval"]!["public"]!.GetValue<string>());
        // Fixed-size check before verification, as any caller of the primitive must do.
        if (publicKey.Length != 32) throw new InvalidOperationException("not an Ed25519 public key");
        var valid = new Org.BouncyCastle.Crypto.Parameters.Ed25519PublicKeyParameters(publicKey).Verify(
            Org.BouncyCastle.Math.EC.Rfc8032.Ed25519.Algorithm.Ed25519, null, message, 0, message.Length,
            Convert.FromHexString(signature["sig"]!.GetValue<string>()), 0);
        Console.WriteLine($"ed25519: root v{root["signed"]!["version"]} key {keyId[..12]} {(valid ? "valid" : "INVALID")}");
    }
}
#endif
return 0;
#else
if (args[0] == "refresh")
{
    var transport = new FixtureTransport(v1.Repository);
    var cache = new RecordingCache();
    using var client = Cases.Client(v1.Repository, transport, cache);
    var metadata = await client.GetTrustedMetadataAsync();
    var root = JsonNode.Parse(cache.Stored["root"])!["signed"]!["version"]!.GetValue<int>();
    Console.WriteLine($"refresh: trusted root v{root}, metadata expires {metadata.Expires:O}");
    foreach (var path in v1.Repository.Targets.Keys.Order(StringComparer.Ordinal))
    {
        var target = await client.GetTargetAsync(path);
        Console.WriteLine($"target {path}: {target.Content.Length} bytes, sha256 {Convert.ToHexStringLower(SHA256.HashData(target.Content.Span))}");
    }

    Console.WriteLine($"requests: {string.Join(", ", transport.Requests)}");
    return 0;
}

#if !PROBE_REFRESH_ONLY
if (args[0] == "conformance" && args.Length >= 3)
{
    var signed = Fixtures.LoadSigned(args[2]);
    var results = new List<CaseResult>();
    results.AddRange(await Cases.V1Suite(v1));
    results.AddRange(await Cases.SignedTrustSuite(signed));
    results.AddRange(await Cases.ProfileSuite(signed, v1));

    var table = new StringBuilder();
    table.AppendLine("| Suite | Case | Expected | Library outcome | Verdict | How the code was obtained / note |");
    table.AppendLine("| --- | --- | --- | --- | --- | --- |");
    foreach (var r in results)
        table.AppendLine($"| {r.Suite} | {r.Name} | {r.Expected} | {r.Actual} | {r.Verdict} | {r.Note.Replace("|", "\\|")} |");

    var counts = results.GroupBy(r => r.Verdict).Select(g => $"{g.Key}: {g.Count()}");
    table.AppendLine().AppendLine($"Totals: {results.Count} cases; {string.Join("; ", counts)}");
    Console.Write(table);
    if (args.Length >= 4) File.WriteAllText(args[3], table.ToString());
    return 0;
}
#endif

Console.Error.WriteLine($"unknown command {args[0]}");
return 2;
#endif

record CaseResult(string Suite, string Name, string Expected, string Actual, string Verdict, string Note);

record V1Fixture(FixtureRepository Repository, JsonObject Raw);

record SignedFixture(FixtureRepository Metadata, FixtureRepository LearnedRevocation, JsonObject Raw, JsonObject Cases,
    Dictionary<string, Signing.Key> Keys);

static class Fixtures
{
    static FixtureRepository Load(JsonObject trustedRoot, JsonArray? rootUpdates, JsonObject roles, JsonObject targetFiles) => new()
    {
        TrustedRoot = (JsonObject)trustedRoot.DeepClone(),
        RootUpdates = rootUpdates?.Select(r => (JsonObject)r!.DeepClone()).ToList() ?? [],
        Roles = new[] { "timestamp", "snapshot", "targets" }
            .ToDictionary(r => r, r => (JsonObject)roles[r]!.DeepClone(), StringComparer.Ordinal),
        Targets = targetFiles.ToDictionary(p => p.Key, p => Encoding.UTF8.GetBytes(p.Value!.GetValue<string>()), StringComparer.Ordinal),
    };

    public static V1Fixture LoadV1(string path)
    {
        var raw = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        return new(Load(raw["trustedRoot"]!.AsObject(), raw["rootUpdates"]!.AsArray(), raw["metadata"]!.AsObject(), raw["targetFiles"]!.AsObject()), raw);
    }

    public static SignedFixture LoadSigned(string directory)
    {
        var raw = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "repository.json")))!.AsObject();
        var cases = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "cases.json")))!.AsObject();
        var keys = raw["testOnlyKeys"]!.AsArray()
            .Select(k => Signing.FromLabel(k!["role"]!.GetValue<string>(), k["keyId"]!.GetValue<string>(), k["label"]!.GetValue<string>()))
            .ToDictionary(k => k.Role == "root" ? $"root-{k.KeyId[..4]}" : k.Role);
        var targets = raw["targetFiles"]!.AsObject();
        return new(
            Load(raw["trustedRoot"]!.AsObject(), null, raw["metadata"]!.AsObject(), targets),
            Load(raw["trustedRoot"]!.AsObject(), null, raw["learnedRevocation"]!.AsObject(), targets),
            raw, cases, keys);
    }
}

#if !PROBE_BASELINE
static class Cases
{
    public static TufClient Client(FixtureRepository repository, ITufRepository transport, ITufCache? cache = null, int maxRootRotations = 32) =>
        new(new TufClientOptions
        {
            MetadataBaseUrl = new Uri("https://primary.example.invalid/morphir-tools/"),
            TrustedRoot = repository.TrustedRootBytes(),
            Repository = transport,
            Cache = cache ?? new InMemoryTufCache(),
            MaxRootRotations = maxRootRotations,
        });

    /// <summary>
    /// Maps a library failure to a profile code. Only an expired role has a typed exception; every other code needs
    /// the message text, which the library does not promise to keep.
    /// </summary>
    public static (string Code, string How) Classify(Exception error)
    {
        var (code, how) = ClassifyCore(error);
        return how == "message text" ? (code, $"message text: \"{error.Message}\"") : (code, how);
    }

    static (string Code, string How) ClassifyCore(Exception error) => error switch
    {
        TufExpiredException expired => ("metadata_expired", $"typed: TufExpiredException (Role={expired.Role})"),
        DownloadLimitException limit => ("download_limit", $"wrapper: BoundedTransport refused {limit.Role}"),
        TufException e when e.Message.Contains("hash verification failed") => ("target_digest_mismatch", "message text"),
        TufException e when e.Message.StartsWith("Target '") && e.Message.Contains("size") => ("target_length_mismatch", "message text"),
        TufException e when e.Message.Contains("not found in targets metadata") => ("target_not_authenticated", "message text"),
        TufException e when e.Message.Contains("not signed by threshold") || e.Message.Contains("self-signed") || e.Message.Contains("Root version mismatch")
            => ("metadata_root_rotation_invalid", "message text"),
        TufException e when e.Message.Contains("signature verification failed") => ("metadata_signature_invalid", "message text"),
        TufException e when e.Message.Contains("ollback") => ("metadata_rollback", "message text"),
        TufException e when e.Message.Contains("hash mismatch") || e.Message.Contains("does not match expected") || e.Message.Contains("match timestamp reference") || e.Message.Contains("match snapshot reference")
            => ("metadata_link_mismatch", "message text"),
        TufException e when e.Message.StartsWith("Failed to fetch") || e.Message.Contains("not found on repository")
            => ("offline_metadata_unavailable or repository_unavailable", "message text; the library does not distinguish them"),
        JsonException => ("unclassified", $"untyped JsonException: {error.Message}"),
        _ => ("unclassified", $"{error.GetType().Name}: {error.Message}"),
    };

    static async Task<(string Code, string How)> Outcome(Func<Task> action)
    {
        try
        {
            await action();
            return ("valid", "");
        }
        catch (Exception e)
        {
            return Classify(e);
        }
    }

    static CaseResult Result(string suite, string name, string expected, (string Code, string How) actual, string? note = null, string? verdict = null) =>
        new(suite, name, expected, actual.Code,
            verdict ?? (actual.Code == expected ? "pass" : "fail"),
            string.Join("; ", new[] { actual.How, note ?? "" }.Where(s => s != "")));

    static async Task<(string, string)> Load(FixtureRepository repository, ITufCache? cache = null, int maxRootRotations = 32, Func<ITufRepository, ITufRepository>? wrap = null)
    {
        var transport = new FixtureTransport(repository);
        using var client = Client(repository, wrap is null ? transport : wrap(transport), cache, maxRootRotations);
        return await Outcome(() => client.GetTrustedMetadataAsync());
    }

    static async Task<(string, string)> Read(FixtureRepository repository, string target, ITufCache? cache = null)
    {
        using var client = Client(repository, new FixtureTransport(repository), cache);
        return await Outcome(() => client.GetTargetAsync(target));
    }

    // ---- tool release metadata v1 conformance fixture -------------------------------------------------------

    public static async Task<List<CaseResult>> V1Suite(V1Fixture v1)
    {
        const string suite = "v1";
        var results = new List<CaseResult>();
        var repo = v1.Repository;

        // Serving check: the canonical bytes the harness serves have the lengths and digests the metadata declares.
        var snapshotMeta = repo.Roles["timestamp"]["signed"]!["meta"]!["snapshot.json"]!;
        var served = repo.Metadata("snapshot", 1)!;
        var servingOk = served.Length == snapshotMeta["length"]!.GetValue<int>() &&
                        FixtureRepository.Hex(SHA256.HashData(served)) == snapshotMeta["hashes"]!["sha256"]!.GetValue<string>();
        results.Add(new(suite, "harness serves the declared canonical bytes", "valid", servingOk ? "valid" : "mismatch", servingOk ? "pass" : "fail", "harness check, not the library"));

        var cache = new RecordingCache();
        var rotation = await Load(repo, cache);
        var rootVersion = cache.Stored.TryGetValue("root", out var rootBytes) ? JsonNode.Parse(rootBytes)!["signed"]!["version"]!.GetValue<int>() : 0;
        results.Add(Result(suite, "two-of-three root, dual-threshold rotation to v2, full refresh", "valid", rotation, $"persisted root v{rootVersion}"));

        foreach (var path in repo.Targets.Keys.Order(StringComparer.Ordinal))
            results.Add(Result(suite, $"authenticate target {path}", "valid", await Read(repo, path)));

        var expired = repo.Clone();
        expired.Roles["timestamp"] = (JsonObject)v1.Raw["rejectionMetadata"]!["expiredTimestamp"]!.DeepClone();
        results.Add(Result(suite, "expired timestamp at referenceTime 2030-01-01", "metadata_expired", await Load(expired),
            "the timestamp expires 2029-12-31T23:59:59Z; the library compares with DateTimeOffset.UtcNow and cannot take the fixture's reference time"));

        var noOldThreshold = repo.Clone(); // v2 is signed by 7ab6, c3f8 and 8bf5: without c3f8 the old threshold holds and the new one does not
        var v2 = noOldThreshold.RootUpdates[0];
        var keep = v2["signatures"]!.AsArray().Where(s => !s!["keyid"]!.GetValue<string>().StartsWith("c3f8")).Select(s => s!.DeepClone()).ToArray();
        v2["signatures"] = new JsonArray(keep);
        results.Add(Result(suite, "root v2 without its own new threshold (the c3f8 signature removed)", "metadata_root_rotation_invalid", await Load(noOldThreshold)));

        var sameLength = repo.Clone();
        var zip = "artifacts/desktop/1.0.0/windows-x86_64.zip";
        sameLength.DeclaredBytes[zip] = sameLength.Targets[zip];
        var tampered = (byte[])sameLength.Targets[zip].Clone();
        tampered[^1] ^= 0x01;
        sameLength.Targets[zip] = tampered;
        results.Add(Result(suite, "target bytes of the same length, another digest", "target_digest_mismatch", await Read(sameLength, zip)));

        var otherLength = repo.Clone();
        otherLength.DeclaredBytes[zip] = otherLength.Targets[zip];
        otherLength.Targets[zip] = [.. otherLength.Targets[zip], 0x00];
        results.Add(Result(suite, "target bytes of another length", "target_length_mismatch", await Read(otherLength, zip),
            "the transport returned every byte before the length was compared: the library cannot bound a download by the declared length"));

        var identity = FixtureRepository.Hex(SHA256.HashData(Canonical.Encode(repo.TrustedRoot["signed"])));
        results.Add(new(suite, "repository identity = SHA-256 of canonical initial root signed", v1.Raw["repositoryId"]!.GetValue<string>()[..12] + "…",
            identity == v1.Raw["repositoryId"]!.GetValue<string>() ? "harness value matches" : "harness value differs", "not expressible",
            "the library computes no repository identity; the harness's own canonical encoder matches the fixture"));

        var first = await Read(repo, zip);
        var second = await Read(repo.Clone(), zip);
        results.Add(new(suite, "the same identity and bytes through two mirrors", "valid", first.Item1 == "valid" && second.Item1 == "valid" ? "valid" : "failed",
            "around the library", "one client has one ITufRepository; mirror order, retry after an integrity failure and the integrity event belong to a wrapper"));

        var custom = cache.Stored.TryGetValue("targets", out var targetsBytes) &&
                     JsonNode.Parse(targetsBytes)!["signed"]!["targets"]!["releases/desktop/0.9.0.json"]!["custom"]!["morphir"]!["status"]!.GetValue<string>() == "revoked";
        results.Add(new(suite, $"{v1.Raw["resolutionCases"]!.AsArray().Count} resolution cases (channels, exact, revoked, CLI, platform)", "selection or no_compatible_release / release_revoked",
            "not run", "not expressible",
            $"the API exposes no targets metadata: TargetFileJson.Custom is parsed and dropped. The verified targets bytes the cache receives do carry custom.morphir ({(custom ? "read here" : "not found")}), so a wrapper can re-parse them"));

        return results;
    }

    // ---- signed toolchain fixture, trust section --------------------------------------------------------------

    static JsonObject RoleAt(JsonObject raw, string dotted)
    {
        JsonNode node = raw;
        foreach (var part in dotted.Split('.')) node = node[part]!;
        return (JsonObject)node.DeepClone();
    }

    static FixtureRepository Mutate(SignedFixture fixture, JsonObject @case)
    {
        var repo = (@case["metadata"]?.GetValue<string>() == "learnedRevocation" ? fixture.LearnedRevocation : fixture.Metadata).Clone();
        var mutations = @case["mutations"] switch
        {
            JsonArray a => a.Select(m => m!.AsObject()).ToList(),
            JsonObject o => [o],
            _ => [],
        };
        foreach (var m in mutations)
        {
            switch (m["op"]!.GetValue<string>())
            {
                case "set-signed-member":
                    Pointer.Set(repo.Roles[m["role"]!.GetValue<string>()]["signed"]!, m["pointer"]!.GetValue<string>(), m["value"]!.DeepClone());
                    break;
                case "relink":
                    Relink(repo, fixture.Keys);
                    break;
                case "replace-role":
                    repo.Roles[m["role"]!.GetValue<string>()] = RoleAt(fixture.Raw, m["from"]!.GetValue<string>());
                    break;
                case "replace-target-bytes":
                {
                    var target = m["target"]!.GetValue<string>();
                    repo.DeclaredBytes[target] = repo.Targets[target];
                    repo.Targets[target] = repo.Targets[m["with"]!.GetValue<string>()];
                    break;
                }
                case "replace-target-text":
                {
                    var target = m["target"]!.GetValue<string>();
                    repo.DeclaredBytes[target] = repo.Targets[target];
                    var text = Encoding.UTF8.GetString(repo.Targets[target]);
                    var find = m["find"]!.GetValue<string>();
                    var at = text.IndexOf(find, StringComparison.Ordinal);
                    repo.Targets[target] = Encoding.UTF8.GetBytes(text[..at] + m["replace"]!.GetValue<string>() + text[(at + find.Length)..]);
                    break;
                }
                default:
                    throw new InvalidOperationException($"unknown mutation {m["op"]}");
            }
        }

        return repo;
    }

    /// <summary>The fixture's relink: recompute the snapshot's targets.json and the timestamp's snapshot.json, and sign both again.</summary>
    public static void Relink(FixtureRepository repo, Dictionary<string, Signing.Key> keys)
    {
        void Link(string parent, string child)
        {
            var bytes = Canonical.Encode(repo.Roles[child]);
            repo.Roles[parent]["signed"]!["meta"]![$"{child}.json"] = new JsonObject
            {
                ["version"] = repo.Roles[child]["signed"]!["version"]!.GetValue<int>(),
                ["length"] = bytes.Length,
                ["hashes"] = new JsonObject { ["sha256"] = FixtureRepository.Hex(SHA256.HashData(bytes)) },
            };
        }

        Link("snapshot", "targets");
        Signing.Resign(repo.Roles["snapshot"], keys["snapshot"]);
        Link("timestamp", "snapshot");
        Signing.Resign(repo.Roles["timestamp"], keys["timestamp"]);
    }

    public static async Task<List<CaseResult>> SignedTrustSuite(SignedFixture fixture)
    {
        const string suite = "signed trust";
        var results = new List<CaseResult>();
        foreach (var node in fixture.Cases["trust"]!.AsArray())
        {
            var @case = node!.AsObject();
            var name = @case["name"]!.GetValue<string>();
            var expected = @case["expected"] is JsonValue ? "valid" : @case["expected"]!["code"]!.GetValue<string>();
            var expectedPath = @case["expected"] is JsonObject e ? e["path"]!.GetValue<string>() : "";
            var action = @case["action"]!.AsObject();
            var repo = Mutate(fixture, @case);
            var op = action["op"]!.GetValue<string>();

            switch (op)
            {
                case "load":
                    results.Add(Result(suite, name, expected, await Load(repo)));
                    break;
                case "read":
                    results.Add(Result(suite, name, expected, await Read(repo, action["target"]!.GetValue<string>())));
                    break;
                case "authenticate":
                {
                    // Local bytes are checked by preloading them as a cached target with an offline target transport.
                    var cache = new InMemoryTufCache();
                    var target = action["target"]!.GetValue<string>();
                    cache.StoreTarget(target, Encoding.UTF8.GetBytes(action["text"]!.GetValue<string>()));
                    results.Add(Result(suite, name, expected, await Read(repo, target, cache),
                        "emulated: preloaded cache entry; the library has no API to authenticate caller-supplied bytes"));
                    break;
                }
                default:
                {
                    // check-recipe and check-recipe-bindings: the TUF step is reading the target the case changes.
                    var mutation = @case["mutations"] switch { JsonArray a => a.FirstOrDefault() as JsonObject, JsonObject o => o, _ => null };
                    var mutated = mutation?["target"]?.GetValue<string>();
                    var bound = action["documentMutations"]?.AsArray().FirstOrDefault()?["value"]?.GetValue<string>();
                    var target = mutated ?? bound;
                    if (target is null)
                    {
                        results.Add(new(suite, name, $"{expected} at {expectedPath}", "not run", "not expressible",
                            "needs the authenticated custom.morphir status of the release; the library drops custom"));
                        break;
                    }

                    var outcome = await Read(repo, target);
                    results.Add(new(suite, name, $"{expected} at {(expectedPath == "" ? "(empty)" : expectedPath)}", outcome.Item1,
                        outcome.Item1 == expected ? "pass (TUF step)" : "fail",
                        $"{outcome.Item2}; read {target}; the binding path is morphirup's recipe check, not the TUF client's"));
                    break;
                }
            }
        }

        return results;
    }

    // ---- profile requirements built on the signed fixture's public TEST-ONLY keys -------------------------------

    static Signing.Key NewRootKey(string label) =>
        Signing.FromLabel("root", FixtureRepository.Hex(SHA256.HashData(Encoding.UTF8.GetBytes("keyid:" + label))), label);

    /// <summary>A case the profile requires refused passes when the library refuses it for any reason.</summary>
    static string Refused(CaseResult r) => r.Actual == "valid" ? "fail" : "pass";

    /// <summary>Root v(n) from the trusted root with the given root keys and threshold, signed by <paramref name="signers"/>.</summary>
    static JsonObject Root(FixtureRepository repo, int version, Signing.Key[] rootKeys, int threshold, params Signing.Key[] signers)
    {
        var root = (JsonObject)repo.TrustedRoot.DeepClone();
        var signed = root["signed"]!.AsObject();
        signed["version"] = version;
        var keys = signed["keys"]!.AsObject();
        foreach (var key in rootKeys.Where(k => !keys.ContainsKey(k.KeyId)))
            keys[key.KeyId] = new JsonObject { ["keytype"] = "ed25519", ["scheme"] = "ed25519", ["keyval"] = new JsonObject { ["public"] = Signing.PublicHex(key) } };
        signed["roles"]!["root"] = new JsonObject
        {
            ["keyids"] = new JsonArray(rootKeys.Select(k => (JsonNode)k.KeyId).ToArray()),
            ["threshold"] = threshold,
        };
        Signing.Resign(root, signers);
        return root;
    }

    public static async Task<List<CaseResult>> ProfileSuite(SignedFixture fixture, V1Fixture v1)
    {
        const string suite = "profile";
        var results = new List<CaseResult>();
        var keys = fixture.Keys;
        var root1 = keys.Values.First(k => k.KeyId.StartsWith("361b"));
        var root2 = keys.Values.First(k => k.KeyId.StartsWith("34aa"));
        var root3 = NewRootKey("TufProbe TEST-ONLY root-3");

        var valid = fixture.Metadata.Clone();
        valid.RootUpdates.Add(Root(valid, 2, [root2, root3], 2, root1, root2, root3));
        results.Add(Result(suite, "rotation v1 to v2 meeting both thresholds", "valid", await Load(valid)));

        var noOld = fixture.Metadata.Clone();
        noOld.RootUpdates.Add(Root(noOld, 2, [root2, root3], 2, root2, root3));
        results.Add(Result(suite, "rotation signed by a new threshold only", "metadata_root_rotation_invalid", await Load(noOld)));

        var noNew = fixture.Metadata.Clone();
        noNew.RootUpdates.Add(Root(noNew, 2, [root2, root3], 2, root1, root2));
        results.Add(Result(suite, "rotation signed by the old threshold only", "metadata_root_rotation_invalid", await Load(noNew)));

        var skipped = fixture.Metadata.Clone();
        var three = Root(skipped, 3, [root1, root2], 2, root1, root2);
        three["fileVersion"] = 2;
        skipped.RootUpdates.Add(three);
        results.Add(Result(suite, "2.root.json whose signed version is 3", "metadata_root_rotation_invalid", await Load(skipped)));

        var chain = fixture.Metadata.Clone();
        for (var v = 2; v <= 34; v++) chain.RootUpdates.Add(Root(chain, v, [root1, root2], 2, root1, root2));
        var chainCache = new RecordingCache();
        var chainOutcome = await Load(chain, chainCache, maxRootRotations: 32);
        var reached = JsonNode.Parse(chainCache.Stored["root"])!["signed"]!["version"]!.GetValue<int>();
        results.Add(new(suite, "33 newer roots with MaxRootRotations = 32", "stop after 32 and ask to retry", $"{chainOutcome.Item1}, trusted root v{reached}",
            chainOutcome.Item1 == "valid" ? "fail" : "pass",
            "the library stops after 32 rotations without any signal and finishes the refresh on root v33; its default MaxRootRotations is 1024"));

        var rollbackCache = new InMemoryTufCache();
        var newer = await Load(fixture.LearnedRevocation, rollbackCache);
        var older = await Load(fixture.Metadata, rollbackCache);
        results.Add(Result(suite, "timestamp v1 served after a persisted timestamp v2", "metadata_rollback", older, $"first refresh: {newer.Item1}"));

        var mixed = fixture.Metadata.Clone();
        mixed.Extra[("snapshot", 1)] = (JsonObject)fixture.LearnedRevocation.Roles["snapshot"].DeepClone();
        results.Add(Result(suite, "snapshot v2 served as 1.snapshot.json (mix and match)", "metadata_link_mismatch", await Load(mixed)));

        var padded = fixture.Metadata.Clone();
        var timestamp = Canonical.Encode(padded.Roles["timestamp"]);
        padded.Overrides["timestamp"] = [.. timestamp[..^1], .. Encoding.ASCII.GetBytes(new string(' ', 70 * 1024)), (byte)'}'];
        results.Add(Result(suite, "timestamp of 70 KiB (limit 64 KiB)", "refused before parsing", await Load(padded),
            "the whitespace is outside the signed canonical bytes, so the signature still verifies") is var p1 ? p1 with { Verdict = Refused(p1) } : p1);
        results.Add(Result(suite, "timestamp of 70 KiB through BoundedTransport", "download_limit", await Load(padded, wrap: inner => new BoundedTransport(inner)),
            "supplied around the library") is var p2 ? p2 with { Verdict = p2.Actual == "download_limit" ? "pass (around)" : "fail" } : p2);

        var bigRoot = fixture.Metadata.Clone();
        bigRoot.RootUpdates.Add(Root(bigRoot, 2, [root1, root2], 2, root1, root2));
        var rootBytes = Canonical.Encode(bigRoot.RootUpdates[0]);
        bigRoot.Overrides["root/2"] = [.. rootBytes[..^1], .. Encoding.ASCII.GetBytes(new string(' ', 70 * 1024)), (byte)'}'];
        results.Add(Result(suite, "root v2 of 70 KiB (limit 64 KiB)", "refused before parsing", await Load(bigRoot)) is var p3 ? p3 with { Verdict = Refused(p3) } : p3);

        var spec = fixture.Metadata.Clone();
        spec.Roles["timestamp"]["signed"]!["spec_version"] = "2.0.0";
        Signing.Resign(spec.Roles["timestamp"], keys["timestamp"]);
        results.Add(Result(suite, "timestamp with spec_version 2.0.0", "refused (spec_version must be compatible with 1.0)", await Load(spec),
            "the parser reads spec_version and never checks it") is var p4 ? p4 with { Verdict = Refused(p4) } : p4);

        var notConsistent = fixture.Metadata.Clone();
        notConsistent.TrustedRoot["signed"]!["consistent_snapshot"] = false;
        Signing.Resign(notConsistent.TrustedRoot, root1, root2);
        results.Add(Result(suite, "trusted root with consistent_snapshot false", "refused (profile requires true)", await Load(notConsistent),
            "the library supports both layouts and offers no option to require consistent snapshots") is var p5 ? p5 with { Verdict = Refused(p5) } : p5);

        // Offline from durable state.
        var directory = Path.Combine(Path.GetTempPath(), "tuf-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            var online = await Load(fixture.Metadata, new FileSystemTufCache(directory));
            using (var warm = Client(fixture.Metadata, new FixtureTransport(fixture.Metadata), new FileSystemTufCache(directory)))
                await warm.GetTargetAsync("toolchains/recipes/fsharp/1.0.0.json");

            var offlineTransport = new FixtureTransport(fixture.Metadata) { Offline = true };
            using (var offline = Client(fixture.Metadata, offlineTransport, new FileSystemTufCache(directory)))
            {
                var outcome = await Outcome(() => offline.GetTrustedMetadataAsync());
                results.Add(new(suite, "offline refresh from persisted state (FileSystemTufCache, no network)", "valid with no request",
                    outcome.Item1, "fail", $"{outcome.Item2}; first online refresh: {online.Item1}; requests: {string.Join(", ", offlineTransport.Requests)}; the client always fetches timestamp.json"));
            }

            var replay = new CacheReplayTransport(new FileSystemTufCache(directory));
            using (var replayed = Client(fixture.Metadata, replay, new FileSystemTufCache(directory)))
            {
                var metadata = await Outcome(() => replayed.GetTrustedMetadataAsync());
                var target = await Outcome(() => replayed.GetTargetAsync("toolchains/recipes/fsharp/1.0.0.json"));
                results.Add(new(suite, "offline refresh and cached target by replaying the persisted metadata", "valid with no network request",
                    $"{metadata.Item1}; target {target.Item1}", metadata.Item1 == "valid" && target.Item1 == "valid" ? "pass (around)" : "fail",
                    $"CacheReplayTransport serves the cache back; transport requests (all answered from disk): {string.Join(", ", replay.Requests)}"));
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        var revocationCache = new RecordingCache();
        await Load(fixture.LearnedRevocation, revocationCache);
        var status = JsonNode.Parse(revocationCache.Stored["targets"])!["signed"]!["targets"]!["releases/morphir-dotnet-native-aot/1.2.3.json"]?["custom"]?["morphir"]?["status"]?.GetValue<string>();
        results.Add(new(suite, "learned revocation of morphir-dotnet-native-aot 1.2.3", "release_revoked, persisted", $"status '{status}' only in raw cached bytes", "not expressible",
            "TUF has no revocation state; the profile's status lives in custom.morphir, which the API never returns. Persisting and enforcing it is morphirup's job over re-parsed verified bytes"));

        var spelling = new FixtureTransport(v1.Repository);
        using (var client = Client(v1.Repository, spelling))
            await Outcome(() => client.GetTargetAsync("artifacts/desktop/1.0.0/windows-x86_64.zip"));
        var request = spelling.Requests.Last(r => r.StartsWith("target "));
        results.Add(new(suite, "consistent-snapshot target path the client requests", "artifacts/desktop/1.0.0/<sha256>.windows-x86_64.zip",
            request["target ".Length..], request.Contains('\\') ? "fail (Windows separator)" : "pass",
            "built with Path.GetDirectoryName, which uses \\ on Windows; System.Uri turns it into / for http(s), a custom transport sees \\"));

        return results;
    }
}
#endif
