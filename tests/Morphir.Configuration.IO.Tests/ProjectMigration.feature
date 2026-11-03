Feature: Loss-checked classic project migration
  A host previews one project and applies only independently verified plans.

  Scenario: A read-only host previews independent TOML without writing
    Given a classic project migration source
    When a read-only host previews its project migration
    Then the project migration is verified with no source mutation

  Scenario: Applying a migration retains the original classic bytes
    Given a classic project migration source
    When the host applies its project migration
    Then the original classic project bytes remain unchanged
