@Generation @CLI @PlatformSpecific
Feature: Generate executable libraries from Morphir models
  The scenarios of this feature run the generated F# against the common corpus of the product (GenerationCases),
  in the test process. Each other scenario of the F# generation, and the same scenarios without the corpus, are in
  tests/Morphir.BlackBox.Tests/Integration/FSharpGeneration.feature.

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
