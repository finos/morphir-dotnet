@integration @host-tool @needs-compilation @needs-in-process-fsharp-frontend
Feature: The F# pricing tutorial refuses a guarded discount as it documents
  docs/content/docs/guides/fsharp-pricing-tutorial.md shows a Discount.fs with a pattern guard, and the error that the
  compile gives for it.

  Scenario: The guarded discount is refused as documented
    Given a copy of the example of the tutorial
    And the guarded discount of the tutorial in the example
    When I compile the example
    Then the exit code is 1
    And standard error contains "src/Discount.fs:6:18: error fsharp.unsupported_pattern_guard: Pattern guards ('when') are not supported; a Morphir pattern has no condition. Match the case, then use 'if' in its body."
    And standard error contains "compile failed (source, exit code 1); 1 error(s): error fsharp.unsupported_pattern_guard"
