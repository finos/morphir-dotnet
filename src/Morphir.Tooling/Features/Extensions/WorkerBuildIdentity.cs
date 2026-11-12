// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using Morphir.Extensions;
using Morphir.Extensions.Wasm;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// The build identity of this host's worker mode (task 4.1): 64 lower-case hexadecimal digits, the SHA-256 of
/// <see cref="Text"/>. <c>morphir extension worker --help --json</c> reports it as <c>result.buildIdentity</c>, and a
/// worker started with another <c>--build-identity</c> refuses with <c>extension.worker_runtime_mismatch</c> before it
/// reads the module.
/// </summary>
/// <remarks>
/// <para>
/// The pinned contract fixes only the form, "the host's build identity, 64 hexadecimal digits". What it covers follows
/// the MoonBit worker at <c>89f8924</c> (<c>worker/wasm/native/identity.mbt</c>), which hashes its worker contract
/// version, its host version, the build's version and git commit, the Extism kernel's SHA-256 and the engine's
/// features. Here the parts are: the pinned profile contract (id, version and SHA-256, since it holds the worker's
/// rules), the host (<c>morphir</c> or <c>dotnet-morphir</c>), the release version with its build metadata (a CI or
/// release build carries the git commit there), the embedded kernel and the engine (<see cref="WasmRuntimeIdentity"/>).
/// </para>
/// <para>
/// The same limit as MoonBit's applies: two builds of one version and commit share an identity, also when one has
/// uncommitted changes. Release and CI builds start from a clean checkout.
/// </para>
/// </remarks>
public static class WorkerBuildIdentity
{
    /// <summary>The text whose SHA-256 is the identity, one part per line.</summary>
    public static string Text(HostIdentity host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var contract = WasmProfileContract.Pinned;
        return $"worker {contract.Id} {contract.Version} sha256 {contract.Sha256}\n" +
            $"host {host.Host.WireValue}\n" +
            $"version {host.Version}\n" +
            $"kernel moonrockz/extism {ExtismKernel.PackageVersion} sha256 {ExtismKernel.Sha256}\n" +
            $"engine {WasmRuntimeIdentity.Engine}\n";
    }

    /// <summary>The identity of <paramref name="host"/>'s build: the SHA-256 of <see cref="Text"/>, in lower-case hexadecimal.</summary>
    public static string Of(HostIdentity host) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Text(host))));
}
