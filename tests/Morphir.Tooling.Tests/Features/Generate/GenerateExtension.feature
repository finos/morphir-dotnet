@Generation @CLI @PlatformSpecific
Feature: Select an external generation backend
  The scenario of this feature compares the files of the guest with the built-in F# backend in the test process.
  Each other scenario of the external backend, and the same scenario without the comparison, are in
  tests/Morphir.BlackBox.Tests/Integration/GenerationExtension.feature.

  Scenario: An explicit extension gives the built-in's files, and the report names the extension
    Given a daily-rate model at version "4" in "json"
    When I generate F# with the external F# backend extension
    Then generation exits with 0 and one JSON report
    And the report names the external F# backend over MEP
    And the generated files equal those of the built-in F# backend
