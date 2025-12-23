namespace Morphir.Configuration

/// Functions for detecting CI environments
module CiDetection =

    /// Well-known CI environment variables
    let ciEnvironmentVariables =
        [ "CI"
          "GITHUB_ACTIONS"
          "AZURE_HTTP_USER_AGENT"
          "GITLAB_CI"
          "BITBUCKET_BUILD_NUMBER"
          "TEAMCITY_VERSION"
          "CIRCLECI"
          "TRAVIS"
          "JENKINS_URL"
          "BUILDKITE"
          "CODEBUILD_BUILD_ID"
          "TF_BUILD" ]

    /// Detect if running in CI based on environment variables
    /// Pure function - accepts environment map as input
    let isCiEnvironment (envVars: Map<string, string>) : bool =
        ciEnvironmentVariables
        |> List.exists (fun varName ->
            envVars.ContainsKey varName &&
            not (System.String.IsNullOrWhiteSpace(envVars.[varName])))

    /// Determine if CI overlay should be applied based on mode and environment
    let shouldApplyCiOverlay (mode: CiProfileMode) (envVars: Map<string, string>) : bool =
        match mode with
        | On -> true
        | Off -> false
        | Auto -> isCiEnvironment envVars
