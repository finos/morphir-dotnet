@ProjectMigration @CLI @Containment
Feature: Contain project migration
  Project migration takes its discovery boundary from a flag or the environment, and always
  excludes machine and personal sources. Spec: openspec discovery-containment.

  Scenario: Project migration stays inside the environment boundary
    Given a classic migration project
    And the migration project is one workspace member
    And migration environment variable MORPHIR_DISCOVERY_BOUNDARY is "<member>"
    When I migrate the project without a boundary flag with "--dry-run"
    Then migration exits 0 with outcome "preview"
    And the migration plan reads no workspace

  Scenario: Migration ignores isolation off
    Given a classic migration project
    And a personal override masks the migration source directory
    And migration environment variable MORPHIR_ISOLATED is "false"
    When I migrate the project with "--dry-run"
    Then migration exits 0 with outcome "preview"
    And migration verification keeps the classic source directory
