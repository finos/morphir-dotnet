namespace Morphir.Internal.CodeGeneration

open System

/// Marker attribute for JSON codec generation
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple = false)>]
type GenerateJsonCodecAttribute() =
    inherit Attribute()

    /// Namespace for generated code (optional, defaults to current namespace + ".Generated")
    member val Namespace: string = null with get, set

    /// Property naming policy (e.g., "camelCase", "PascalCase")
    member val PropertyNamingPolicy: string = "camelCase" with get, set

/// Marker attribute for visitor pattern generation
[<AttributeUsage(AttributeTargets.Class, AllowMultiple = false)>]
type GenerateVisitorAttribute() =
    inherit Attribute()

    member val Namespace: string = null with get, set
    member val VisitorName: string = null with get, set

/// Marker attribute for lens generation
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple = false)>]
type GenerateLensesAttribute() =
    inherit Attribute()

    member val Namespace: string = null with get, set

/// Marker attribute for active pattern generation
[<AttributeUsage(AttributeTargets.Class, AllowMultiple = false)>]
type GenerateActivePatternsAttribute() =
    inherit Attribute()

    member val Namespace: string = null with get, set

/// Marker attribute for builder generation
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple = false)>]
type GenerateBuilderAttribute() =
    inherit Attribute()

    member val Namespace: string = null with get, set
    member val BuilderName: string = null with get, set
