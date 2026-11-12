@integration @needs-model-validation
Feature: Validate a Morphir model with the analyzers of the host
  morphir validate through the real CLI process. Standard output holds only the report.

  Scenario Outline: Standard output holds only the JSON report, and its exit code is the exit code of the process
    Given the model fixture "Models/<fixture>"
    When I validate the model as JSON
    Then validation exits with <exit> and one JSON report

    Examples:
      | fixture                     | exit |
      | daily-rate-mismatch-v3.json | 1    |
      | daily-rate-v3.json          | 0    |
      | daily-rate-v4.json          | 0    |

  Scenario: A missing input exits with 2 and names the path
    Given no model file "missing.json"
    When I validate the model
    Then the exit code is 2
    And standard output is empty
    And standard error contains "missing.json"

  Scenario: An unknown option exits with 2
    Given no model file "model.json"
    When I validate the model as JSON with the arguments "--no-such-option"
    Then validation exits with 2 and one JSON report

  Scenario: The text output is a plain table when standard output is redirected
    Given the model fixture "Models/daily-rate-mismatch-v3.json"
    When I validate the model
    Then the exit code is 1
    And the validation text is a plain table with the error "type-mismatch"

  @needs-all-hosts
  Scenario Outline: Both hosts write the same report apart from the host
    Given the model fixture "Models/<fixture>"
    When I validate the model as JSON on the native host and on the tool host
    Then both hosts write the same validation report apart from the host

    Examples:
      | fixture                     |
      | daily-rate-mismatch-v3.json |
      | daily-rate-v4.json          |
