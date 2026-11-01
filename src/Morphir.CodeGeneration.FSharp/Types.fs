namespace Morphir.CodeGeneration.FSharp

/// One generated file in the manifest.
type ManifestEntry = {
    /// The module's dotted path, which selects it for the Myriad host
    ModulePath: string
    /// The generated file's location in the tree
    Location: string
}

/// One module source and its path, as emission produces it.
type internal GeneratedModule = {
    ModulePath: string
    RelativePath: string
    Source: string
}

exception internal GenerationFailure of category: string * location: string * message: string
