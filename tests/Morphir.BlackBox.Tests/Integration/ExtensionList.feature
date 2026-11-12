@integration
Feature: List the extensions of the host
  As a user of the Morphir CLI
  I want to list the extensions that my host knows
  So that I can see which capabilities I can use in morphir and in dotnet-morphir

  Scenario: The built-in generators and analyzers are listed as one JSON document on standard output
    When I list the extensions as JSON
    Then the exit code is 0
    And standard output is one JSON document
    And the extension list names the host of the run
    And the extension list has the ids "morphir-dotnet.fsharp, morphir.csharp, morphir.fsharp, morphir.type-validator"
    And each listed extension except "morphir-dotnet.fsharp" is available in the host
    And the in-process F# frontend is available exactly when the host has it

  @needs-known-form
  Scenario: The list names the deployment that built the executable
    When I list the extensions as JSON
    Then the exit code is 0
    And the extension list names the host of the run
    And the extension list names the deployment of the target

  Scenario: The type validator is a built-in validator for the classic IR versions
    When I list the extensions as JSON
    Then the exit code is 0
    And the extension "morphir.type-validator" has the types "validator"
    And the extension "morphir.type-validator" has the delivery "built-in" and the hosts "morphir, dotnet-morphir"
    And the extension "morphir.type-validator" claims the validator IR versions "1, 2, 3"

  Scenario: The text table shows the validator claim
    When I list the extensions as text
    Then the exit code is 0
    And standard output contains "validator (1, 2, 3)"

  @needs-native-and-tool-hosts
  Scenario: Both hosts list the same shared built-ins
    When I list the extensions as JSON
    And I list the extensions as JSON on the other host
    Then the extensions that are available in both hosts are equal in both lists
