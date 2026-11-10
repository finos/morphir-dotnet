@packaged
Feature: Packaged release artifacts
  As a user who downloads one of the four .NET offerings
  I want the packaged artifact to start and work from wherever I put it
  So that each offering is qualified from the bytes that users get, not from a build folder

  # Task 2.4 of add-deployment-and-acquisition. These scenarios run when MORPHIR_EXECUTABLE_TYPE is one of
  # native-aot, self-contained, framework-dependent or dotnet-tool, against the artifact that the PackRelease target
  # wrote to artifacts/release (or MORPHIR_RELEASE_DIR) for this machine's platform. Each expectation comes from the
  # artifact's own launch metadata, so a scenario also checks that the metadata tells the truth.
  #
  # Single-file: no offering claims a .NET single-file bundle. native-aot is one native file that needs no
  # extraction; the managed offerings are folders. The layout scenario qualifies those claims.

  Background:
    Given the packaged artifact under test is installed into a new directory whose path contains a space

  Scenario: The installed artifact is the one its launch metadata names
    Then the archive matches the SHA-256 in its launch metadata
    And the launch path of the launch metadata is in the installed directory

  Scenario: The installed layout is what the offering claims
    Then the installed layout is what the offering claims

  Scenario: Startup from a clean directory
    When I run the installed artifact with "--version" from a new empty directory
    Then the installed artifact exits with code 0
    And the installed artifact prints the release of its launch metadata

  Scenario: Core commands
    When I run the installed artifact with "extension list --json" from a new empty directory
    Then the installed artifact exits with code 0
    And the installed artifact lists the extensions of its host
    When I run the installed artifact with "--help" from a new empty directory
    Then the installed artifact exits with code 0

  Scenario: An F# compile follows the artifact's frontend
    When I compile an F# source file with the installed artifact
    Then the F# compile follows the in-process F# frontend of the launch metadata

  Scenario: Reading an input file
    # On Linux and macOS this reads the file kind through libSystem.Native (the fix in #556).
    When I validate the model fixture "daily-rate-v3.json" with the installed artifact
    Then the installed artifact exits with code 0

  Scenario: No installed .NET runtime
    When I run the installed artifact with "--version" with no .NET runtime reachable
    Then the installed artifact runs without a runtime as its launch metadata says

  Scenario: The declared runtime
    When I run the installed artifact with "--version" with the machine's .NET runtime reachable
    Then the installed artifact uses the runtime that its launch metadata declares
