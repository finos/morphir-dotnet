// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// A test-only copy of the group 2 codecs, extended with the items that the validateModel proposal adds
/// (docs/content/contributing/design/proposals/mep-validate-model.md). It is not part of Morphir.Mep.
namespace Morphir.Mep.Tests.Proposal

open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization
open Morphir.Myriad.SystemTextJson

/// ModelOperation with the proposed validateModel value.
[<RequireQualifiedAccess>]
type ProposalOperation =
    | [<JsonName "morphir.frontend.compileModel">] CompileModel
    | [<JsonName "morphir.backend.generateModel">] GenerateModel
    | [<JsonName "morphir.validator.validateModel">] ValidateModel

type ProposalTuple =
    { Operation: ProposalOperation
      Profile: string
      Codec: string
      EnvelopeVersion: string
      IrVersion: string }

type ProposalLimits =
    { MaxEncodedBytes: int
      MaxDecodedBytes: int
      MaxDepth: int
      MaxNodes: int }

type ProposalSupport =
    { Tuples: IReadOnlyList<ProposalTuple>
      Limits: ProposalLimits
      [<JsonDefaultEmpty>]
      Guarantees: IReadOnlyList<string> }

/// The proposed validator capability.
type ValidatorCapability = { IrVersions: IReadOnlyList<string> }

/// ExtensionCapabilities with the proposed validator member.
type ProposalCapabilities =
    { Frontend: JsonElement option
      Backend: JsonElement option
      Workspace: JsonElement option
      Validator: ValidatorCapability option
      ModelTransport: ProposalSupport option
      [<JsonDefaultValue(false)>]
      Streaming: bool
      [<JsonDefaultValue(false)>]
      Incremental: bool
      [<JsonDefaultValue(false)>]
      Cancellation: bool
      [<JsonDefaultValue(false)>]
      Progress: bool
      [<JsonExtensionData>]
      Extra: IReadOnlyDictionary<string, JsonElement> }

type ProposalInitializeParams =
    { ProtocolVersions: IReadOnlyList<string>
      Host: JsonElement
      ModelTransport: ProposalSupport option }

type ProposalInitializeResult =
    { ProtocolVersion: string
      Extension: JsonElement
      Capabilities: ProposalCapabilities }

/// The proposed request. The model is read again with the Morphir.Mep MorphirModel codec.
type ValidateModelRequest =
    { Model: JsonElement
      [<JsonDefaultEmpty>]
      Options: IReadOnlyDictionary<string, JsonElement> }

/// The proposed result. Each diagnostic is read again with the Morphir.Mep Diagnostic codec.
type ValidateModelResult = { Diagnostics: IReadOnlyList<JsonElement> }
