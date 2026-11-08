# Spec Delta

## ADDED Requirements

### Requirement: The package is published with each release
Each morphir-dotnet release SHALL publish the `Morphir.Myriad` package to NuGet.org with the release version, after the packages it depends on. The release SHALL validate the plugin package layout before it publishes. The getting-started article, the car rental example and its README SHALL use the published package and the release version, not a local feed.

#### Scenario: Restore from NuGet.org
- **WHEN** a release has published `Morphir.Myriad`, and a new F# project references it with the release version and a supported model
- **THEN** the project restores the package from NuGet.org and builds the generated modules

#### Scenario: Documentation uses the published package
- **WHEN** a reader follows the getting-started article after a release
- **THEN** the article asks for no local pack step and no local feed, and names the release version
