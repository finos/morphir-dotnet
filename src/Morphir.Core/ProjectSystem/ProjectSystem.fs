namespace Morphir.ProjectSystem
#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif
type MorphirJson = {
    PackageName: string
    SourceDirectory: string
    ExposedModules: string list
}

module MorphirJson =
    [<CompiledName("Decode")>]
    let decode json = Decode.Auto.fromString<MorphirJson> json

    [<CompiledName("Encode")>]
    let encode (value:MorphirJson) = Encode.Auto.toString value

    let createOrUpdate newInstance (original:MorphirJson option) : MorphirJson =
        match original with
        | Some original ->
            {
                PackageName = newInstance.PackageName
                SourceDirectory = newInstance.SourceDirectory
                ExposedModules = newInstance.ExposedModules
            }
        | None ->
            newInstance

type MorphirJson with
    static member CreateOrUpdate(newInstance, original) = MorphirJson.createOrUpdate newInstance original
