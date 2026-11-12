// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Wasm;

/// <summary>
/// The codes a worker refuses with before any request that are not about the module: the pinned contract's
/// <c>worker.refusals</c>. <see cref="WasmProfileContract"/> refuses a contract whose list is not exactly
/// <see cref="Codes"/>, so the worker's codes and the contract's cannot drift apart. A module's own refusals are
/// <see cref="WasmRefusal.Codes"/>.
/// </summary>
public static class WasmWorkerRefusal
{
    /// <summary><c>extension.worker_runtime_mismatch</c>: <c>--build-identity</c> is not the worker's own.</summary>
    public const string RuntimeMismatchCode = "extension.worker_runtime_mismatch";

    /// <summary><c>extension.runtime_unavailable</c>: the host has no usable WASM engine. The message gives the reason.</summary>
    public const string RuntimeUnavailableCode = "extension.runtime_unavailable";

    /// <summary>Every code of <c>worker.refusals</c>, in the contract's order.</summary>
    public static IReadOnlyList<string> Codes { get; } = [RuntimeMismatchCode, RuntimeUnavailableCode];
}
