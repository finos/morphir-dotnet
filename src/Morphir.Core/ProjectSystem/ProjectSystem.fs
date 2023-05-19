namespace Morphir.ProjectSystem

type MorphirJson = {
    PackageName: string
    SourceDirectory: string
    ExposedModules: string list
}

module MorphirJson =
    ()
