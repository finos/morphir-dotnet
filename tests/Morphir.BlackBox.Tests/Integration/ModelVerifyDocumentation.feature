@integration @host-native @needs-model-verification
Feature: The commands of the model verify page give their documented output
  Each command of docs/content/docs/cli/model-verify.md with documented output runs against the test fixtures, and its
  documented output is the real output. model.json in the page is the catalog model with no format version.

  Scenario: Each documented command gives its documented output
    Given the documentation page "cli/model-verify.md"
    And an IR model with no format version
    Then the page documents these commands
      | command                                         |
      | morphir model verify model.json --json=document |
      | morphir model verify --quiet --json model.json  |
    When I run each documented command of the host on the model
    Then each documented command exits with 1
    And each documented command gives its documented output
