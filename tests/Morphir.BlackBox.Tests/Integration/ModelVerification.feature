@integration @needs-model-verification
Feature: Verify a Morphir model
  As a developer who uses Morphir
  I want to verify a Morphir model with the CLI
  So that I can check models in my build pipeline

  Scenario: The group lists its commands
    When I ask for the help of the model commands
    Then the exit code is 0
    And the help lists the command "verify" with a description

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
    And standard error is empty
    And the verification report says that the model is not valid
    And the verification report states the exit code 1

  Scenario: The report states its exit code as a JSON document
    Given an IR model with no format version
    When I verify the model as a JSON document
    Then the exit code is 1
    And standard output is one JSON document
    And the verification report says that the model is not valid
    And the verification report states the exit code 1

  Scenario: A usage failure as a JSON document states the exit code 2
    Given a valid IR v3 model
    When I verify the model as a JSON document with an option that it does not have
    Then the exit code is 2
    And standard output is one JSON document
    And the verification report says that the model is not valid
    And the verification report states the exit code 2

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

  Scenario: The former command is unknown
    Given a valid IR v3 model
    When I verify the model with the former command
    Then the command fails and names "ir" as an unknown command
    And the model was not verified

  Scenario: A valid v4 model is valid
    Given a valid IR v4 model
    When I verify the model
    Then the exit code is 0
    And the output contains "Schema Version: v4 (auto)"
    And the output contains "No validation errors found"

  Scenario: The result of a valid v4 model as JSON
    Given a valid IR v4 model
    When I verify the model as JSON
    Then the exit code is 0
    And the verification report says that the model is valid
    And the verification report has the schema version "4"
    And the verification report has the layout "single-file"

  Scenario: The v4 schema version can be given
    Given a valid IR v4 model
    When I verify the model with the schema version 4 as JSON
    Then the exit code is 0
    And the verification report says that the model is valid
    And the verification report has the schema version "4"
    And the verification report has the detection method "manual"

  Scenario: An invalid v4 model names its error
    Given an IR v4 model with an unknown tag in one type node
    When I verify the model as JSON
    Then the exit code is 1
    And the verification report says that the model is not valid
    And the verification report has the schema version "4"
    And an error of the verification report names the place of the fault

  Scenario: A valid v4 tree is valid
    Given the tree that model convert wrote from a v4 Library with a dependency
    When I verify the model as JSON
    Then the exit code is 0
    And the verification report says that the model is valid
    And the verification report has the schema version "4"
    And the verification report has the layout "document-tree"
    And the verification report counts every file of the tree

  Scenario: A valid v3 tree is valid
    Given the tree that model convert wrote from a v3 Library with a dependency
    When I verify the model as JSON
    Then the exit code is 0
    And the verification report says that the model is valid
    And the verification report has the schema version "3"
    And the verification report has the layout "document-tree"
    And the verification report counts every file of the tree

  Scenario: An error inside one tree file names the file
    Given the tree that model convert wrote from a v4 Library with a dependency, with "an unknown tag in one type node"
    When I verify the model as JSON
    Then the exit code is 1
    And the verification report says that the model is not valid
    And an error of the verification report names the place of the fault

  Scenario: The text of an error inside one tree file names the file
    Given the tree that model convert wrote from a v4 Library with a dependency, with "an unknown tag in one type node"
    When I verify the model
    Then the exit code is 1
    And the output contains "Layout: document-tree"
    And the output names the file of the fault

  Scenario: A broken tree rule is an error
    Given the tree that model convert wrote from a v4 Library with a dependency, with "no file for one value"
    When I verify the model as JSON
    Then the exit code is 1
    And the verification report says that the model is not valid
    And an error of the verification report has the code "missing_member" and names the place of the fault

  Scenario: A schema version that contradicts the manifest
    Given the tree that model convert wrote from a v4 Library with a dependency
    When I verify the model with the schema version 3 as JSON
    Then the exit code is 2
    And the verification report says that the model is not valid
    And the verification report states the exit code 2
    And an error of the verification report has the code "verify.schema_version_conflict" and names the versions 3 and 4

  Scenario: A schema version that no IR has is a usage error
    Given a valid IR v4 model
    When I verify the model with the schema version 7 as JSON
    Then the exit code is 2
    And the verification report says that the model is not valid
    And the verification report states the exit code 2
