// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open Org.BouncyCastle.Crypto.Parameters
open Org.BouncyCastle.Math.EC.Rfc8032

/// <summary>
/// Signature thresholds for the tool release metadata v1 profile: Ed25519 only, with exact key and signature sizes,
/// counting only keys of the role.
/// </summary>
/// <remarks>
/// SECURITY: this code has not had a security review. It must not be used to authenticate a production release until
/// it has. The primitive is BouncyCastle's RFC 8032 Ed25519, used directly through
/// <c>Ed25519PublicKeyParameters.Verify</c>, never through a factory.
/// </remarks>
module TufSignatures =
    /// <summary>Whether an Ed25519 signature over the message verifies under the public key (RFC 8032, pure Ed25519,
    /// no context). Anything but a 32-byte key and a 64-byte signature is refused before the primitive runs, and a
    /// key that is not a valid point fails.</summary>
    let verifyEd25519 (publicKey: byte array) (message: byte array) (signature: byte array) =
        publicKey.Length = 32
        && signature.Length = 64
        && (try
                Ed25519PublicKeyParameters(publicKey, 0)
                    .Verify(Ed25519.Algorithm.Ed25519, null, message, 0, message.Length, signature, 0)
            with :? ArgumentException ->
                false)

    /// <summary>Whether the role's threshold is met: the number of distinct public keys of the role, named by
    /// distinct key ids, whose signature over the document's canonical <c>signed</c> bytes verifies. A signature by a
    /// key the root does not list, or lists for another role, does not count. Two key ids naming one public key count
    /// once (provisional: the profile does not say).</summary>
    let thresholdMet (root: TufRoot) (role: TufRole) (document: TufDocument<'T>) =
        match root.Roles.TryFind role with
        | None -> false
        | Some keys ->
            let verified =
                document.Signatures
                |> List.filter (fun signature -> List.contains signature.KeyId keys.KeyIds)
                |> List.distinctBy _.KeyId
                |> List.choose (fun signature ->
                    match root.Keys.TryFind signature.KeyId with
                    | Some publicKey when verifyEd25519 publicKey document.Canonical signature.Value ->
                        Some(Convert.ToHexStringLower publicKey)
                    | _ -> None)
                |> List.distinct

            List.length verified >= keys.Threshold
