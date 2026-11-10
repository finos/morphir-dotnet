// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The host side of morphir.backend.generateModel model transport: the tuples a host offers and which one a model
/// needs. The decision is in F#, with an exhaustive match on the model family, so C# callers do not test cases.
module Morphir.Compilation.Mep.Tests.MepGenerationTransportTests

open Expecto
open Morphir.Compilation.Mep
open Morphir.IR.Model
open Morphir.Mep

let private tupleOf (family: ModelFamily) (version: string) =
    match MepGenerationTransport.TryTupleFor(family, version) with
    | true, tuple -> Some tuple
    | false, _ -> None

[<Tests>]
let tests =
    testList "MEP generation transport" [
        test "The host offers the JSON generateModel tuples for IR 4.0.0 and 3.0.0, 4.0.0 first" {
            let offered = MepGenerationTransport.Tuples |> List.ofSeq

            Expect.equal
                [ for tuple in offered -> tuple.Operation, tuple.Profile, tuple.Codec, tuple.EnvelopeVersion.Value, tuple.IrVersion.Value ]
                [ ModelOperation.GenerateModel, "morphir-ir", ModelCodec.MorphirJson, "0.1.0-draft.1", "4.0.0"
                  ModelOperation.GenerateModel, "morphir-ir", ModelCodec.MorphirJson, "0.1.0-draft.1", "3.0.0" ]
                "the offer"

            Expect.equal offered [ MepGenerationTransport.V4Tuple; MepGenerationTransport.ClassicTuple ] "the named tuples"
        }

        test "A model needs the tuple of its family and IR version; others have none" {
            for family, version, expected in
                [ ModelFamily.V4, "4", Some MepGenerationTransport.V4Tuple
                  ModelFamily.V4, "4.0.0", Some MepGenerationTransport.V4Tuple
                  ModelFamily.Classic, "3", Some MepGenerationTransport.ClassicTuple
                  ModelFamily.Classic, "3.0.0", Some MepGenerationTransport.ClassicTuple
                  ModelFamily.Classic, "3.1.0", None
                  ModelFamily.Classic, "2", None
                  ModelFamily.Classic, "1", None
                  ModelFamily.Classic, "4", None
                  ModelFamily.V4, "3", None
                  ModelFamily.V4, "4.1.0", None ] do
                Expect.equal (tupleOf family version) expected $"{family} {version}"
        }
    ]
