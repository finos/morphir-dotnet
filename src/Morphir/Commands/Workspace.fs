module Morphir.Commands.Workspace
open Argu

type Arguments =
    | [<CliPrefix(CliPrefix.None)>] List of ParseResults<ListArgs>
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | List _ -> "List items in the workspace"
and ListArgs =
    | Models
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Models -> "List the models contained in the workspace"
