Feature: Host independent configuration resolution
  Explicit sources give every host the same settings and origins without discovery.

  Scenario: Source ladder and complete array replacement
    Given lower and higher sources supply different arrays
    When the supplied sources are resolved
    Then only the higher array and its origin remain

  Scenario: Environment scalar wins over a nested environment path
    Given environment values supply a scalar and its nested setting
    When the supplied sources are resolved
    Then the shorter environment path wins in either enumeration order

  Scenario: Later workspace profile overrides earlier member profile
    Given postgres then metrics are explicitly selected
    And member postgres and workspace metrics set the same setting
    When the supplied sources are resolved
    Then the workspace metrics value wins

  Scenario: Disabled profiles never load invalid files
    Given profile loading is disabled
    And a supplied inactive profile reader would fail
    When the explicit candidate loader runs
    Then the inactive reader has not been called
