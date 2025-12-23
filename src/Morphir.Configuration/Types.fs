namespace Morphir.Configuration

/// Specifies when to apply CI profile overlay
type CiProfileMode =
    /// Always apply CI overlay
    | On
    /// Never apply CI overlay
    | Off
    /// Apply CI overlay when CI environment is detected (default)
    | Auto

/// Cache path configuration
type CachePaths =
    { /// Optional override for workspace-local cache path
      /// Defaults to .morphir/cache/ at workspace root
      WorkspaceCache: string option
      /// Optional override for global cache path
      /// Defaults to OS-standard cache directory
      GlobalCache: string option }

/// Minimal configuration schema for v1
/// Additional fields can be added as needed
type MorphirConfig =
    { /// Cache path configuration
      Cache: CachePaths }

/// Represents a single configuration layer with its source
type ConfigLayer =
    { /// Path to the config file (for debugging/telemetry)
      Path: string
      /// Parsed configuration
      Config: MorphirConfig }

/// Result of configuration resolution with full context
type ConfigResolution =
    { /// Effective (merged) configuration
      Effective: MorphirConfig
      /// All loaded layers in precedence order (lowest to highest)
      Layers: ConfigLayer list
      /// Discovered workspace root path, if any
      WorkspaceRoot: string option
      /// Whether CI profile overlay was applied
      CiProfileApplied: bool }

/// Default values for configuration
module Defaults =
    let cachePaths =
        { WorkspaceCache = None
          GlobalCache = None }

    let morphirConfig =
        { Cache = cachePaths }

    let configResolution =
        { Effective = morphirConfig
          Layers = []
          WorkspaceRoot = None
          CiProfileApplied = false }
