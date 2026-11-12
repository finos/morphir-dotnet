@integration @needs-configuration
Feature: The global Morphir home is never a project
  Files in the global Morphir home are global configuration only, wherever discovery starts.
  Spec: openspec morphir-project-system. Reproduces #494 problems 1 and 2.

  Scenario: A bare project below the home directory does not adopt the global file
    Given a home directory with a global Morphir file and a bare project below it
    When I inspect the bare project with "path"
    Then inspection exits with 0 and one JSON result
    And the global Morphir file is consulted only as a global source

  Scenario: A native Windows Morphir home works
    Given a home directory with a global Morphir file and a bare project below it
    And MORPHIR_HOME is the native path of the global Morphir home
    When I inspect the bare project with "path"
    Then inspection exits with 0 and one JSON result
    And the global Morphir file is consulted only as a global source
