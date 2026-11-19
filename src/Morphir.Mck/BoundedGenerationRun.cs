// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Morphir.Toolchain.IO;

namespace Morphir.Mck;

internal sealed record BoundedRunSelection(string Fixture, string Executable, string[] Arguments, string Binary, string Revision);
internal sealed record BoundedRunResult(int ExitCode, string Output);

// This is the existing development MCK's native reader lane, not a second kit runner.
// Only the parent corpus supplies expectations. No Models/observer/IR codec call occurs here.
internal static class BoundedGenerationRun
{
    internal const int MaxObservationBytes = 65536;
    private const string CasesDigest = "fd72b801461873db04911ade8d4c5b9194da793a0c89cafc55be791290d1f8e4";
    private const string ExpectedDigest = "a5e585531daa19b5343130b5cd065eb8ac7ba5a7655d8ea21dc1d605fe134c58";
    private static readonly string[] Profiles = ["plainJson", "plainYaml", "contextualJson", "contextualYaml"];
    internal static JsonDocument Parse(byte[] bytes)
    {
        if (bytes.Length > MaxObservationBytes) throw new InvalidDataException("observation.too_large");
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32, AllowDuplicateProperties = false });
        try
        {
            var nodes = 0;
            void Shape(JsonElement node)
            {
                if (++nodes > 8192) throw new InvalidDataException("observation.too_many_nodes");
                if (node.ValueKind == JsonValueKind.Object)
                {
                    var properties = node.EnumerateObject().ToArray();
                    if (properties.Length > 64) throw new InvalidDataException("observation.too_many_properties");
                    foreach (var property in properties)
                    {
                        if (property.Name.Length > 4096) throw new InvalidDataException("observation.string_too_long");
                        Shape(property.Value);
                    }
                }
                else if (node.ValueKind == JsonValueKind.Array)
                {
                    if (node.GetArrayLength() > 256) throw new InvalidDataException("observation.array_too_long");
                    foreach (var value in node.EnumerateArray()) Shape(value);
                }
                else if (node.ValueKind == JsonValueKind.String && node.GetString()!.Length > 4096)
                    throw new InvalidDataException("observation.string_too_long");
            }
            Shape(document.RootElement);
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    internal static FileStream OpenRegularFile(string path)
    {
        if (!InputFileKind.IsRegular(path)) throw new InvalidDataException("input.not_regular");
        return File.OpenRead(path);
    }

    internal static byte[] ReadFile(string path, int limit)
    {
        using var file = OpenRegularFile(path);
        using var output = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = file.Read(chunk)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("input.too_large");
            output.Write(chunk, 0, read);
        }
        return output.ToArray();
    }
    internal static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string FileDigest(string path)
    {
        var file = new FileInfo(path);
        var target = file.ResolveLinkTarget(returnFinalTarget: true) ?? file;
        using var input = OpenRegularFile(target.FullName);
        if (input.Length > 512L * 1024 * 1024) throw new InvalidDataException("adapter.binary_too_large");
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
    internal static string TreeDigest(string root)
    {
        // Reject links and cap both storage metadata and bytes. Never follow a tree outside its root.
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var entries = 0;
        void Walk(string directory, int depth)
        {
            if (depth > 16 || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("fixture.invalid_directory");
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > 64) throw new InvalidDataException("fixture.too_many_entries");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("fixture.link");
                if ((attributes & FileAttributes.Directory) != 0) Walk(path, depth + 1);
                else
                {
                    if (files.Count == 9) throw new InvalidDataException("fixture.too_many_files");
                    files.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), Digest(ReadFile(path, 8192)));
                }
            }
        }
        Walk(root, 0);
        if (files.Count != 9) throw new InvalidDataException("fixture.incomplete_tree");
        return Digest(Encoding.UTF8.GetBytes(string.Concat(files.Select(file => $"{file.Key}\n{file.Value}\n"))));
    }
    private static string Under(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException("fixture.invalid_path");
        return Path.Combine(root, relative);
    }
    private static JsonNode Copy(JsonElement value) => JsonNode.Parse(value.GetRawText())!;
    private static JsonObject Normalize(JsonElement facts, JsonElement expected, string version)
    {
        var normalized = new JsonObject();
        foreach (var field in new[] { "distribution", "package", "pathBudget", "dependencies", "modules" })
            normalized[field] = Copy(facts.GetProperty(field));
        normalized["domainInventory"] = new JsonObject
        {
            ["pageSize"] = Copy(expected.GetProperty("domainInventory").GetProperty("pageSize")),
            ["pages"] = Copy(facts.GetProperty("pages")),
            ["finalPageBody"] = Copy(facts.GetProperty("finalPageBody"))
        };
        if (version == "3.1.0") normalized["classicAttributes"] = Copy(facts.GetProperty("classicAttributes"));
        return normalized;
    }
    internal static BoundedRunResult Error(int code, string reason) => Render(code, reason, null, new JsonArray(), false);
    private static BoundedRunResult Render(int exit, string? error, JsonObject? binding, JsonArray cases, bool passed)
    {
        var report = new JsonObject
        {
            ["scope"] = "native-reader-small",
            ["command"] = "mck bounded-generation run",
            ["exitCode"] = exit,
            ["error"] = error is null ? null : new JsonObject { ["code"] = error },
            ["binding"] = binding,
            ["correctness"] = new JsonObject { ["state"] = passed ? "passed" : "failed" },
            ["resources"] = new JsonObject { ["state"] = "not-run", ["reasons"] = new JsonArray("resource.small_case") },
            ["cases"] = cases
        };
        return new(exit, report.ToJsonString());
    }
    internal static async Task<BoundedRunResult> RunAsync(BoundedRunSelection selection,
        Func<JsonElement, CancellationToken, Task<byte[]>> invoke, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(selection.Executable) || !Path.IsPathFullyQualified(selection.Binary)
            || (selection.Binary != selection.Executable && !selection.Arguments.Contains(selection.Binary, StringComparer.Ordinal))
            || selection.Revision.Length is < 1 or > 128 || selection.Arguments.Length > 32
            || selection.Arguments.Sum(a => Encoding.UTF8.GetByteCount(a)) > 8192
            || selection.Arguments.Any(a => a.Length > 4096 || a.Contains('\0')))
            return Error(2, "mck.usage");
        var results = new JsonArray();
        JsonObject? binding = null;
        try
        {
            var root = Path.GetFullPath(selection.Fixture);
            var casesBytes = ReadFile(Path.Combine(root, "cases.json"), MaxObservationBytes);
            var expectedBytes = ReadFile(Path.Combine(root, "expected.json"), MaxObservationBytes);
            if (Digest(casesBytes) != CasesDigest || Digest(expectedBytes) != ExpectedDigest)
                return Error(1, "case_expectation_source_mismatch");
            using var corpus = Parse(casesBytes);
            using var expectedFile = Parse(expectedBytes);
            var expected = expectedFile.RootElement;
            binding = new JsonObject
            {
                ["casesSha256"] = CasesDigest,
                ["expectedSha256"] = ExpectedDigest,
                ["adapterRevision"] = selection.Revision,
                ["executable"] = selection.Executable,
                ["executableSha256"] = FileDigest(selection.Executable),
                ["binary"] = selection.Binary,
                ["binarySha256"] = FileDigest(selection.Binary),
                ["arguments"] = new JsonArray(selection.Arguments.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray())
            };
            var finalDocument = expected.GetProperty("domainInventory").GetProperty("finalPageBody").GetProperty("document").GetString();
            var module = expected.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("members").EnumerateArray()
                .Any(member => member.GetProperty("document").GetString() == finalDocument)).GetProperty("document").GetString();
            foreach (var version in corpus.RootElement.GetProperty("versions").EnumerateArray())
                foreach (var profile in Profiles)
                {
                    ct.ThrowIfCancellationRequested();
                    var irVersion = version.GetProperty("irVersion").GetString()!;
                    var tree = Under(root, version.GetProperty("trees").GetProperty(profile).GetString()!);
                    var snapshot = TreeDigest(tree);
                    var id = $"acme-orders/{irVersion}/{profile}";
                    var attributes = new JsonArray();
                    if (irVersion == "3.1.0")
                        foreach (var attribute in expected.GetProperty("classicAttributes").EnumerateArray())
                            attributes.Add(new JsonObject { ["document"] = Copy(attribute.GetProperty("document")), ["pointer"] = Copy(attribute.GetProperty("pointer")) });
                    var request = new JsonObject
                    {
                        ["scope"] = "native-reader-adapter",
                        ["caseId"] = id,
                        ["binding"] = binding.DeepClone(),
                        ["treeRoot"] = tree,
                        ["snapshot"] = snapshot,
                        ["irVersion"] = irVersion,
                        ["profile"] = profile,
                        ["selection"] = module,
                        ["pageSize"] = Copy(expected.GetProperty("domainInventory").GetProperty("pageSize")),
                        ["documentLimit"] = 8192,
                        ["attributes"] = attributes
                    };
                    var reason = "";
                    string? observedDigest = null;
                    try
                    {
                        if (FileDigest(selection.Binary) != binding["binarySha256"]!.GetValue<string>()
                            || FileDigest(selection.Executable) != binding["executableSha256"]!.GetValue<string>())
                            throw new InvalidDataException("adapter.binary_changed");
                        var requestBytes = Encoding.UTF8.GetBytes(request.ToJsonString());
                        if (requestBytes.Length > 16384) throw new InvalidDataException("adapter.request_limit");
                        using var requestDocument = Parse(requestBytes);
                        var bytes = await invoke(requestDocument.RootElement, ct).ConfigureAwait(false);
                        using var response = Parse(bytes);
                        var reply = response.RootElement;
                        if (reply.GetProperty("scope").GetString() != "native-reader-adapter" || reply.GetProperty("caseId").GetString() != id
                            || !JsonElement.DeepEquals(reply.GetProperty("binding"), requestDocument.RootElement.GetProperty("binding")))
                            throw new InvalidDataException("adapter.identity_mismatch");
                        var facts = reply.GetProperty("facts");
                        if (facts.GetProperty("scope").GetString() != "native-reader" || facts.GetProperty("revision").GetString() != snapshot
                            || facts.GetProperty("irVersion").GetString() != irVersion)
                            throw new InvalidDataException("adapter.snapshot_mismatch");
                        var normalized = Normalize(facts, expected, irVersion);
                        var oracle = Copy(expected).AsObject();
                        if (irVersion != "3.1.0") oracle.Remove("classicAttributes");
                        observedDigest = Digest(Encoding.UTF8.GetBytes(normalized.ToJsonString()));
                        if (!JsonNode.DeepEquals(normalized, oracle)) reason = "correctness.mismatch";
                        if (TreeDigest(tree) != snapshot || FileDigest(selection.Binary) != binding["binarySha256"]!.GetValue<string>()
                            || FileDigest(selection.Executable) != binding["executableSha256"]!.GetValue<string>())
                            reason = "adapter.input_or_binary_changed";
                    }
                    catch (Exception error) when (AdapterCapture.SettlementTimedOut(error))
                    { reason = "adapter.cleanup_timeout"; }
                    catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or IOException or InvalidDataException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
                    { reason = error is InvalidDataException ? error.Message : "adapter.invalid_observation"; }
                    results.Add(new JsonObject
                    {
                        ["caseId"] = id,
                        ["snapshot"] = snapshot,
                        ["correctness"] = reason == "" ? "passed" : "failed",
                        ["reason"] = reason == "" ? null : reason,
                        ["observationSha256"] = observedDigest
                    });
                    if (reason == "adapter.cleanup_timeout")
                        return Render(1, null, binding, results, false);
                }
            var passed = results.Count == 8 && results.All(c => c!["correctness"]!.GetValue<string>() == "passed");
            return Render(passed ? 0 : 1, null, binding, results, passed);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return Render(1, "adapter.admission_failed", binding, results, false); }
    }
}
