@integration @needs-model-verification
Feature: Verify a Morphir IR file
  As a developer who uses Morphir
  I want to verify a Morphir IR JSON file with the CLI
  So that I can check IR files in my build pipeline

  Scenario: A valid IR v3 file is valid
    Given the model fixture "Models/valid-ir-v3.json"
    When I verify the model
    Then the exit code is 0
    And the output contains "VALID" or "✓ VALID"
    And the output contains "Schema Version: v3"
    And the output contains "No validation errors found"

  Scenario: A valid IR v2 file is valid
    Given the model fixture "Models/valid-ir-v2.json"
    When I verify the model
    Then the exit code is 0
    And the output contains "VALID" or "✓ VALID"
    And the output contains "Schema Version: v2"

  Scenario: A valid IR v1 file is valid
    Given the model fixture "Models/valid-ir-v1.json"
    When I verify the model
    Then the exit code is 0
    And the output contains "VALID" or "✓ VALID"
    And the output contains "Schema Version: v1"

  Scenario: An IR file with no format version is not valid
    Given the model fixture "Models/invalid-missing-formatversion.json"
    When I verify the model
    Then the exit code is 1
    And the output contains "INVALID" or "✗ INVALID"
    And the output contains "validation error"
    And the output contains "formatVersion"

  Scenario: The schema version can be given
    Given the model fixture "Models/valid-ir-v3.json"
    When I verify the model with the schema version 3
    Then the exit code is 0
    And the output contains "VALID" or "✓ VALID"
    And the output contains "Schema Version: v3 (manual)"

  Scenario: The schema version can be given with the result as JSON
    Given the model fixture "Models/valid-ir-v3.json"
    When I verify the model with the schema version 3 as JSON
    Then the exit code is 0
    And standard output is one JSON document
    And the verification report says that the model is valid
    And the verification report has the schema version "3"
    And the verification report has the detection method "manual"

  Scenario: The result of a valid file as JSON
    Given the model fixture "Models/valid-ir-v3.json"
    When I verify the model as JSON
    Then the exit code is 0
    And standard output is one JSON document
    And the verification report says that the model is valid
    And the verification report has the schema version "3"

  Scenario: Quiet mode writes nothing for a valid file
    Given the model fixture "Models/valid-ir-v3.json"
    When I verify the model in quiet mode
    Then the exit code is 0
    And the output is empty

  Scenario: Quiet mode writes nothing for a file that is not valid
    Given the model fixture "Models/invalid-missing-formatversion.json"
    When I verify the model in quiet mode
    Then the exit code is 1
    And the output is empty

  Scenario: A value of the wrong type shows what was expected and what was found
    Given the model fixture "Models/invalid-wrong-type-formatversion.json"
    When I verify the model
    Then the exit code is 1
    And the output contains "Expected:"
    And the output contains "Found:"

  Scenario: A file that is not there is an error
    Given no model file "nonexistent.json"
    When I verify the model
    Then the exit code is 1
    And the output contains "error" or "not found"

  Scenario: A file that is not JSON is an error
    Given the model fixture "Models/malformed.json"
    When I verify the model
    Then the exit code is 1
    And the output contains "error"

  Scenario: The v3 format is detected
    Given the model fixture "Models/valid-ir-v3.json"
    When I verify the model
    Then the exit code is 0
    And the output contains "Schema Version: v3 (auto)"

  Scenario: The result of a file that is not valid as JSON has the errors
    Given the model fixture "Models/invalid-missing-formatversion.json"
    When I verify the model as JSON
    Then the exit code is 1
    And standard output is one JSON document
    And the verification report says that the model is not valid
    And the verification report has errors
