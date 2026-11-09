Feature: Morphir extension list
  As a user of the Morphir CLI
  I want to list the extensions that my host knows
  So that I can see which capabilities I can use in morphir and in dotnet-morphir

  Background:
    Given a Morphir executable is available

  Scenario: The built-in generators and analyzers are listed as one JSON document on stdout
    When I run the command "extension list --json"
    Then the exit code should be 0
    And stdout should be one JSON document
    And the listed host should be the host of the executable under test
    And the listed extension ids should be "morphir.csharp, morphir.fsharp, morphir.type-validator"
    And every listed extension should be available in this host

  Scenario: The type validator is a built-in validator for the classic IR versions
    When I run the command "extension list --json"
    Then the exit code should be 0
    And the extension "morphir.type-validator" should have the types "validator"
    And the extension "morphir.type-validator" should have the delivery "built-in" and the hosts "morphir, dotnet-morphir"
    And the extension "morphir.type-validator" should claim the validator IR versions "1, 2, 3"

  Scenario: The text table shows the validator claim
    When I run the command "extension list"
    Then the exit code should be 0
    And stdout should contain "validator (1, 2, 3)"

  Scenario: Both hosts list the same shared built-ins
    When I run the command "extension list --json"
    And I run "extension list --json" with the other host
    Then the extensions available in both hosts should be equal in both results
