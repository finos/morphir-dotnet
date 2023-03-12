module Morphir.IR.NodePath

open Morphir.IR.Name
open Morphir.SDK.String

type NodePathStep =
    | ChildByName of Name
    | ChildByIndex of int
and NodePath = NodePath of NodePathStep list

let toString nodePath =
    nodePath |> List.map (function
        | ChildByName name -> name |> Name.toCamelCase
        | ChildByIndex index -> index |> string
    ) |> String.concat "."

let fromString str =
    str |> split "." |> List.map (fun stepString ->
        match toInt stepString with
        | Some index -> ChildByIndex index
        | None -> ChildByName (Name.fromString stepString)
    ) |> NodePath
