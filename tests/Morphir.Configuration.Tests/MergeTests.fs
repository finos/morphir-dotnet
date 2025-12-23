module Morphir.Configuration.Tests.MergeTests

open Expecto
open Morphir.Configuration

[<Tests>]
let mergeTests =
    testList "Merge" [
        testList "mergeCachePaths" [
            test "should merge None with None" {
                let base' = { WorkspaceCache = None; GlobalCache = None }
                let overrideConfig = { WorkspaceCache = None; GlobalCache = None }
                let result = Merge.mergeCachePaths base' overrideConfig
                Expect.equal result.WorkspaceCache None "workspace cache should be None"
                Expect.equal result.GlobalCache None "global cache should be None"
            }

            test "should prefer override values when present" {
                let base' = { WorkspaceCache = Some "/base/cache"; GlobalCache = Some "/base/global" }
                let overrideConfig = { WorkspaceCache = Some "/override/cache"; GlobalCache = Some "/override/global" }
                let result = Merge.mergeCachePaths base' overrideConfig
                Expect.equal result.WorkspaceCache (Some "/override/cache") "should use override workspace cache"
                Expect.equal result.GlobalCache (Some "/override/global") "should use override global cache"
            }

            test "should fallback to base when override is None" {
                let base' = { WorkspaceCache = Some "/base/cache"; GlobalCache = Some "/base/global" }
                let overrideConfig = { WorkspaceCache = None; GlobalCache = None }
                let result = Merge.mergeCachePaths base' overrideConfig
                Expect.equal result.WorkspaceCache (Some "/base/cache") "should use base workspace cache"
                Expect.equal result.GlobalCache (Some "/base/global") "should use base global cache"
            }

            test "should handle mixed Some and None" {
                let base' = { WorkspaceCache = Some "/base/cache"; GlobalCache = None }
                let overrideConfig = { WorkspaceCache = None; GlobalCache = Some "/override/global" }
                let result = Merge.mergeCachePaths base' overrideConfig
                Expect.equal result.WorkspaceCache (Some "/base/cache") "should use base workspace cache"
                Expect.equal result.GlobalCache (Some "/override/global") "should use override global cache"
            }
        ]

        testList "mergeConfigs" [
            test "should merge cache paths correctly" {
                let base' = { Cache = { WorkspaceCache = Some "/base"; GlobalCache = None } }
                let overrideConfig = { Cache = { WorkspaceCache = None; GlobalCache = Some "/override" } }
                let result = Merge.mergeConfigs base' overrideConfig
                Expect.equal result.Cache.WorkspaceCache (Some "/base") "should preserve base workspace cache"
                Expect.equal result.Cache.GlobalCache (Some "/override") "should use override global cache"
            }
        ]

        testList "mergeLayers" [
            test "should return default config for empty layers" {
                let result = Merge.mergeLayers []
                Expect.equal result Defaults.morphirConfig "should return default config"
            }

            test "should apply single layer correctly" {
                let layer = {
                    Path = "/test/config.toml"
                    Config = { Cache = { WorkspaceCache = Some "/test"; GlobalCache = None } }
                }
                let result = Merge.mergeLayers [layer]
                Expect.equal result.Cache.WorkspaceCache (Some "/test") "should apply layer's workspace cache"
            }

            test "should apply precedence correctly with multiple layers" {
                let layer1 = {
                    Path = "/global/config.toml"
                    Config = { Cache = { WorkspaceCache = Some "/global-ws"; GlobalCache = Some "/global-gc" } }
                }
                let layer2 = {
                    Path = "/workspace/config.toml"
                    Config = { Cache = { WorkspaceCache = Some "/workspace-ws"; GlobalCache = None } }
                }
                let layer3 = {
                    Path = "/user/config.toml"
                    Config = { Cache = { WorkspaceCache = None; GlobalCache = Some "/user-gc" } }
                }
                let result = Merge.mergeLayers [layer1; layer2; layer3]
                // layer2 should override layer1's WorkspaceCache, layer3 should override layer1's GlobalCache
                Expect.equal result.Cache.WorkspaceCache (Some "/workspace-ws") "should use highest precedence workspace cache"
                Expect.equal result.Cache.GlobalCache (Some "/user-gc") "should use highest precedence global cache"
            }
        ]
    ]
