@integration @needs-validation
Feature: Validate a Morphir model
  As a user of the Morphir CLI
  I want to check a Morphir model with the built-in analyzers
  So that I can find type mismatches before I generate code

  Scenario: A classic model with a type mismatch gives one type-mismatch error
    Given the model fixture "Models/daily-rate-mismatch-v3.json"
    When I validate the model
    Then the exit code is 1
    And standard output is one JSON document
    And the validation report has the exit code 1
    And the validation report has 1 "error" diagnostic with the code "type-mismatch" from "morphir.type-validator"

  Scenario: A clean classic model gives exit code 0
    Given the model fixture "Models/daily-rate-v3.json"
    When I validate the model
    Then the exit code is 0
    And the validation report has the exit code 0
    And the validation report has no diagnostic

  Scenario: A V4 model gives one info diagnostic that names the family
    Given the model fixture "Models/daily-rate-v4.json"
    When I validate the model
    Then the exit code is 0
    And the validation report has 1 "info" diagnostic with the code "no-analyzer"
    And the validation report lists "morphir.type-validator" as "not-run"

  Scenario: A missing input gives exit code 2 and a diagnostic that names the path
    Given no model file "missing-model.json"
    When I validate the model
    Then the exit code is 2
    And the output contains "missing-model.json"

  Scenario: The text output is a table and a summary line
    Given the model fixture "Models/daily-rate-mismatch-v3.json"
    When I validate the model as text
    Then the exit code is 1
    And standard output contains "type-mismatch"
    And standard output contains "1 error, 0 warnings, 0 info, 0 hints."

  Scenario: The validator entries of extension list are the analyzers of validate
    Given the model fixture "Models/daily-rate-v3.json"
    When I validate the model
    Then the analyzers of the validation report are the validator entries of extension list

  @needs-native-and-tool-hosts
  Scenario Outline: Both hosts write the same report apart from the host
    Given the model fixture "Models/<model>"
    When I validate the model
    And I validate the model on the other host
    Then the validation reports of both hosts are equal apart from the host

    Examples:
      | model                       |
      | daily-rate-mismatch-v3.json |
      | daily-rate-v4.json          |
