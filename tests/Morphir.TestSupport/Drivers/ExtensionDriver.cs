// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>The options of <c>extension list</c>.</summary>
public sealed record ExtensionListOptions
{
    /// <summary>Write one JSON report to standard output. Default true.</summary>
    public bool Json { get; init; } = true;

    /// <summary>List only the extensions of this machine (<c>--local</c>).</summary>
    public bool Local { get; init; }

    /// <summary>The platform of the list (<c>--platform</c>), for example <c>linux-x64</c>.</summary>
    public string? Platform { get; init; }
}

/// <summary>An extension guest that the development build builds (<c>build/Morphir.ExtensionArtifacts.targets</c>).</summary>
public enum BuiltExtension
{
    /// <summary>The F# backend guest, <c>morphir-fsharp-backend</c> of <c>src/Morphir.CodeGeneration.FSharp.Extension</c>.</summary>
    FSharpBackend,

    /// <summary>The stub of the external tool <c>morphir-mck</c> (<c>tests/Morphir.Mck.Stub</c>), which <c>morphir mck</c> runs.</summary>
    MckStub,
}

/// <summary>
/// The files of the built extension guests, from the assembly metadata that Morphir.ExtensionArtifacts.targets
/// writes. A guest with no file is an error with the command that builds it: no other file is used.
/// </summary>
public sealed class ExtensionArtifacts
{
    private const string MetadataPrefix = "Morphir.TestSupport.Extension.";

    private static readonly IReadOnlyDictionary<BuiltExtension, string> Projects = new Dictionary<BuiltExtension, string>
    {
        [BuiltExtension.FSharpBackend] = "src/Morphir.CodeGeneration.FSharp.Extension/Morphir.CodeGeneration.FSharp.Extension.fsproj",
        [BuiltExtension.MckStub] = "tests/Morphir.Mck.Stub/Morphir.Mck.Stub.csproj",
    };

    private readonly IReadOnlyDictionary<string, string> _metadata;
    private readonly Func<string, bool> _fileExists;

    private ExtensionArtifacts(IReadOnlyDictionary<string, string> metadata, Func<string, bool> fileExists)
    {
        _metadata = metadata;
        _fileExists = fileExists;
    }

    /// <summary>No built guest: the artifacts of a target other than the development build.</summary>
    public static ExtensionArtifacts None { get; } = new(new Dictionary<string, string>(), _ => false);

    /// <summary>The built guests of <paramref name="testAssembly"/>, which imports Morphir.ExtensionArtifacts.targets.</summary>
    public static ExtensionArtifacts OfTestAssembly(Assembly testAssembly) =>
        Of(testAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal) && attribute.Value is not null)
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!), File.Exists);

    public static ExtensionArtifacts Of(IReadOnlyDictionary<string, string> metadata, Func<string, bool> fileExists) => new(metadata, fileExists);

    /// <summary>The file of <paramref name="guest"/>, or an error with the command that builds it.</summary>
    public string PathOf(BuiltExtension guest)
    {
        if (!_metadata.TryGetValue(MetadataPrefix + guest, out var path))
            throw new TestSessionException($"The test assembly has no path of the built extension {guest}. Import tests/Morphir.TestSupport/build/Morphir.ExtensionArtifacts.targets in its project.");
        return _fileExists(path)
            ? path
            : throw new TestSessionException($"The built extension {guest} has no file at {path}. Build it: dotnet build {Projects[guest]}");
    }
}

/// <summary>The <c>extension</c> commands as intents: list the extensions of the host, and report its runtimes.</summary>
public interface IExtensionDriver
{
    /// <summary>The file of a built extension guest, to give to a command (for example <c>generate --extension</c>).</summary>
    string BuiltExtension(BuiltExtension guest);

    Task<CommandResult> ListAsync(ExtensionListOptions? options = null, CancellationToken cancellation = default);

    /// <summary>Runs <c>extension runtimes</c>, with <c>--json</c> when <paramref name="json"/> is true.</summary>
    Task<CommandResult> RuntimesAsync(bool json = true, CancellationToken cancellation = default);
}

/// <summary>Builds the command lines of <c>extension</c> and gives them to the CLI driver.</summary>
public sealed class ExtensionDriver(ICliDriver cli, ExtensionArtifacts? guests = null) : IExtensionDriver
{
    public string BuiltExtension(BuiltExtension guest) => (guests ?? ExtensionArtifacts.None).PathOf(guest);

    public Task<CommandResult> ListAsync(ExtensionListOptions? options = null, CancellationToken cancellation = default)
    {
        options ??= new ExtensionListOptions();
        var arguments = new List<string> { "extension", "list" };
        if (options.Local) arguments.Add("--local");
        if (options.Platform is { } platform)
        {
            arguments.Add("--platform");
            arguments.Add(platform);
        }

        if (options.Json) arguments.Add("--json");
        return cli.RunAsync(arguments, cancellation: cancellation);
    }

    public Task<CommandResult> RuntimesAsync(bool json = true, CancellationToken cancellation = default) =>
        cli.RunAsync(json ? ["extension", "runtimes", "--json"] : ["extension", "runtimes"], cancellation: cancellation);
}

/// <summary>One extension of an extension list report.</summary>
public sealed record ListedExtension(string Id, bool Available, string? Reason);

/// <summary>
/// The JSON report of <c>extension list --json</c>: the host and the extensions that it has. It has its own form:
/// no field <c>success</c>, because a list does not fail.
/// </summary>
public sealed class ExtensionListReport : JsonCommandReport
{
    private ExtensionListReport(CommandResult result) : base(result, "extension list")
    {
        Extensions = Required("extensions") is { ValueKind: JsonValueKind.Array } list
            ? [.. list.EnumerateArray().Select(item => new ListedExtension(
                item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : "",
                item.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.True,
                item.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null))]
            : throw new CommandAssertionException("The extension list report has no list extensions.", result);
    }

    /// <summary>The command name of the host, for example <c>morphir</c>.</summary>
    public string? Host => Text("host");

    /// <summary>How the host runs: <c>jit</c> or <c>aot</c>.</summary>
    public string? Execution => Text("execution");

    /// <summary>True when the report states a deployment. A development build states none.</summary>
    public bool HasDeployment => Member("deployment") is not null;

    public IReadOnlyList<ListedExtension> Extensions { get; }

    /// <summary>Reads the report from standard output, which must be exactly one JSON document.</summary>
    public static ExtensionListReport Of(CommandResult result) => new(result);
}

/// <summary>One runtime of an extension runtimes report.</summary>
public sealed record ExtensionRuntime(string Kind, bool Available, string? Reason, string? EngineName, string? EngineVersion);

/// <summary>The JSON report of <c>extension runtimes --json</c>: the host, its platform, and its extension runtimes.</summary>
public sealed class ExtensionRuntimesReport : JsonCommandReport
{
    private ExtensionRuntimesReport(CommandResult result) : base(result, "extension runtimes")
    {
        Runtimes = Required("runtimes") is { ValueKind: JsonValueKind.Array } list
            ? [.. list.EnumerateArray().Select(Runtime)]
            : throw new CommandAssertionException("The extension runtimes report has no list runtimes.", result);
    }

    public string? Host => Text("host");

    /// <summary>The runtime identifier of the host process, for example <c>osx-arm64</c>.</summary>
    public string? Platform => Text("platform");

    public IReadOnlyList<ExtensionRuntime> Runtimes { get; }

    /// <summary>Reads the report from standard output, which must be exactly one JSON document.</summary>
    public static ExtensionRuntimesReport Of(CommandResult result) => new(result);

    private static ExtensionRuntime Runtime(JsonElement item)
    {
        static string? Field(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        var engine = item.TryGetProperty("engine", out var value) ? value : default;
        return new ExtensionRuntime(
            Field(item, "kind") ?? "",
            item.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.True,
            Field(item, "reason"),
            Field(engine, "name"),
            Field(engine, "version"));
    }
}

/// <summary>
/// The platform of this machine, as the extension reports of the product name it: <c>win</c>, <c>osx</c>,
/// <c>linux</c> or <c>linux-musl</c>, then the architecture of the process, for example <c>osx-arm64</c>.
/// </summary>
public static class ExtensionPlatform
{
    public static string OfThisMachine { get; } = Of(
        OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsLinux() ? "linux" : RuntimeInformation.OSDescription.ToLowerInvariant(),
        RuntimeInformation.ProcessArchitecture,
        RuntimeInformation.RuntimeIdentifier);

    /// <summary>The platform name of a system, a process architecture and the runtime identifier of the runtime.</summary>
    public static string Of(string system, Architecture architecture, string runtimeIdentifier)
    {
        if (system == "linux" && runtimeIdentifier.Contains("musl", StringComparison.OrdinalIgnoreCase)) system = "linux-musl";
        return $"{system}-{architecture.ToString().ToLowerInvariant()}";
    }

    /// <summary>The platform on which the WASM runtime of the product is refused before it loads (issue #609).</summary>
    public const string WithoutWasm = "win-x64";
}

