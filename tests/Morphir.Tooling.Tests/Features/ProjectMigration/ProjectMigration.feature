@ProjectMigration @CLI @PlatformSpecific
Feature: Migrate one classic project without losing its configuration
  The scenarios of this feature read the migrated file with the configuration library of the product, in the test
  process. Each other scenario of the project migration, and the same scenarios with the check through the command
  line, are in tests/Morphir.BlackBox.Tests/Integration/ProjectMigration.feature.

  Scenario Outline: Supported primary outputs retain the project source directory
    Given a classic migration project
    When I migrate the project with "--output <output>"
    Then migration exits 0 with outcome "written"
    And migration writes "<output>" with the legacy defaults
    And the migration source remains byte identical

    Examples:
      | output                        |
      | morphir.toml                  |
      | .morphir/morphir.toml          |
      | .config/morphir/config.toml    |
