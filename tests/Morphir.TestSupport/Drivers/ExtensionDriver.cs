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

    /// <summary>The platform of this machine as the extension reports name it (<see cref="ExtensionPlatform.OfThisMachine"/>).</summary>
    string Platform { get; }

    /// <summary>True when the product refuses the WASM runtime on this machine before it loads (<see cref="ExtensionPlatform.WithoutWasm"/>).</summary>
    bool PlatformHasNoWasm { get; }

    Task<CommandResult> ListAsync(ExtensionListOptions? options = null, CancellationToken cancellation = default);

    /// <summary>Runs <c>extension runtimes</c>, with <c>--json</c> when <paramref name="json"/> is true.</summary>
    Task<CommandResult> RuntimesAsync(bool json = true, CancellationToken cancellation = default);
}

/// <summary>Builds the command lines of <c>extension</c> and gives them to the CLI driver.</summary>
public sealed class ExtensionDriver(ICliDriver cli, ExtensionArtifacts? guests = null) : IExtensionDriver
{
    public string BuiltExtension(BuiltExtension guest) => (guests ?? ExtensionArtifacts.None).PathOf(guest);

    public string Platform => ExtensionPlatform.OfThisMachine;

    public bool PlatformHasNoWasm => ExtensionPlatform.OfThisMachine == ExtensionPlatform.WithoutWasm;

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
/// <param name="Reason">The reason of the entry, or null when the entry has no member <c>reason</c> or its value is null.</param>
public sealed record ListedExtension(string Id, bool Available, string? Reason)
{
    /// <summary>The types of the entry, for example <c>validator</c>.</summary>
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>The delivery of the entry, for example <c>built-in</c>.</summary>
    public string? Delivery { get; init; }

    /// <summary>The command names of the hosts that have the entry.</summary>
    public IReadOnlyList<string> Hosts { get; init; } = [];

    /// <summary>True when the entry has the member <c>reason</c>, also with a null value.</summary>
    public bool HasReason { get; init; }

    /// <summary>The IR versions of the validator capability of the entry, or null when the entry claims no validator.</summary>
    public IReadOnlyList<string>? ValidatorIrVersions { get; init; }

    /// <summary>The JSON text of the entry, to compare the entries of two reports.</summary>
    public string RawJson { get; init; } = "";
}

/// <summary>
/// The JSON report of <c>extension list --json</c>: the host and the extensions that it has. It has its own form:
/// no field <c>success</c>, because a list does not fail.
/// </summary>
public sealed class ExtensionListReport : JsonCommandReport
{
    private ExtensionListReport(CommandResult result) : base(result, "extension list")
    {
        Extensions = Required("extensions") is { ValueKind: JsonValueKind.Array } list
            ? [.. list.EnumerateArray().Select(Entry)]
            : throw new CommandAssertionException("The extension list report has no list extensions.", result);
    }

    /// <summary>The arguments of <c>extension list --json</c>.</summary>
    public static IReadOnlyList<string> Arguments { get; } = ["extension", "list", "--json"];

    /// <summary>The command name of the host, for example <c>morphir</c>.</summary>
    public string? Host => Text("host");

    /// <summary>How the host runs: <c>jit</c> or <c>aot</c>.</summary>
    public string? Execution => Text("execution");

    /// <summary>True when the report states a deployment. A development build states none.</summary>
    public bool HasDeployment => Member("deployment") is not null;

    /// <summary>The deployment of the build, or null when the report states none.</summary>
    public string? Deployment => Text("deployment");

    public IReadOnlyList<ListedExtension> Extensions { get; }

    /// <summary>Reads the report from standard output, which must be exactly one JSON document.</summary>
    public static ExtensionListReport Of(CommandResult result) => new(result);

    /// <summary>The entry with <paramref name="id"/>; a report with no such entry is a failure of the check.</summary>
    public ListedExtension Extension(string id) =>
        Extensions.SingleOrDefault(entry => entry.Id == id)
        ?? throw new CommandAssertionException($"The extension list has no entry '{id}'.", Result);

    /// <summary>The executable name of a host in a report: <c>morphir</c>, or <c>dotnet-morphir</c> for the tool host.</summary>
    public static string HostName(MorphirHost host) => host == MorphirHost.DotnetTool ? "dotnet-morphir" : "morphir";

    /// <summary>
    /// The deployment and the execution that the build of a target reports. The Native AOT executable is published
    /// with the settings of the native-aot profile, and each packaged offering is the output of its own release
    /// profile. The development build, the single-file executables and an executable at a path name no profile.
    /// </summary>
    public static (string? Deployment, string Execution) ExpectedDeploymentOf(TestTarget target) => target switch
    {
        TestTarget.NativeAot => ("native-aot", "native-aot"),
        TestTarget.Packaged packaged => (packaged.Name, packaged.Offering == PackagedOffering.NativeAot ? "native-aot" : "jit"),
        _ => (null, "jit"),
    };

    private static ListedExtension Entry(JsonElement item)
    {
        static IReadOnlyList<string> Texts(JsonElement element, string name) =>
            element.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
                ? [.. list.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!)]
                : [];

        return new ListedExtension(
            item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : "",
            item.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.True,
            item.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null)
        {
            Types = Texts(item, "types"),
            Delivery = item.TryGetProperty("delivery", out var delivery) && delivery.ValueKind == JsonValueKind.String ? delivery.GetString() : null,
            Hosts = Texts(item, "hosts"),
            HasReason = item.TryGetProperty("reason", out _),
            ValidatorIrVersions = item.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Object
                && capabilities.TryGetProperty("validator", out var validator) && validator.ValueKind == JsonValueKind.Object
                    ? Texts(validator, "irVersions")
                    : null,
            RawJson = item.GetRawText(),
        };
    }
}

/// <summary>
/// One runtime of an extension runtimes report. A runtime that is not available can have no profiles and no bounds:
/// <see cref="Profiles"/> is then empty and <see cref="MemoryBytes"/> null.
/// </summary>
public sealed record ExtensionRuntime(string Kind, bool Available, string? Reason, string? EngineName, string? EngineVersion)
{
    /// <summary>The profiles of the runtime, for example <c>extism-kernel-v1</c>.</summary>
    public IReadOnlyList<string> Profiles { get; init; } = [];

    /// <summary>The memory bound of a guest, in bytes (<c>bounds.memoryBytes</c>), or null when the runtime states none.</summary>
    public long? MemoryBytes { get; init; }
}

/// <summary>The JSON report of <c>extension runtimes --json</c>: the host, its platform, and its extension runtimes.</summary>
public sealed class ExtensionRuntimesReport : JsonCommandReport
{
    /// <summary>The reason of a host that cannot run guests on Windows x64 (#609).</summary>
    public const string WindowsX64Reason = "win_x64_trap_unsupported";

    private ExtensionRuntimesReport(CommandResult result) : base(result, "extension runtimes")
    {
        Runtimes = Required("runtimes") is { ValueKind: JsonValueKind.Array } list
            ? [.. list.EnumerateArray().Select(Runtime)]
            : throw new CommandAssertionException("The extension runtimes report has no list runtimes.", result);
    }

    /// <summary>The arguments of <c>extension runtimes --json</c>.</summary>
    public static IReadOnlyList<string> Arguments { get; } = ["extension", "runtimes", "--json"];

    public string? Host => Text("host");

    /// <summary>The runtime identifier of the host process, for example <c>osx-arm64</c>.</summary>
    public string? Platform => Text("platform");

    public IReadOnlyList<ExtensionRuntime> Runtimes { get; }

    /// <summary>The one runtime of the kind <c>wasm</c>; a report with no such runtime, or with more, is a failure of the check.</summary>
    public ExtensionRuntime Wasm => Runtimes.Count(runtime => runtime.Kind == "wasm") == 1
        ? Runtimes.Single(runtime => runtime.Kind == "wasm")
        : throw new CommandAssertionException($"The runtime report has {Runtimes.Count(runtime => runtime.Kind == "wasm")} runtimes of the kind wasm, not one.", Result);

    /// <summary>True on Windows x64, where the host reports that it cannot run guests (#609).</summary>
    public bool IsWindowsX64 => Platform == "win-x64";

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
            Field(engine, "version"))
        {
            Profiles = item.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array
                ? [.. profiles.EnumerateArray().Where(profile => profile.ValueKind == JsonValueKind.String).Select(profile => profile.GetString()!)]
                : [],
            MemoryBytes = item.TryGetProperty("bounds", out var bounds) && bounds.ValueKind == JsonValueKind.Object
                && bounds.TryGetProperty("memoryBytes", out var memory) && memory.ValueKind == JsonValueKind.Number
                    ? memory.GetInt64()
                    : null,
        };
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
        // The rule of the product (WasmRuntimeProbe.UsesMusl): a musl or an Alpine runtime identifier.
        if (system == "linux" && (runtimeIdentifier.Contains("musl", StringComparison.OrdinalIgnoreCase)
            || runtimeIdentifier.StartsWith("alpine", StringComparison.OrdinalIgnoreCase))) system = "linux-musl";
        return $"{system}-{architecture.ToString().ToLowerInvariant()}";
    }

    /// <summary>The platform on which the WASM runtime of the product is refused before it loads (issue #609).</summary>
    public const string WithoutWasm = "win-x64";
}
