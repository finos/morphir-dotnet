// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Wasm;

/// <summary>
/// Thrown by a host function to stop the call: the contract's "a host function that stops the call", fault kind
/// <c>failure</c>. Wasmtime returns it as the inner exception of the call's <c>WasmtimeException</c>, and the
/// session fails with <see cref="GuestFailureKind.IncompleteSession"/> and this message.
/// </summary>
internal sealed class HostCallStopped(string message) : Exception(message);

/// <summary>Thrown by WASI <c>proc_exit</c>: the guest ends the call with <paramref name="code"/>. Code 0 makes the
/// call succeed (contract policy <c>wasi.procExitZero</c>); any other code fails the session.</summary>
internal sealed class GuestExited(int code) : Exception($"the guest called proc_exit with code {code} during a call")
{
    public int Code { get; } = code;
}
