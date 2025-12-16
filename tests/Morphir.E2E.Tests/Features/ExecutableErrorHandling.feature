Feature: Morphir Executable Error Handling
  As a user of the Morphir CLI
  I want proper error messages when things go wrong
  So that I can diagnose and fix issues

  Background:
    Given a Morphir executable is available

  Scenario: Verify nonexistent file returns error
    When I run the command "ir verify <test-data>/nonexistent.json"
    Then the exit code should be 1
    And the output should contain "error" or "not found"

  Scenario: Verify malformed JSON returns error
    When I run the command "ir verify <test-data>/malformed.json"
    Then the exit code should be 1
    And the output should contain "error"

