// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Wasm;

/// <summary>
/// Why the host refuses a module before any request, with the contract's refusal code and a message that
/// follows the contract's naming rules: an import is named <c>module.name</c>, a missing or mistyped
/// <c>handle</c> is reported as not exporting <c>handle</c> as <c>() -&gt; i32</c>, a profile is named by its
/// identifier. The module is never instantiated.
/// </summary>
public abstract record WasmRefusal
{
    /// <summary><c>extension.abi_unsupported</c>.</summary>
    public const string AbiUnsupportedCode = "extension.abi_unsupported";

    /// <summary><c>extension.abi_mismatch</c>.</summary>
    public const string AbiMismatchCode = "extension.abi_mismatch";

    /// <summary><c>extension.wasm_feature_unsupported</c>.</summary>
    public const string WasmFeatureUnsupportedCode = "extension.wasm_feature_unsupported";

    /// <summary>Every code a refusal can carry. Each must be a refusal code of the pinned contract.</summary>
    public static IReadOnlyList<string> Codes { get; } = [AbiUnsupportedCode, AbiMismatchCode, WasmFeatureUnsupportedCode];

    private WasmRefusal(string code) => Code = code;

    /// <summary>The contract's refusal code.</summary>
    public string Code { get; }

    /// <summary>The diagnostic message.</summary>
    public abstract string Message { get; }

    /// <summary>
    /// An import is not a function of the classified profile's import surface, or has another type there.
    /// </summary>
    /// <param name="Profile">The classified profile.</param>
    /// <param name="Import">The import, written <c>module.name</c> in the message.</param>
    /// <param name="Imported">What the module imports: its function type when the surface has the name with another type, else a description such as <c>a function (i32) -&gt; i32</c> or <c>a memory</c>.</param>
    /// <param name="SurfaceType">The type the surface has for that name, or null when the name is outside it.</param>
    public sealed record ImportOutsideSurface(WasmAbi Profile, WasmImportName Import, string Imported, WasmFunctionType? SurfaceType)
        : WasmRefusal(AbiUnsupportedCode)
    {
        /// <inheritdoc />
        public override string Message => SurfaceType is null
            ? $"the module imports {Import}, {Imported}, which is outside the {Profile} import surface"
            : $"the module imports {Import} as {Imported}; the {Profile} import surface has it as {SurfaceType}";
    }

    /// <summary>The module does not export <c>handle</c> with the contract's type.</summary>
    /// <param name="Name">The export name the contract requires, <c>handle</c>.</param>
    /// <param name="Required">The type the contract requires, <c>() -&gt; i32</c>.</param>
    /// <param name="Exported">What the module exports under that name, or null when it exports nothing by it.</param>
    public sealed record HandleExportMissing(string Name, WasmFunctionType Required, string? Exported)
        : WasmRefusal(AbiUnsupportedCode)
    {
        /// <inheritdoc />
        public override string Message => Exported is null
            ? $"the module does not export {Name} as {Required}"
            : $"the module does not export {Name} as {Required}; it exports {Name} as {Exported}";
    }

    /// <summary>A declared ABI differs from the classified profile. A declaration only confirms.</summary>
    /// <param name="Declared">The declared profile.</param>
    /// <param name="Classified">The profile the module's imports give it.</param>
    public sealed record DeclaredAbiMismatch(WasmAbi Declared, WasmAbi Classified) : WasmRefusal(AbiMismatchCode)
    {
        /// <inheritdoc />
        public override string Message => $"the module is declared as {Declared}, but its imports make it {Classified}";
    }

    /// <summary>The module is a profile this host does not run.</summary>
    /// <param name="Profile">The classified profile.</param>
    /// <param name="HostProfiles">The profiles this host runs.</param>
    public sealed record ProfileNotRun(WasmAbi Profile, IReadOnlyList<WasmAbi> HostProfiles) : WasmRefusal(AbiUnsupportedCode)
    {
        /// <inheritdoc />
        public override string Message =>
            $"the module is {Profile}, a profile this host does not run; this host runs Extism modules ({string.Join(", ", HostProfiles)})";
    }

    /// <summary>
    /// The module needs a WASM feature the engine lacks: a shared, 64-bit or second memory, which the engine is
    /// configured without, or another proposal the engine does not enable.
    /// </summary>
    /// <param name="EngineReport">The engine's validation message, which names the feature.</param>
    public sealed record FeatureUnsupported(string EngineReport) : WasmRefusal(WasmFeatureUnsupportedCode)
    {
        /// <inheritdoc />
        public override string Message => $"the module needs a WASM feature that the engine does not have: {EngineReport}";
    }
}
