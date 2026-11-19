@integration @host-native @needs-model-validation
Feature: The machine output examples of the CLI reference give their documented output
  Each command of the section "Machine output" of docs/content/docs/cli/_index.md runs on a valid v3 model, and its
  documented output is the real output. model.json in the page is that model.

  Scenario: Each documented command gives its documented output
    Given the section "Machine output" of the documentation page "cli/_index.md"
    And a valid IR v3 model
    Then the page documents these commands
      | command                                             |
      | morphir validate --input model.json --json          |
      | morphir validate --input model.json --json=document |
    When I run each documented command of the host on the model
    Then each documented command exits with 0
    And each documented command gives its documented output
