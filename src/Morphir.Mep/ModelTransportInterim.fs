// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

/// Interim tuple negotiation, replaced by ModelTransport.fs (task 4.3).
module ModelTransport =
    let select (offer: ModelTransportSupport) (support: ModelTransportSupport) : ModelTransportSupport =
        { Tuples = offer.Tuples |> Seq.filter (fun tuple -> support.Tuples |> Seq.contains tuple) |> Array.ofSeq
          Limits =
            { MaxEncodedBytes = min offer.Limits.MaxEncodedBytes support.Limits.MaxEncodedBytes
              MaxDecodedBytes = min offer.Limits.MaxDecodedBytes support.Limits.MaxDecodedBytes
              MaxDepth = min offer.Limits.MaxDepth support.Limits.MaxDepth
              MaxNodes = min offer.Limits.MaxNodes support.Limits.MaxNodes }
          Guarantees = offer.Guarantees |> Seq.filter (fun id -> support.Guarantees |> Seq.contains id) |> Array.ofSeq }

    let verifySelection (offer: ModelTransportSupport) (selection: ModelTransportSupport) : Result<ModelTransportSupport, string> =
        match selection.Tuples |> Seq.tryFind (fun tuple -> not (offer.Tuples |> Seq.contains tuple)) with
        | Some tuple -> Error $"The extension selected a tuple that the host did not offer: {tuple}."
        | None -> Ok selection
