Feature: Morphir validate
  As a user of the Morphir CLI
  I want to check a Morphir model with the built-in analyzers
  So that I can find type mismatches before I generate code

  Background:
    Given a Morphir executable is available

  Scenario: A classic model with a type mismatch gives one type-mismatch error
    When I run the command "validate --input <model-fixtures>/daily-rate-mismatch-v3.json --json"
    Then the exit code should be 1
    And stdout should be one JSON document
    And the validate report should have the exit code 1
    And the validate report should have 1 "error" diagnostic with the code "type-mismatch" from "morphir.type-validator"

  Scenario: A clean classic model gives exit code 0
    When I run the command "validate --input <model-fixtures>/daily-rate-v3.json --json"
    Then the exit code should be 0
    And the validate report should have the exit code 0
    And the validate report should have no diagnostic

  Scenario: A V4 model gives one info diagnostic that names the family
    When I run the command "validate --input <model-fixtures>/daily-rate-v4.json --json"
    Then the exit code should be 0
    And the validate report should have 1 "info" diagnostic with the code "no-analyzer"
    And the validate report should list "morphir.type-validator" as "not-run"

  Scenario: A missing input gives exit code 2 and a diagnostic that names the path
    When I run the command "validate --input missing-model.json"
    Then the exit code should be 2
    And the output should contain "missing-model.json"

  Scenario: The text output is a table and a summary line
    When I run the command "validate --input <model-fixtures>/daily-rate-mismatch-v3.json"
    Then the exit code should be 1
    And stdout should contain "type-mismatch"
    And stdout should contain "1 error, 0 warnings, 0 info, 0 hints."

  Scenario: The validator entries of extension list are the analyzers of validate
    When I run the command "validate --input <model-fixtures>/daily-rate-v3.json --json"
    Then the analyzers of the validate report should be the validator entries of extension list

  Scenario Outline: Both hosts write the same report apart from the host
    When I run the command "validate --input <model-fixtures>/<model> --json"
    And I run "validate --input <model-fixtures>/<model> --json" with the other host
    Then the validate reports of both hosts should be equal apart from the host

    Examples:
      | model                       |
      | daily-rate-mismatch-v3.json |
      | daily-rate-v4.json          |
