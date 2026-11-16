@integration @host-native @needs-generation
Feature: The documented upgrade of a classic model gives its documented report
  The section "The current IR" of docs/content/docs/pipeline/middle-end.md generates C# from a classic model. model.json
  in the page is the classic daily-rate fixture of tests/Fixtures/MorphirIR. The C# backend reads the current IR only,
  so the command upgrades the model before the passes and the backend, and the report lists the passes that ran and
  the application list.

  Scenario: The documented command gives its documented report
    Given the section "The current IR" of the documentation page "pipeline/middle-end.md"
    And the model fixture "Models/daily-rate-v3.json" as "model.json"
    Then the page documents these commands
      | command                                                                 |
      | morphir generate --input model.json --target csharp --output gen --json |
    When I run each documented command of the host on the model
    Then each documented command exits with 0
    And each documented command gives its documented output
