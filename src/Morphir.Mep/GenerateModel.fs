// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Generic

/// A morphir.backend.generateModel request that passed the guest check: the request, its admitted model and the
/// guarantees that the model's evidence establishes. Generation reads the model bytes from Model.
type AdmittedGenerateRequest =
    {
        /// The request as it was received.
        Request: GenerateModelRequest
        /// The admitted model, in the input tuple.
        Model: AdmittedModel
        /// The established required guarantees, in request order.
        Guarantees: IReadOnlyList<EstablishedGuarantee>
    }

    /// Stable text for diagnostics.
    override this.ToString() = $"Admitted {this.Request}"

/// Rules of morphir.backend.generateModel that the generated codecs cannot express: the guest check of a request
/// before generation.
[<RequireQualifiedAccess>]
module GenerateModel =

    /// Checks that the input of a request is a generateModel tuple that the session agrees. A tuple of another
    /// operation is refused even when the session agrees it for that operation.
    [<CompiledName "CheckInput">]
    let checkInput
        (agreed: ModelTransportSupport option)
        (request: GenerateModelRequest)
        : Result<ModelTransportTuple, AdmissionFailure> =
        if request.Input.Operation <> ModelOperation.GenerateModel then
            Error(AdmissionFailure.UnsupportedTuple request.Input)
        else
            ModelTransport.checkTuple agreed request.Input

    /// The guest check of a request, in the spec order, stopping at the first failure: the input tuple; the
    /// envelope against that tuple and the agreed limits (ModelTransport.admit); then each required guarantee from
    /// the model's evidence, the request contextDigest and the trust policy (ModelTransport.establish). Every
    /// failure is a -32602 at the protocol boundary (AdmissionFailure.ToRpcError).
    [<CompiledName "Admit">]
    let admit
        (agreed: ModelTransportSupport option)
        (trust: Func<ModelGuaranteeEvidence, bool>)
        (request: GenerateModelRequest)
        : Result<AdmittedGenerateRequest, AdmissionFailure> =
        match checkInput agreed request, agreed with
        | Error failure, _ -> Error failure
        | Ok _, None -> Error(AdmissionFailure.UnsupportedTuple request.Input)
        | Ok input, Some support ->
            ModelTransport.admit input support.Limits request.Model
            |> Result.bind (fun admitted ->
                ModelTransport.establish support request.ContextDigest request.RequiredGuarantees trust admitted
                |> Result.map (fun established ->
                    { Request = request
                      Model = admitted
                      Guarantees = established }))
