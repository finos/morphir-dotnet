namespace Morphir.Configuration

/// Functions for merging configuration layers with proper precedence
module Merge =

    /// Merge two cache path configurations, with `overrideConfig` taking precedence
    let mergeCachePaths (base': CachePaths) (overrideConfig: CachePaths) : CachePaths =
        { WorkspaceCache = overrideConfig.WorkspaceCache |> Option.orElse base'.WorkspaceCache
          GlobalCache = overrideConfig.GlobalCache |> Option.orElse base'.GlobalCache }

    /// Merge two configurations, with `overrideConfig` taking precedence
    let mergeConfigs (base': MorphirConfig) (overrideConfig: MorphirConfig) : MorphirConfig =
        { Cache = mergeCachePaths base'.Cache overrideConfig.Cache }

    /// Merge a list of configuration layers in order (lowest to highest precedence)
    let mergeLayers (layers: ConfigLayer list) : MorphirConfig =
        layers
        |> List.map (fun layer -> layer.Config)
        |> List.fold mergeConfigs Defaults.morphirConfig
