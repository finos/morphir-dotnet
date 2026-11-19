@WorkspaceMigration @CLI @PlatformSpecific
Feature: Adopt classic projects through one bounded workspace migration
  The scenarios of this feature make or recover a pending configuration transaction with the configuration
  library of the product, in the test process. Each other scenario of the workspace migration is in
  tests/Morphir.BlackBox.Tests/Integration/WorkspaceMigration.feature.

  Scenario: A pending durable transaction blocks a workspace preview with recovery evidence
    Given a workspace migration repository
    And workspace migration has pending durable recovery
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 2 with outcome "recovery-required"
    And workspace migration preserves and reports pending recovery

  Scenario: A process interrupted after directory creation reports uncertain ownership
    Given a workspace migration repository
    And workspace migration has interrupted directory recovery
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 2 with outcome "recovery-required"
    And workspace migration reports uncertain directory ownership
