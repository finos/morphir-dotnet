Feature: Bounded workspace migration
  Repository adoption uses shared inventory and verified project migration before writes.

  Scenario: Adopt projects without a declared workspace
    Given a repository with two classic workspace candidates
    When a host previews bounded workspace migration
    Then both conversions share one verified virtual workspace
    And workspace preview changes no files

  Scenario: Root classic project retains its identity
    Given a repository with a root classic project and one child
    When a host previews bounded workspace migration
    Then the root remains a project outside the member list

  Scenario: Exclusions and nested workspace boundaries explain omissions
    Given a repository with excluded and nested workspace candidates
    When a host previews bounded workspace migration
    Then excluded and boundary paths have no conversions
