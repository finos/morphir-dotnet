Feature: Recoverable configuration batches
  As a workspace owner
  I want interrupted batches to retain inspectable evidence
  So that overlapping tools cannot overwrite unfinished changes

  Scenario: Complete batch staging precedes replacements
    Given a recoverable configuration batch
    When the host stages the complete batch
    Then no replacement starts before all candidates are staged

  Scenario: Shared editing and project migration respect pending recovery
    Given a recoverable configuration batch
    When a batch replacement and its rollback both fail
    Then shared configuration editing and project migration refuse overlapping writes
    And explicit shared recovery restores the originals and clears the guard

  Scenario: User changes survive explicit recovery
    Given a recoverable configuration batch
    When a batch replacement and its rollback both fail
    And the user changes an installed target
    Then shared recovery reports the changed target and preserves its journal
