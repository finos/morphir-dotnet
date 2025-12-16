Feature: Morphir Executable Basic Commands
  As a user of the Morphir CLI
  I want to execute basic commands
  So that I can verify the executable works correctly

  Background:
    Given a Morphir executable is available

  Scenario: Executable shows help information
    When I run the command "--help"
    Then the exit code should be 0
    And the output should contain "Morphir command line"
    And the output should contain "Commands:"

  Scenario: Executable shows version information
    When I run the command "--version"
    Then the exit code should be 0
    And the output should match the semantic version pattern

  Scenario: Executable info command works
    When I run the command "info"
    Then the exit code should be 0

