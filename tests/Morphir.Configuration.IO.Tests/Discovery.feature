Feature: Bounded project and workspace discovery
  Hosts share declaration-aware roots without changing legacy artifact locations.

  Scenario: A member primary does not hide its workspace
    Given a declared workspace above a standalone member
    When the host discovers from inside the member
    Then the owning workspace and member are both identified
