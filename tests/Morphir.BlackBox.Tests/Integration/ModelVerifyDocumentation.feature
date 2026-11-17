@integration @host-native @needs-model-verification
Feature: The commands of the model verify page give their documented output
  Each command of docs/content/docs/cli/model-verify.md with documented output runs against the test fixtures, and its
  documented output is the real output. model.json in the page is the catalog model with no format version, and the
  directory model/ is the written v4 tree with no file for one value.

  Scenario: Each documented command gives its documented output
    Given the section "Options" of the documentation page "cli/model-verify.md"
    And an IR model with no format version
    Then the page documents these commands
      | command                                         |
      | morphir model verify model.json --json=document |
      | morphir model verify --quiet --json model.json  |
    When I run each documented command of the host on the model
    Then each documented command exits with 1
    And each documented command gives its documented output

  Scenario: The documented command of a tree gives its documented output
    Given the section "Document trees" of the documentation page "cli/model-verify.md"
    And the tree that the store writes from a v4 Library with a dependency, with "no file for one value"
    Then the page documents these commands
      | command                                     |
      | morphir model verify model/ --json=document |
    When I run each documented command of the host on the model tree
    Then each documented command exits with 1
    And each documented command gives its documented output

  Scenario: The documented command of a compacted tree gives its documented output
    Given the section "Compacted files" of the documentation page "cli/model-verify.md"
    And the v4 tree of the name-context examples, with "every file" compacted
    Then the page documents these commands
      | command                                     |
      | morphir model verify model/ --json=document |
    When I run each documented command of the host on the model tree
    Then each documented command exits with 0
    And each documented command gives its documented output
