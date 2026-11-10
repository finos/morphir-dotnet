namespace Morphir.CodeGeneration.FSharp

/// Options shared by typed-model and legacy JSON generation.
type GenerationOptions =
    {
        /// Whether to run exact constant folding. Validation always runs.
        Optimize: bool
        /// <summary>
        /// Whether to format the source with Fantomas. True formats when the runtime can run Fantomas (not under
        /// Native AOT, where the text stays unformatted and the tree records <c>unavailable</c>). False writes the
        /// emitter's own text: LF line endings only and exactly one final LF, the same bytes on every OS, runtime
        /// and culture.
        /// </summary>
        Format: bool
    }

    /// Existing callers keep constant folding and formatting enabled.
    static member Default = { Optimize = true; Format = true }

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
