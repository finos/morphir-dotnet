namespace Morphir.CodeGeneration.FSharp

/// Options shared by typed-model and legacy JSON generation.
type GenerationOptions =
    {
        /// Whether to run exact constant folding. Validation always runs.
        Optimize: bool
    }

    /// Existing callers keep constant folding enabled.
    static member Default = { Optimize = true }

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
