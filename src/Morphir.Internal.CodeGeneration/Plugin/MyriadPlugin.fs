namespace Morphir.Internal.CodeGeneration.Plugin

open Myriad.Core
open Morphir.Internal.CodeGeneration.Generators

/// Register all generators
module Plugin =
    
    /// All available generators
    let generators: IMyriadGenerator list = [
        JsonCodecGenerator() :> IMyriadGenerator
        VisitorGenerator() :> IMyriadGenerator
        LensGenerator() :> IMyriadGenerator
        ActivePatternGenerator() :> IMyriadGenerator
        BuilderGenerator() :> IMyriadGenerator
    ]
    
    /// Get generator by name
    let getGenerator (name: string) : IMyriadGenerator option =
        generators |> List.tryFind (fun _ -> 
            // This is a simplified version
            true
        )
