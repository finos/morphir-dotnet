Feature: CLI IR Verification Integration
  As a developer using Morphir
  I want to verify Morphir IR JSON files via the CLI
  So that I can validate IR files in my build pipeline

  Background:
    Given the Morphir CLI is available

  @Integration @CLI
  Scenario: Verify valid IR v3 file returns success
    When I run the CLI with command "ir verify <test-data>/valid-ir-v3.json"
    Then the exit code should be 0
    And the output should contain "VALID" or "✓ VALID"
    And the output should contain "Schema Version: v3"
    And the output should contain "No validation errors found"

  @Integration @CLI
  Scenario: Verify valid IR v2 file returns success
    When I run the CLI with command "ir verify <test-data>/valid-ir-v2.json"
    Then the exit code should be 0
    And the output should contain "VALID" or "✓ VALID"
    And the output should contain "Schema Version: v2"

  @Integration @CLI
  Scenario: Verify valid IR v1 file returns success
    When I run the CLI with command "ir verify <test-data>/valid-ir-v1.json"
    Then the exit code should be 0
    And the output should contain "VALID" or "✓ VALID"
    And the output should contain "Schema Version: v1"

  @Integration @CLI
  Scenario: Verify invalid IR file returns failure
    When I run the CLI with command "ir verify <test-data>/invalid-missing-formatversion.json"
    Then the exit code should be 1
    And the output should contain "✗ INVALID"
    And the output should contain "validation error"
    And the output should contain "formatVersion"

  @Integration @CLI
  Scenario: Verify with explicit schema version
    When I run the CLI with command "ir verify <test-data>/valid-ir-v3.json --schema-version 3"
    Then the exit code should be 0
    And the output should contain "VALID" or "✓ VALID"
    And the output should contain "Schema Version: v3 (manual)"

  @Integration @CLI
  Scenario: Verify with JSON output format
    When I run the CLI with command "ir verify <test-data>/valid-ir-v3.json --json"
    Then the exit code should be 0
    And the output should be valid JSON
    And the JSON output should have field "IsValid" with value "true"
    And the JSON output should have field "SchemaVersion" with value "3"

  @Integration @CLI
  Scenario: Verify with quiet mode for valid file
    When I run the CLI with command "ir verify <test-data>/valid-ir-v3.json --quiet"
    Then the exit code should be 0
    And the output should be empty

  @Integration @CLI
  Scenario: Verify with quiet mode for invalid file
    When I run the CLI with command "ir verify <test-data>/invalid-missing-formatversion.json --quiet"
    Then the exit code should be 1
    And the output should be empty

  @Integration @CLI
  Scenario: Verify shows enhanced error details for type mismatch
    When I run the CLI with command "ir verify <test-data>/invalid-wrong-type-formatversion.json"
    Then the exit code should be 1
    And the output should contain "Expected:"
    And the output should contain "Found:"

  @Integration @CLI
  Scenario: Verify nonexistent file returns error
    When I run the CLI with command "ir verify <test-data>/nonexistent.json"
    Then the exit code should be 1
    And the output should contain "error" or "not found"

  @Integration @CLI
  Scenario: Verify malformed JSON returns error
    When I run the CLI with command "ir verify <test-data>/malformed.json"
    Then the exit code should be 1
    And the output should contain "error"

  @Integration @CLI
  Scenario: Verify with auto-detected v3 format
    When I run the CLI with command "ir verify <test-data>/valid-ir-v3.json"
    Then the exit code should be 0
    And the output should contain "Schema Version: v3 (auto)"

  @Integration @CLI
  Scenario: Verify JSON output for invalid file includes errors
    When I run the CLI with command "ir verify <test-data>/invalid-missing-formatversion.json --json"
    Then the exit code should be 1
    And the output should be valid JSON
    And the JSON output should have field "IsValid" with value "false"
    And the JSON output should have field "Errors" that is not empty
