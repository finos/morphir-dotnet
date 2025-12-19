namespace Morphir.Internal.CodeGeneration

/// Configuration attributes for code generators
module ConfigurationAttributes =
    
    /// Configuration options for JSON codec generation
    type JsonCodecConfig = {
        Namespace: string option
        PropertyNamingPolicy: string
        IncludeNullValues: bool
    }
    
    /// Configuration options for visitor generation
    type VisitorConfig = {
        Namespace: string option
        VisitorName: string option
        IncludeDefaultImplementation: bool
    }
    
    /// Configuration options for lens generation
    type LensConfig = {
        Namespace: string option
        GenerateComposableLenses: bool
    }
    
    /// Configuration options for active pattern generation
    type ActivePatternConfig = {
        Namespace: string option
        IncludePartialPatterns: bool
    }
    
    /// Configuration options for builder generation
    type BuilderConfig = {
        Namespace: string option
        BuilderName: string option
        FluentStyle: bool
    }
    
    /// Create default JSON codec configuration
    let defaultJsonCodecConfig = {
        Namespace = None
        PropertyNamingPolicy = "camelCase"
        IncludeNullValues = false
    }
    
    /// Create default visitor configuration
    let defaultVisitorConfig = {
        Namespace = None
        VisitorName = None
        IncludeDefaultImplementation = false
    }
    
    /// Create default lens configuration
    let defaultLensConfig = {
        Namespace = None
        GenerateComposableLenses = true
    }
    
    /// Create default active pattern configuration
    let defaultActivePatternConfig = {
        Namespace = None
        IncludePartialPatterns = false
    }
    
    /// Create default builder configuration
    let defaultBuilderConfig = {
        Namespace = None
        BuilderName = None
        FluentStyle = true
    }
