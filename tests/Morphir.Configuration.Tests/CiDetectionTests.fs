module Morphir.Configuration.Tests.CiDetectionTests

open Expecto
open Morphir.Configuration

[<Tests>]
let ciDetectionTests =
    testList "CiDetection" [
        testList "isCiEnvironment" [
            test "should return false for empty environment" {
                let envVars = Map.empty<string, string>
                let result = CiDetection.isCiEnvironment envVars
                Expect.isFalse result "should not detect CI in empty environment"
            }

            test "should detect CI when CI=true" {
                let envVars = Map.ofList [("CI", "true")]
                let result = CiDetection.isCiEnvironment envVars
                Expect.isTrue result "should detect CI when CI=true"
            }

            test "should detect CI when GITHUB_ACTIONS is set" {
                let envVars = Map.ofList [("GITHUB_ACTIONS", "true")]
                let result = CiDetection.isCiEnvironment envVars
                Expect.isTrue result "should detect CI when GITHUB_ACTIONS is set"
            }

            test "should detect CI when GITLAB_CI is set" {
                let envVars = Map.ofList [("GITLAB_CI", "true")]
                let result = CiDetection.isCiEnvironment envVars
                Expect.isTrue result "should detect CI when GITLAB_CI is set"
            }

            test "should detect CI when AZURE_HTTP_USER_AGENT is set" {
                let envVars = Map.ofList [("AZURE_HTTP_USER_AGENT", "Azure-Pipelines")]
                let result = CiDetection.isCiEnvironment envVars
                Expect.isTrue result "should detect CI when AZURE_HTTP_USER_AGENT is set"
            }

            test "should detect CI when BITBUCKET_BUILD_NUMBER is set" {
                let envVars = Map.ofList [("BITBUCKET_BUILD_NUMBER", "123")]
                let result = CiDetection.isCiEnvironment envVars
                Expect.isTrue result "should detect CI when BITBUCKET_BUILD_NUMBER is set"
            }

            test "should detect CI when TEAMCITY_VERSION is set" {
                let envVars = Map.ofList [("TEAMCITY_VERSION", "2021.1")]
                let result = CiDetection.isCiEnvironment envVars
                Expect.isTrue result "should detect CI when TEAMCITY_VERSION is set"
            }

            test "should not detect CI when variable is empty string" {
                let envVars = Map.ofList [("CI", "")]
                let result = CiDetection.isCiEnvironment envVars
                Expect.isFalse result "should not detect CI when variable is empty"
            }

            test "should not detect CI when variable is whitespace" {
                let envVars = Map.ofList [("CI", "   ")]
                let result = CiDetection.isCiEnvironment envVars
                Expect.isFalse result "should not detect CI when variable is whitespace"
            }

            test "should detect CI when multiple CI variables are set" {
                let envVars = Map.ofList [
                    ("CI", "true")
                    ("GITHUB_ACTIONS", "true")
                ]
                let result = CiDetection.isCiEnvironment envVars
                Expect.isTrue result "should detect CI when multiple variables are set"
            }
        ]

        testList "shouldApplyCiOverlay" [
            test "should always return true for On mode" {
                let envVars = Map.empty<string, string>
                let result = CiDetection.shouldApplyCiOverlay CiProfileMode.On envVars
                Expect.isTrue result "should apply CI overlay when mode is On"
            }

            test "should always return false for Off mode" {
                let envVars = Map.ofList [("CI", "true")]
                let result = CiDetection.shouldApplyCiOverlay CiProfileMode.Off envVars
                Expect.isFalse result "should not apply CI overlay when mode is Off"
            }

            test "should detect CI for Auto mode when CI=true" {
                let envVars = Map.ofList [("CI", "true")]
                let result = CiDetection.shouldApplyCiOverlay CiProfileMode.Auto envVars
                Expect.isTrue result "should apply CI overlay when mode is Auto and CI detected"
            }

            test "should not detect CI for Auto mode when no CI variables" {
                let envVars = Map.empty<string, string>
                let result = CiDetection.shouldApplyCiOverlay CiProfileMode.Auto envVars
                Expect.isFalse result "should not apply CI overlay when mode is Auto and CI not detected"
            }

            test "should detect CI for Auto mode with GITHUB_ACTIONS" {
                let envVars = Map.ofList [("GITHUB_ACTIONS", "true")]
                let result = CiDetection.shouldApplyCiOverlay CiProfileMode.Auto envVars
                Expect.isTrue result "should apply CI overlay when mode is Auto and GITHUB_ACTIONS set"
            }
        ]
    ]
