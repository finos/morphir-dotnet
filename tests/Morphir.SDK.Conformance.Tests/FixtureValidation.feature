Feature: Offline shared SDK evidence
  Both .NET SDK workers need the same independent expectations and strict coverage.

  Scenario: The checked-in contract validates without another runtime
    Given the checked-in SDK corpus
    When I validate the corpus offline
    Then there are no conformance fixture errors

  Scenario Outline: Malformed cases fail with a case identity and field
    Given the checked-in SDK corpus
    And I introduce the fixture defect "<defect>"
    When I validate the corpus offline
    Then a conformance fixture error contains "<field>"

    Examples:
      | defect              | field          |
      | duplicate case      | caseId         |
      | missing expectation | expected       |
      | unknown operation   | operation      |
      | invalid tag         | tag            |
      | incomplete error    | detail         |
      | unclassified case   | classification |
