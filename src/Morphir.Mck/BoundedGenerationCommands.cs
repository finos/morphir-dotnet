// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Morphir.BoundedGeneration;
using Morphir.Toolchain.IO;

namespace Morphir.Mck;

internal sealed class BoundedGenerationRunCommand : Command, IMckParseErrorHandler
{
    private readonly Action<string> _stdout;
    private readonly Option<string> _fixture = new("--fixture") { Required = true };
    private readonly Option<string> _adapter = new("--adapter") { Required = true, Description = "Absolute executable; no default adapter or shell" };
    private readonly Option<string[]> _arguments = new("--adapter-arg") { AllowMultipleArgumentsPerToken = false };
    private readonly Option<string> _binary = new("--adapter-binary") { Required = true, Description = "Absolute adapter entry artifact, executable or an argument such as a DLL/WASM file" };
    private readonly Option<string> _revision = new("--adapter-revision") { Required = true, Description = "Explicit source revision of the selected artifact" };
    internal BoundedGenerationRunCommand(Action<string> stdout) : base("run", "[experimental, internal] Run eight frozen small reader cases through one explicitly selected adapter")
    {
        _stdout = stdout;
        foreach (var option in new Option[] { _fixture, _adapter, _arguments, _binary, _revision }) Options.Add(option);
        SetAction(ExecuteAsync);
    }
    public int HandleParseErrors(ParseResult parsed)
    {
        _stdout(BoundedGenerationRun.Error(2, "mck.usage").Output);
        return 2;
    }
    private async Task<int> ExecuteAsync(ParseResult parsed, CancellationToken ct)
    {
        var selection = new BoundedRunSelection(parsed.GetValue(_fixture)!, parsed.GetValue(_adapter)!,
            parsed.GetValue(_arguments) ?? [], parsed.GetValue(_binary)!, parsed.GetValue(_revision)!);
        var result = await BoundedGenerationRun.RunAsync(selection, (request, cancellation) => AdapterCapture.RunAsync(
            selection.Executable, selection.Arguments, Path.GetFullPath(selection.Fixture), Encoding.UTF8.GetBytes(request.GetRawText()),
            TimeSpan.FromSeconds(10), BoundedGenerationRun.MaxObservationBytes, cancellation), ct).ConfigureAwait(false);
        _stdout(result.Output);
        return result.ExitCode;
    }
}

internal sealed class BoundedGenerationObserveCommand : Command, IMckParseErrorHandler
{
    private readonly Action<string> _stdout;
    internal BoundedGenerationObserveCommand(Action<string> stdout) : base("observe", "[experimental, internal] .NET reader adapter: one bounded stdin request, one stdout response")
    {
        _stdout = stdout;
        SetAction(ExecuteAsync);
    }
    public int HandleParseErrors(ParseResult parsed) => 2;
    private async Task<int> ExecuteAsync(ParseResult parsed, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var bytes = new MemoryStream();
            var chunk = new byte[4096];
            var input = Console.OpenStandardInput();
            int count;
            while ((count = await input.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (bytes.Length + count > 16384) throw new InvalidDataException("adapter.request_limit");
                bytes.Write(chunk, 0, count);
            }
            using var document = BoundedGenerationRun.Parse(bytes.ToArray());
            var request = document.RootElement;
            var response = Observe(request);
            if (Encoding.UTF8.GetByteCount(response) > BoundedGenerationRun.MaxObservationBytes) return 1;
            _stdout(response);
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return 1; }
    }
    // Only query locations cross this boundary; no expected semantic values are available to the adapter.
    internal static string Observe(JsonElement request)
    {
        if (request.GetProperty("scope").GetString() != "native-reader-adapter") throw new InvalidDataException("adapter.scope");
        var tree = request.GetProperty("treeRoot").GetString()!;
        if (!Path.IsPathFullyQualified(tree)) throw new InvalidDataException("adapter.tree_root");
        var snapshot = request.GetProperty("snapshot").GetString();
        if (BoundedGenerationRun.TreeDigest(tree) != snapshot) throw new InvalidDataException("adapter.snapshot_mismatch");
        var limit = request.GetProperty("documentLimit").GetInt64();
        var pageSize = request.GetProperty("pageSize").GetInt32();
        if (limit is < 1 or > 8192 || pageSize is < 1 or > 256) throw new InvalidDataException("adapter.limit");
        var factsElement = ReaderObservationAdapter.Observe(new ReaderDirectoryProvider(tree),
            new Func<string>(() => BoundedGenerationRun.TreeDigest(tree)), request.GetProperty("profile").GetString()!,
            request.GetProperty("selection").GetString()!, pageSize, limit);
        var facts = JsonNode.Parse(factsElement.GetRawText())!.AsObject();
        if (facts["irVersion"]!.GetValue<string>() != request.GetProperty("irVersion").GetString())
            throw new InvalidDataException("adapter.ir_version");
        var attributes = new JsonArray();
        foreach (var query in request.GetProperty("attributes").EnumerateArray())
        {
            var id = query.GetProperty("document").GetString()!;
            var pointer = query.GetProperty("pointer").GetString()!;
            if (!pointer.StartsWith('/')) throw new InvalidDataException("adapter.pointer");
            var value = factsElement.GetProperty("classicDocuments").GetProperty(id);
            foreach (var token in pointer[1..].Split('/'))
                value = value.ValueKind == JsonValueKind.Array ? value[int.Parse(token, System.Globalization.CultureInfo.InvariantCulture)]
                    : value.GetProperty(token.Replace("~1", "/").Replace("~0", "~"));
            attributes.Add(new JsonObject { ["document"] = id, ["pointer"] = pointer, ["value"] = JsonNode.Parse(value.GetRawText()) });
        }
        facts.Remove("classicDocuments");
        facts["classicAttributes"] = attributes;
        return new JsonObject
        {
            ["scope"] = "native-reader-adapter",
            ["caseId"] = request.GetProperty("caseId").GetString(),
            ["binding"] = JsonNode.Parse(request.GetProperty("binding").GetRawText()),
            ["facts"] = facts
        }.ToJsonString();
    }
}

// A read-only physical provider for this small dev adapter. The production tree reader owns layout,
// paging, document validation, decoding and cursor/revision checks. This type supplies storage only.
internal sealed class ReaderDirectoryProvider(string root) : IFileProvider
{
    private string Resolve(string subpath)
    {
        var path = Path.GetFullPath(Path.Combine(root, subpath.TrimStart('/')));
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(part => part == ".."))
            throw new InvalidDataException("adapter.path");
        return path;
    }
    public IFileInfo GetFileInfo(string subpath) => new ReaderFileInfo(Resolve(subpath));
    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var path = Resolve(subpath);
        return new ReaderDirectoryContents(Directory.Exists(path)
            ? Directory.EnumerateFileSystemEntries(path).Take(65).Select(p => (IFileInfo)new ReaderFileInfo(p)).ToArray() : null);
    }
    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    private sealed class ReaderFileInfo(string path) : IFileInfo
    {
        public bool Exists => File.Exists(path) || Directory.Exists(path);
        public bool IsDirectory => Directory.Exists(path);
        public long Length => IsDirectory ? -1 : new FileInfo(path).Length;
        public string PhysicalPath => path;
        public string Name => Path.GetFileName(path);
        public DateTimeOffset LastModified => File.GetLastWriteTimeUtc(path);
        public Stream CreateReadStream() => BoundedGenerationRun.OpenRegularFile(path);
    }
    private sealed class ReaderDirectoryContents(IFileInfo[]? entries) : IDirectoryContents
    {
        public bool Exists => entries is not null;
        public IEnumerator<IFileInfo> GetEnumerator() => ((IEnumerable<IFileInfo>)(entries ?? [])).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
