@Generation @CLI
Feature: Generate executable libraries from Morphir models
  Generation uses explicit settings and the same behavior expectations for every representation.

  Scenario Outline: Nonempty models produce executable F# with pure JSON stdout
    Given a daily-rate model at version "<version>" in "<format>"
    When I generate F# with explicit settings
    Then generation exits with 0 and one JSON report
    And the generated daily-rate behavior matches the common corpus

    Examples:
      | version | format   |
      | 3       | json     |
      | 4       | json     |
      | 3.0.0   | ion-tree |
      | 4.0.0   | ion-tree |

  Scenario Outline: Missing explicit settings are invocation failures
    Given a daily-rate model at version "3" in "json"
    When I generate F# without "<setting>"
    Then generation exits with 2 and one JSON report
    And no generation output is published

    Examples:
      | setting |
      | input   |
      | target  |
      | output  |

  Scenario Outline: Refused input preserves previous output
    Given a daily-rate model at version "<version>" in "json"
    And previous generated output exists
    And the generation input is "<problem>"
    When I generate F# with explicit settings
    Then generation exits with 1 and one JSON report
    And previous generated output is unchanged

    Examples:
      | version | problem           |
      | 3       | malformed         |
      | 4       | malformed         |
      | 4       | document-literal  |

  Scenario: Equivalent physical representations preserve source and declared order
    Given a daily-rate model at version "3" in "json"
    When I generate F# with explicit settings
    Then generation exits with 0 and one JSON report
    When I generate the equivalent Ion tree
    Then generation exits with 0 and one JSON report
    And the ordered source matches the first representation
