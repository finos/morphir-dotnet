Feature: Verify Morphir IR
    As a Morphir developer
    I want to validate my IR JSON files against the Morphir IR schema
    So that I can ensure my IR is correctly formatted before using it

Background:
    Given a SchemaLoader instance
    And a SchemaValidator instance

Scenario: Verify valid IR v3 file
    Given a valid IR v3 JSON file
    When I validate the IR against schema version "3"
    Then the validation should succeed
    And there should be no validation errors

Scenario: Verify IR with missing required field
    Given an IR JSON file missing the "formatVersion" field
    When I validate the IR against schema version "3"
    Then the validation should fail
    And there should be validation errors
    And the errors should mention "formatVersion"

Scenario: Verify IR with wrong type for field
    Given an IR JSON file with dependencies as a string instead of an object
    When I validate the IR against schema version "3"
    Then the validation should fail
    And there should be validation errors

Scenario: Verify IR with invalid schema version
    Given a valid IR v3 JSON file
    When I validate the IR against schema version "99"
    Then the validation should throw a FileNotFoundException
    And the error message should contain "Schema file not found"

Scenario: Verify IR with malformed JSON
    Given a malformed JSON file
    When I attempt to validate the IR against schema version "3"
    Then the validation should throw a JsonException

Scenario Outline: Verify IR across multiple versions
    Given a valid IR v<version> JSON file
    When I validate the IR against schema version "<version>"
    Then the validation should succeed
    And there should be no validation errors

    Examples:
      | version |
      | 1       |
      | 2       |
      | 3       |
