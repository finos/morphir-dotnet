// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// The engine that runs WASM extensions in this host, as reports name it: the Wasmtime .NET package that
/// <c>Morphir.Extensions.Wasm</c> references. The worker's build identity covers it (task 4.1), and the runtime report
/// of task 4.2 (<c>morphir extension runtimes</c>) states it. A test checks the version against
/// <c>Directory.Packages.props</c> and against the Wasmtime assembly that is loaded, so a package update that forgets
/// this file fails.
/// </summary>
public static class WasmRuntimeIdentity
{
    /// <summary>The engine's name.</summary>
    public const string EngineName = "wasmtime";

    /// <summary>The engine's version: the version of the Wasmtime package, which is the version of its native library.</summary>
    public const string EngineVersion = "50.0.1";

    /// <summary>The engine as one text, for example <c>wasmtime 50.0.1</c>.</summary>
    public static string Engine => $"{EngineName} {EngineVersion}";
}
