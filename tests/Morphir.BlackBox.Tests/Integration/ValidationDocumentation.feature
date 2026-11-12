@integration @host-native @needs-model-validation
Feature: The commands of the validate page give their documented output
  Each command of docs/content/docs/cli/validate.md runs against the test fixtures, and its documented output is the
  real output. model.json in the page is the classic fixture with a type mismatch. ValidateDocumentationTests of
  Morphir.Tooling.Tests checks the configuration example of the page with the validation library, in the test process.

  Scenario: Each documented command gives its documented output
    Given the documentation page "cli/validate.md"
    And the model fixture "Models/daily-rate-mismatch-v3.json" as "model.json"
    Then the page documents these commands
      | command                                    |
      | morphir validate --input model.json        |
      | morphir validate --input model.json --json |
    When I run each documented command of the host on the model
    Then each documented command exits with 1
    And each documented command gives its documented output
