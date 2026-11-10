// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Compilation.Mep

open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.IR.Model
open Morphir.Mep

/// <summary>
/// The model transport of <c>morphir.backend.generateModel</c> on the host side: the JSON IR tuples a host offers, and
/// the tuple a model needs. The decision is here, in F#, with an exhaustive match on <see cref="ModelFamily"/>, so a new
/// model family fails to compile until it is decided; C# callers (the generation backend adapter) call these members
/// and do not test the family's cases.
/// </summary>
[<AbstractClass; Sealed>]
type MepGenerationTransport =

    static let json (irVersion: string) : ModelTransportTuple =
        { Operation = ModelOperation.GenerateModel
          Profile = "morphir-ir"
          Codec = ModelCodec.MorphirJson
          EnvelopeVersion = SemanticVersion.Create "0.1.0-draft.1"
          IrVersion = SemanticVersion.Create irVersion }

    static let v4 = json "4.0.0"
    static let classic = json "3.0.0"
    static let offered: IReadOnlyList<ModelTransportTuple> = [| v4; classic |]

    /// <summary>The JSON IR 4.0.0 tuple: an IR v4 model with formatVersion 4 or "4.0.0".</summary>
    static member V4Tuple = v4

    /// <summary>The JSON IR 3.0.0 tuple: a classic model with formatVersion 3 or "3.0.0".</summary>
    static member ClassicTuple = classic

    /// <summary>The tuples a host offers, IR 4.0.0 first.</summary>
    static member Tuples = offered

    /// <summary>
    /// The tuple that a model of the family and declared format version needs, for C# callers: true and the tuple, or
    /// false when the host sends no tuple for it (for example classic versions 1 and 2, or a version that its family
    /// does not have).
    /// </summary>
    static member TryTupleFor
        (
            family: ModelFamily,
            version: string,
            [<Out; MaybeNullWhen(false)>] tuple: byref<ModelTransportTuple>
        ) : bool =
        let needed =
            match family with
            | ModelFamily.V4 -> if version = "4" || version = "4.0.0" then Some v4 else None
            | ModelFamily.Classic -> if version = "3" || version = "3.0.0" then Some classic else None

        match needed with
        | Some found ->
            tuple <- found
            true
        | None ->
            tuple <- Unchecked.defaultof<_>
            false
