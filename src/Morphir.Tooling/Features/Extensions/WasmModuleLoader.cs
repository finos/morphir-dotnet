// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using Morphir.Extensions;
using Morphir.Extensions.Wasm;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// A configured WASM module to load: the <c>[extensions.&lt;name&gt;]</c> table's <c>path</c>, with its optional
/// <c>abi</c> and <c>sha256</c> (design CFG-1), already parsed.
/// </summary>
/// <param name="Extension">The extension's name, the <c>&lt;name&gt;</c> of its table.</param>
/// <param name="Path">
/// The module's fully qualified path. The loader resolves no relative path: that is the caller's job, by the rule
/// the configuration documents for <c>extensions.&lt;name&gt;.path</c>.
/// </param>
/// <param name="DeclaredAbi">The declared profile (<c>abi</c>), or null. A declaration only confirms the classified profile.</param>
/// <param name="DeclaredSha256">The declared digest (<c>sha256</c>), or null. Other bytes are refused before classification.</param>
public sealed record WasmModuleSource(string Extension, string Path, WasmAbi? DeclaredAbi = null, ModuleDigest? DeclaredSha256 = null)
{
    /// <summary>The extension's name.</summary>
    public string Extension { get; } = string.IsNullOrWhiteSpace(Extension)
        ? throw new ArgumentException("A WASM module source needs the extension's name.", nameof(Extension))
        : Extension;

    /// <summary>The module's fully qualified path.</summary>
    public string Path { get; } = System.IO.Path.IsPathFullyQualified(Path)
        ? Path
        : throw new ArgumentException($"A WASM module path must be fully qualified; '{Path}' is not.", nameof(Path));
}

/// <summary>A module the host loaded and admitted: classified as a profile this host runs, ready for a session.</summary>
public sealed class LoadedWasmModule
{
    internal LoadedWasmModule(string extension, string path, ExtismModule module)
    {
        Extension = extension;
        Path = path;
        Module = module;
    }

    /// <summary>The extension's name.</summary>
    public string Extension { get; }

    /// <summary>The path the bytes were read from.</summary>
    public string Path { get; }

    /// <summary>The admitted module, which <see cref="SessionStore.Open"/> takes.</summary>
    public ExtismModule Module { get; }

    /// <summary>The SHA-256 of the bytes that were read.</summary>
    public ModuleDigest Digest => Module.Compiled.Digest;

    /// <summary>The classified profile.</summary>
    public WasmAbi Profile => Module.Profile;

    /// <summary>The number of bytes that were read.</summary>
    public long ByteLength => Module.Compiled.ByteLength;
}

/// <summary>
/// Why a configured module was not loaded. <see cref="Message"/> names the extension and the path, and, once the
/// bytes are read, their SHA-256. Only <see cref="Refused"/> and <see cref="RuntimeUnavailable"/> have a
/// <see cref="Code"/>: the pinned profile contract defines refusal codes for a module, and
/// <c>extension.runtime_unavailable</c> for a host without a usable engine; it defines none for a file that is
/// missing, cannot be read, is not a module or has another digest than the declared one.
/// </summary>
public abstract record WasmModuleFailure
{
    private WasmModuleFailure(string extension, string path)
    {
        Extension = extension;
        Path = path;
    }

    /// <summary>The extension's name.</summary>
    public string Extension { get; }

    /// <summary>The configured path.</summary>
    public string Path { get; }

    /// <summary>The contract's refusal code, or null when the contract has none for this failure.</summary>
    public virtual string? Code => null;

    /// <summary>The diagnostic message.</summary>
    public abstract string Message { get; }

    /// <summary>
    /// The host's WASM runtime is unavailable, so the module was not read (requirement "Unavailable runtime fails by
    /// name"). The message names the host, the platform and the reason, and never a native library loading error.
    /// </summary>
    /// <param name="Host">The host that has no runtime.</param>
    /// <param name="Platform">The platform it runs on.</param>
    /// <param name="Reason">Why the runtime is unavailable.</param>
    public sealed record RuntimeUnavailable(string Extension, string Path, HostIdentity Host, string Platform, WasmRuntimeUnavailableReason Reason)
        : WasmModuleFailure(Extension, Path)
    {
        /// <inheritdoc />
        public override string? Code => WasmRuntimeUnavailable.Code;

        /// <inheritdoc />
        public override string Message =>
            $"The WASM module of extension '{Extension}' at '{Path}' cannot be loaded with {Code}: {WasmRuntimeUnavailable.Clause(Host, Platform, Reason)}.";
    }

    /// <summary>No file exists at the path.</summary>
    public sealed record NotFound(string Extension, string Path) : WasmModuleFailure(Extension, Path)
    {
        /// <inheritdoc />
        public override string Message =>
            $"The WASM module of extension '{Extension}' cannot be loaded: there is no file at '{Path}'.";
    }

    /// <summary>The path names something that cannot be read as a file: a directory, a denied file, an invalid path.</summary>
    /// <param name="Reason">What the file system reported.</param>
    public sealed record Unreadable(string Extension, string Path, string Reason) : WasmModuleFailure(Extension, Path)
    {
        /// <inheritdoc />
        public override string Message =>
            $"The WASM module of extension '{Extension}' at '{Path}' cannot be read: {Reason}";
    }

    /// <summary>The configuration declares a digest, and the bytes at the path have another. Nothing was classified.</summary>
    public sealed record DigestMismatch(string Extension, string Path, ModuleDigest Declared, ModuleDigest Actual)
        : WasmModuleFailure(Extension, Path)
    {
        /// <inheritdoc />
        public override string Message =>
            $"The WASM module of extension '{Extension}' at '{Path}' is refused: its SHA-256 is {Actual}, " +
            $"but the configuration declares sha256 = \"{Declared}\".";
    }

    /// <summary>The bytes at the path are not a WASM module, whatever the file is called.</summary>
    /// <param name="EngineReport">The engine's validation message.</param>
    public sealed record NotAModule(string Extension, string Path, ModuleDigest Digest, string EngineReport)
        : WasmModuleFailure(Extension, Path)
    {
        /// <inheritdoc />
        public override string Message =>
            $"The file '{Path}' of extension '{Extension}' is not a WASM module (SHA-256 {Digest}): {EngineReport}";
    }

    /// <summary>The bytes are a module, and the host refuses it before any request.</summary>
    /// <param name="Refusal">The refusal, with the contract's code.</param>
    public sealed record Refused(string Extension, string Path, ModuleDigest Digest, WasmRefusal Refusal)
        : WasmModuleFailure(Extension, Path)
    {
        /// <inheritdoc />
        public override string? Code => Refusal.Code;

        /// <inheritdoc />
        public override string Message =>
            $"The WASM module of extension '{Extension}' at '{Path}' (SHA-256 {Digest}) is refused with {Refusal.Code}: {Refusal.Message}";
    }
}

/// <summary>What loading a configured module gave.</summary>
public abstract record WasmModuleLoad
{
    private WasmModuleLoad() { }

    /// <summary>The module is loaded and admitted.</summary>
    public sealed record Loaded(LoadedWasmModule Module) : WasmModuleLoad;

    /// <summary>The module is not loaded.</summary>
    public sealed record Failed(WasmModuleFailure Failure) : WasmModuleLoad;

    /// <summary>Calls the function for this result.</summary>
    public TResult Match<TResult>(Func<LoadedWasmModule, TResult> loaded, Func<WasmModuleFailure, TResult> failed) => this switch
    {
        Loaded result => loaded(result.Module),
        Failed result => failed(result.Failure),
        _ => throw new UnreachableException(),
    };
}

/// <summary>Loads the WASM module a configured extension names (task 2.6, design CFG-1).</summary>
public interface IWasmModuleLoader
{
    /// <summary>Reads, checks, classifies and admits the module. Every outcome is a value; nothing is written.</summary>
    WasmModuleLoad Load(WasmModuleSource source);
}

/// <summary>
/// Loads a configured WASM module by its content. The checks run in this order, and the first that fails decides:
/// <list type="number">
/// <item>Ask the host's WASM runtime (<see cref="WasmRuntime"/>), before the file is read: an unavailable runtime is
/// <see cref="WasmModuleFailure.RuntimeUnavailable"/>, <c>extension.runtime_unavailable</c> with its reason. This is
/// the load that probes the engine, once for the process.</item>
/// <item>Read the whole file at <see cref="WasmModuleSource.Path"/>: <see cref="WasmModuleFailure.NotFound"/> or
/// <see cref="WasmModuleFailure.Unreadable"/>.</item>
/// <item>Compute the SHA-256 of the bytes. A declared digest that differs is
/// <see cref="WasmModuleFailure.DigestMismatch"/>, before the engine sees the bytes.</item>
/// <item>Validate and compile the bytes (<see cref="WasmEngine.Load"/>): bytes that are not a module are
/// <see cref="WasmModuleFailure.NotAModule"/>; a module that needs a feature the engine lacks is refused with
/// <c>extension.wasm_feature_unsupported</c>.</item>
/// <item>Classify against the pinned contract and admit (<see cref="WasmHost.Admit"/>): an import outside the
/// profile, a missing <c>handle</c> or a profile this host does not run is <c>extension.abi_unsupported</c>, and a
/// declared ABI that differs from the classified profile is <c>extension.abi_mismatch</c>.</item>
/// </list>
/// The file name and its extension play no part. The engine reuses a compiled module by the digest of its bytes,
/// never by path, so a module replaced on disk is read, hashed and classified again. The loader writes nothing to
/// stdout or stderr; it returns every diagnostic as a value, and a native library that cannot be loaded is the reason
/// of step 1, never an exception. The process's engine, <see cref="WasmEngine.Shared"/>, is created by the first load,
/// through the runtime's probe, not when the loader is created or registered.
/// </summary>
/// <param name="runtime">The host's WASM runtime: in the CLI, <see cref="WasmRuntime.Process"/>.</param>
/// <param name="host">The host, which a <see cref="WasmModuleFailure.RuntimeUnavailable"/> names.</param>
public sealed class WasmModuleLoader(WasmRuntime runtime, HostIdentity host) : IWasmModuleLoader
{
    /// <inheritdoc />
    public WasmModuleLoad Load(WasmModuleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (extension, path) = (source.Extension, source.Path);

        if (runtime.Availability.Match<WasmRuntimeUnavailableReason?>(available: () => null, unavailable: reason => reason) is { } unavailable)
            return Fail(new WasmModuleFailure.RuntimeUnavailable(extension, path, host, runtime.Platform, unavailable));

        byte[] bytes;
        try
        {
            if (Directory.Exists(path))
                return Fail(new WasmModuleFailure.Unreadable(extension, path, "the path is a directory, not a file."));
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Fail(new WasmModuleFailure.NotFound(extension, path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Fail(new WasmModuleFailure.Unreadable(extension, path, exception.Message));
        }

        var digest = ModuleDigest.Of(bytes);
        if (source.DeclaredSha256 is { } declared && declared != digest)
            return Fail(new WasmModuleFailure.DigestMismatch(extension, path, declared, digest));

        return WasmEngine.Shared.Load(bytes).Match(
            compiled => WasmHost.Admit(compiled, source.DeclaredAbi).Match(
                admitted => (WasmModuleLoad)new WasmModuleLoad.Loaded(new LoadedWasmModule(extension, path, admitted)),
                refusal => Fail(new WasmModuleFailure.Refused(extension, path, digest, refusal))),
            refusal => Fail(new WasmModuleFailure.Refused(extension, path, digest, refusal)),
            report => Fail(new WasmModuleFailure.NotAModule(extension, path, digest, report)));
    }

    private static WasmModuleLoad Fail(WasmModuleFailure failure) => new WasmModuleLoad.Failed(failure);
}
