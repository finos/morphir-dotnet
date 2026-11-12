@integration @needs-generation @needs-host-without-dynamic-code
Feature: Format generated F# on a host that cannot format
  The F# backend option format is true by default: the backend formats with Fantomas when the host can. A host
  without dynamic code, as the Native AOT morphir, cannot. It says so on standard error, never on standard output,
  and writes the bytes of format=false. The development build runs such a host with the dynamic-code switch of the
  runtime off.

  Scenario: The default warns on standard error only, and the report says unavailable
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "fsharp" from the model on a host without dynamic code
    Then generation exits with 0 and one JSON report
    And standard error contains "warning:"
    And standard error contains "format=false"
    And standard output does not contain "warning"
    And the generation report has the formatting "unavailable"

  Scenario Outline: The default writes the bytes of format false
    Given the model fixture "<fixture>"
    When I generate "fsharp" from the model on a host without dynamic code
    Then generation exits with 0 and one JSON report
    And the generation report has the formatting "unavailable"
    Given I remember the files of the output
    When I generate "fsharp" from the model on a host without dynamic code with the option "format=false"
    Then generation exits with 0 and one JSON report
    And the generation report has artifacts
    And the files of the output are the same as I remembered

    Examples:
      | fixture                                         |
      | WireForms/daily-rate-4.json                     |
      | Toolchain/fsharp-compile-generate/ir-4.0.0.json |

  Scenario: Text output keeps standard output for the artifact paths
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "fsharp" from the model as text on a host without dynamic code
    Then the exit code is 0
    And standard error contains "warning:"
    And standard output lists only the artifacts of the output

  Scenario: With format false the host does not warn and writes the bytes of a JIT host
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "fsharp" from the model with the option "format=false"
    Then generation exits with 0 and one JSON report
    Given I remember the files of the output
    When I generate "fsharp" from the model on a host without dynamic code with the option "format=false"
    Then generation exits with 0 and one JSON report
    And standard error does not contain "warning"
    And the generation report has the formatting "off"
    And the files of the output are the same as I remembered
