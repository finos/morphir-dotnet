module Morphir.IR.NodeId

open Morphir.IR.FQName
open Morphir.SDK.String

type NodeID =
    | TypeID of FQName
    | ValueID of FQName

let nodeIdToString nodeId =
    function
    | TypeID fqName ->
        concat [
            "Type:"
            fqName
            |> FQName.toString
        ]
    | ValueID fqName ->
        concat [
            "Value:"
            fqName
            |> FQName.toString
        ]
