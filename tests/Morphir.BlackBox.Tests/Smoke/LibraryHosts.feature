@smoke @needs-built-programs
Feature: Each host of a shipped library runs as a managed program
  As a developer who uses a Morphir library in an application
  I want each library that the CLI does not reach to run in a small host program
  So that I know that the library starts and does its basic functions outside the CLI

  Scenario Outline: A host that writes one JSON result reports success
    When I run the program "<host>"
    Then the exit code is 0
    And the JSON document on standard output has the field "success" with the value true

    Examples:
      | host                           |
      | configuration-io-smoke         |
      | configuration-microsoft-smoke  |
      | configuration-inspection-smoke |

  Scenario: The host of the compilation library passes each of its checks
    When I run the program "compilation-smoke"
    Then the exit code is 0
    And the output contains "ok   diagnostic text"

  Scenario: The host of the compatibility kit writes one report of the kit
    When I run the program "conformance-smoke"
    Then the exit code is 0
    And standard output is one JSON document
