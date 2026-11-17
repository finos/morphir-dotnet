@integration @needs-model-verification
Feature: Verify a Morphir model
  As a developer who uses Morphir
  I want to verify a Morphir model with the CLI
  So that I can check models in my build pipeline

  Scenario: The group lists its commands
    When I ask for the help of the model commands
    Then the exit code is 0
    And the help lists the command "verify" with a description
    And the help lists the command "convert" with a description

  Scenario: A valid v3 model is valid
    Given a valid IR v3 model
    When I verify the model
    Then the exit code is 0
    And the output contains "VALID" or "✓ VALID"
    And the output contains "Schema Version: v3"
    And the output contains "No validation errors found"

  Scenario: A valid v2 model is valid
    Given a valid IR v2 model
    When I verify the model
    Then the exit code is 0
    And the output contains "VALID" or "✓ VALID"
    And the output contains "Schema Version: v2"

  Scenario: A valid v1 model is valid
    Given a valid IR v1 model
    When I verify the model
    Then the exit code is 0
    And the output contains "VALID" or "✓ VALID"
    And the output contains "Schema Version: v1"

  Scenario: A file without a format version is not valid
    Given an IR model with no format version
    When I verify the model
    Then the exit code is 1
    And the output contains "INVALID" or "✗ INVALID"
    And the output contains "validation error"
    And the output contains "formatVersion"

  Scenario: The schema version can be given
    Given a valid IR v3 model
    When I verify the model with the schema version 3
    Then the exit code is 0
    And the output contains "VALID" or "✓ VALID"
    And the output contains "Schema Version: v3 (manual)"

  Scenario: The schema version can be given with the result as JSON
    Given a valid IR v3 model
    When I verify the model with the schema version 3 as JSON
    Then the exit code is 0
    And standard output is one JSON document
    And the verification report says that the model is valid
    And the verification report has the schema version "3"
    And the verification report has the detection method "manual"

  Scenario: The result of a valid file as JSON
    Given a valid IR v3 model
    When I verify the model as JSON
    Then the exit code is 0
    And standard output is one JSON document
    And the verification report says that the model is valid
    And the verification report has the schema version "3"
    And the verification report states the exit code 0

  Scenario: Quiet mode writes nothing for a valid file
    Given a valid IR v3 model
    When I verify the model in quiet mode
    Then the exit code is 0
    And the output is empty

  Scenario: Quiet mode writes nothing for a file that is not valid
    Given an IR model with no format version
    When I verify the model in quiet mode
    Then the exit code is 1
    And the output is empty

  Scenario: JSON output is written in quiet mode
    Given an IR model with no format version
    When I verify the model in quiet mode as JSON
    Then the exit code is 1
    And standard output is one JSON document
    And the verification report says that the model is not valid
    And the verification report states the exit code 1

  Scenario: A value of the wrong type shows what was expected and what was found
    Given an IR model whose format version is a string
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
    Given a model file that is not JSON
    When I verify the model
    Then the exit code is 1
    And the output contains "error"

  Scenario: The v3 format is detected
    Given a valid IR v3 model
    When I verify the model
    Then the exit code is 0
    And the output contains "Schema Version: v3 (auto)"

  Scenario: The result of a file that is not valid as JSON has the errors
    Given an IR model with no format version
    When I verify the model as JSON
    Then the exit code is 1
    And standard output is one JSON document
    And the verification report says that the model is not valid
    And the verification report has errors
    And the verification report states the exit code 1

  @needs-tool-routing
  Scenario: The former command is unknown
    Given a valid IR v3 model
    When I verify the model with the former command, and no morphir-ir is on the search path
    Then the command fails and names "ir" as an unknown command
    And the model was not verified
