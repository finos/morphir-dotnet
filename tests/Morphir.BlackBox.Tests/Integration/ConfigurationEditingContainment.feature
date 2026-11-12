@integration @needs-configuration
Feature: Contain configuration edits
  Edits take the discovery boundary and isolation from a flag or the environment.
  Spec: openspec discovery-containment.

  Scenario: Edits stay inside an environment boundary
    Given a configuration editing project
    And the editing project is a member below an outer workspace
    And editing environment variable MORPHIR_DISCOVERY_BOUNDARY is "<inner>"
    When I edit configuration without a boundary flag with "set custom.value after --dry-run"
    Then editing exits 0 with outcome "preview"
    And the editing plan consults no project source outside "<inner>"

  Scenario: Edits honor environment isolation
    Given a configuration editing project
    And the editing home has a global configuration
    And editing environment variable MORPHIR_ISOLATED is "true"
    When I edit configuration with "set custom.value after --dry-run"
    Then editing exits 0 with outcome "preview"
    And the editing plan consults no machine or personal sources
