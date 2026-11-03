Feature: Immutable C# SDK runtime
  Ordinary C# applications use independent immutable values and Morphir behavior.

  Scenario: Optional and result branches preserve payload meanings
    When a C# consumer maps present, absent, success and error values
    Then only present and successful values invoke their callbacks
    And string errors and integer successes retain their types

  Scenario: Persistent collections retain old values and ordered traversal
    When a C# consumer updates persistent SDK collections
    Then retained operands remain unchanged
    And independently constructed nested values have equal hashes

  Scenario: Frozen admitted operations execute through the public runtime
    When the C# adapter executes the frozen required SDK cases
    Then all 207 required case IDs pass without skips
    And the eight representation exclusions are reported separately
