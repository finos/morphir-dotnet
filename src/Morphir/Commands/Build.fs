module Finos.Morphir.Commands.Build

open Argu
open Finos.Morphir.Elm
open Serilog

type Arguments =
    | Repository of path:string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Repository _ -> "add an additional repository to use in model/package resolution"

let run json (args:ParseResults<_>) =
    MorphirElmJs.make {ProjectDir = ""; OutputPath=""}
    Log.Information("TODO: Build")
    Ok ()
