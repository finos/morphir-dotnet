module rec Morphir.FSharp.Backend

open Morphir.FSharp.Feature.Codec
open Morphir.File
open Morphir.File.FileMap
open Morphir.IR.Distribution
open Morphir.IR.Module

type Options = {
    [<CompiledName("LimitToModules")>]
    limitToModules: Set<ModuleName> option
}

type Error = CodecError of error: Feature.Codec.Error


let defaultOptions = { limitToModules = None }

let mapDistribution (options: Options) (disto: Distribution) : Result<FileMap, Error> =
    match disto with
    | Distribution.Library(packageName, _, packageDef) ->
        match options.limitToModules with
        | Some(modules) -> Ok(FileMap.empty)
        | None -> Ok(FileMap.empty)

module Codec =
    open Json

    let encodeError: Error -> Json.Value =
        function
        | CodecError(err) -> (Encode.string err)
