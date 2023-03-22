module Morphir.IR.Decoration

open Morphir.SDK.Dict
open Morphir.IR.FQName
open Morphir.IR.Value
open Morphir.IR.NodeId

type DecorationID = string

type AllDecorationConfigAndData =
    Dict<DecorationID, DecorationConfigAndData>


and DecorationData = Dict<NodeID, RawValue>


and DecorationConfigAndData =
    { DisplayName : string
      EntryPoint : FQName
      IR : Morphir.IR.Distribution.Distribution
      Data : DecorationData
    }
