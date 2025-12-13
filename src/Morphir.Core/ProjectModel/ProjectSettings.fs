namespace Morphir.ProjectModel

open Thoth.Json.Net

type ProjectSettings = {
    PackageName: string
    SourceDirectory: string
    ExposedModules: string list
}

module ProjectSettings =
    [<CompiledName("Decode")>]
    let decode json =
        Decode.Auto.fromString<ProjectSettings> json

    [<CompiledName("Encode")>]
    let encode (value: ProjectSettings) = Encode.Auto.toString value

    let createOrUpdate newInstance (original: ProjectSettings option) : ProjectSettings =
        match original with
        | Some original -> {
            PackageName = newInstance.PackageName
            SourceDirectory = newInstance.SourceDirectory
            ExposedModules = newInstance.ExposedModules
          }
        | None -> newInstance

type ProjectSettings with

    static member CreateOrUpdate(newInstance, original) =
        ProjectSettings.createOrUpdate newInstance original
