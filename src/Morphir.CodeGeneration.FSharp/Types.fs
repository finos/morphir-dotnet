namespace Morphir.CodeGeneration.FSharp

/// A generation failure tied to its model source.
type Diagnostic = {
    Category: string
    InputPath: string
    Location: string
    Message: string
}

/// One module source and its path in the ordered compilation manifest.
type GeneratedModule = {
    ModulePath: string
    RelativePath: string
    Source: string
}

exception internal GenerationFailure of category: string * location: string * message: string
