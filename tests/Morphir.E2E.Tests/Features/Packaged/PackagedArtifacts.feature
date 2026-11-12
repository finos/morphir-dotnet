@packaged
Feature: Packaged release artifacts
  As a user who downloads one of the four .NET offerings
  I want the packaged artifact to start and work from wherever I put it
  So that each offering is checked from packaged artifacts, the bytes a release would hold, not from a build folder

  # Task 2.4 of add-deployment-and-acquisition. These scenarios run when MORPHIR_EXECUTABLE_TYPE is one of
  # native-aot, self-contained, framework-dependent or dotnet-tool, against the artifact that the PackRelease target
  # wrote to artifacts/release (or the MORPHIR_RELEASE_DIR folder, which run-e2e-tests.cs also packs into) for this machine's platform. Each expectation comes from the
  # artifact's own launch metadata, so a scenario also checks that the metadata tells the truth. A pass here is a
  # check of a local build run on the build machine. It does not qualify a release or a platform: that needs the exact
  # artifacts acquired the way a user acquires them.
  #
  # Single-file: no offering claims a .NET single-file bundle. native-aot is one native file that needs no
  # extraction, and the layout scenario checks that it is a Native AOT executable, not a bundle; the managed offerings
  # are folders. "No installed .NET runtime" is skipped, with the reason, where the machine's runtime cannot be hidden.

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

  # The WASM engine in each offering: task 5.2 of add-wasm-extension-host. The worker session uses the pinned crafted
  # module extism-echo of tests/Fixtures/MepWasm, copied into a folder whose path has a space.
  #
  # Requirement "Packaged engine is qualified", scenario "Platform where static linking failed": an artifact that
  # carries the engine as a separate file lists that file, and one that links the engine in carries no engine file.
  # The expectation is the artifact's own <artifact>.build-contents.json (its engines), which PackRelease wrote and
  # checked against the pinned engine: native-aot one linked engine and no library, self-contained and
  # framework-dependent this platform's library, the tool package one library per platform it records.
  Scenario: The engine files are those the build contents record
    Then the engine libraries in the installed artifact are exactly the separate libraries its build contents record

  # Requirement "Runtime availability is reported", scenario "Supported platform", from the installed artifact.
  Scenario: The runtime report of the installed artifact
    When I run the installed artifact with "extension runtimes --json" from a new empty directory
    Then the installed artifact reports its WASM runtime availability with the pinned engine on this platform

  # Requirement "Worker mode", scenario "Conformance runner drives a guest", and "The Extism profile in every offering".
  Scenario: A WASM worker session from the installed artifact
    When the installed artifact serves the pinned module "extism-echo" with the frames "initialize, initialized, shutdown, exit" in a worker session
    Then the packaged worker session exits with status 0
    And the packaged worker session's stdout holds only these frames:
      | frame                                                                                             |
      | {"jsonrpc":"2.0","id":1,"method":"morphir.initialize","params":{"protocolVersion":"0.2.0-draft.1"}} |
      | {"jsonrpc":"2.0","id":2,"method":"morphir.shutdown"}                                                |
    And the packaged worker session's stderr has the events "started, exited"
    And the packaged worker session's temporary directory is empty

  # Task 5.2: "first-run extraction with a read-only install directory". No offering extracts anything at first run
  # (evidence/5.2-packaged-offerings-assessment.md), so this scenario asserts that the first run of a fresh
  # installation, read-only for this user, works and leaves the installation's files as they were. On Windows every
  # file is read-only and the folder denies this user creating, writing and deleting below it; on Linux and macOS
  # every write bit is cleared. The permissions are given back after the scenario, pass or fail.
  Scenario: A WASM guest runs from a read-only install directory, and its first run extracts nothing
    Given a second installation of the artifact under test, made read-only before its first run
    When I run the installed artifact with "extension runtimes --json" from a new empty directory
    Then the installed artifact reports its WASM runtime availability with the pinned engine on this platform
    When the installed artifact serves the pinned module "extism-echo" with the frames "initialize, initialized, shutdown, exit" in a worker session
    Then the packaged worker session exits with status 0
    And the packaged worker session's stdout holds only these frames:
      | frame                                                                                             |
      | {"jsonrpc":"2.0","id":1,"method":"morphir.initialize","params":{"protocolVersion":"0.2.0-draft.1"}} |
      | {"jsonrpc":"2.0","id":2,"method":"morphir.shutdown"}                                                |
    And the packaged worker session's stderr has the events "started, exited"
    And the packaged worker session's temporary directory is empty
    And the read-only installation holds the same files as before its first run
