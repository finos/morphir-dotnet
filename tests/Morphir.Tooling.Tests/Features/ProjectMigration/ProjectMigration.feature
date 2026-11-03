@ProjectMigration @CLI
Feature: Migrate one classic project without losing its configuration
  A reviewed TOML plan must work independently of the retained classic manifest.

  Scenario Outline: One classic project accepts directory manifest and current inputs
    Given a classic migration project
    When I migrate the project with "<command>"
    Then migration exits 0 with outcome "<outcome>"
    And the migration source remains byte identical
    And the migration report verifies independent equivalence

    Examples:
      | command                    | outcome |
      | --dry-run                  | preview |
      | . --dry-run                | preview |
      | morphir.json --dry-run     | preview |
      | .                          | written |
      | morphir.json               | written |

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

  Scenario: Preview does not create a target
    Given a classic migration project
    When I migrate the project with "--dry-run"
    Then migration exits 0 with outcome "preview"
    And no migration target exists
    And the migration source remains byte identical

  Scenario: An existing equivalent target and repeated migration preserve text and timestamp
    Given a classic migration project
    And the migration target contains equivalent values and comments
    When I migrate the project with ""
    Then migration exits 0 with outcome "written"
    And migration preserves the existing equivalent text
    When I migrate the project with ""
    Then migration exits 0 with outcome "unchanged"
    And the repeated migration preserves the target timestamp

  Scenario Outline: Invalid input and unsupported losses return structured blockers
    Given a classic migration project
    And migration fixture "<fixture>"
    When I migrate the project with "<command>"
    Then migration exits 2 with outcome "blocked"
    And migration reports blocking diagnostics on stderr
    And no migration target exists
    And the migration source remains byte identical

    Examples:
      | fixture             | command                               |
      | ir                  | morphir-ir.json --dry-run              |
      | dependency          | --dry-run                             |
      | decorations         | --dry-run                             |
      | local-dependency    | --dry-run                             |
      | null                | --dry-run                             |
      | canonical-conflict  | --dry-run                             |
      | normal              | --output ../outside.toml --dry-run     |
      | normal              | --output config.yaml --dry-run         |
      | normal              | --output custom.toml --dry-run         |

  Scenario: Ambient overrides cannot hide an existing target conflict
    Given a classic migration project
    And a conflicting migration target and ambient overrides
    When I migrate the project with "--dry-run"
    Then migration exits 2 with outcome "blocked"
    And migration preserves the conflicting target
    And migration reports blocking diagnostics on stderr

  Scenario: A member migration keeps workspace membership and sibling files
    Given a classic migration project
    And the migration project is one workspace member
    When I migrate the project with ""
    Then migration exits 0 with outcome "written"
    And migration changes only the selected member target
    And the migration report verifies independent equivalence

  Scenario: Raw values and references are redacted throughout the plan
    Given a classic migration project
    And migration fixture "secrets"
    When I migrate the project with "--dry-run"
    Then migration exits 0 with outcome "preview"
    And migration streams redact secret inputs
    And no migration target exists

  Scenario: Human output reports the result without JSON log noise
    Given a classic migration project
    When I migrate the project with human output
    Then migration human output reports a verified preview

  Scenario: Host initialization failure keeps JSON stdout valid
    Given a classic migration project
    And malformed migration host appsettings
    When I migrate the project with "--dry-run"
    Then migration exits 2 with outcome "blocked"
    And migration reports blocking diagnostics on stderr
    And no migration target exists

  Scenario Outline: Parser failures retain the migration JSON contract
    Given a classic migration project
    When I migrate the project with "<command>"
    Then migration exits 2 with outcome "blocked"
    And migration reports blocking diagnostics on stderr
    And no migration target exists

    Examples:
      | command               |
      | --unknown             |
      | --output              |
      | first second          |
