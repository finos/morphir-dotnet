module Morphir.Tools.Backend.Target

open Morphir.File.FileMap
open Morphir.IR.Distribution

type BackendOptions = FSharpOptions of Morphir.FSharp.Backend.Options

let mapDistributionInternal (backendOptions: BackendOptions) (distro: Distribution) =
    match backendOptions with
    | FSharpOptions options -> Morphir.FSharp.Backend.mapDistribution options distro


let mapDistribution
    (backendOptions: BackendOptions)
    (distro: Distribution)
    : Result<FileMap, Json.Value> =
    mapDistributionInternal backendOptions distro
    |> Result.mapError (fun (Morphir.FSharp.Backend.CodecError err) -> Json.Encode.string err)
