@integration @host-native @needs-model-conversion
Feature: The commands of the model convert page give their documented output
  Each command of docs/content/docs/cli/model-convert.md with documented output runs as written in the work area,
  where morphir-ir.json is the catalog's valid v4 model, and its documented output is the real output, paths and all.

  Scenario: The commands to try give their documented output
    Given the section "Try it" of the documentation page "cli/model-convert.md"
    And a valid IR v4 model in the file "morphir-ir.json"
    Then the page documents these commands
      | command                                                                         |
      | morphir model convert --input morphir-ir.json --output tree --layout=document-tree |
      | morphir model convert --input tree --output copy.json --layout=single-file         |
    When I run each documented command of the host as written in the work area
    Then each documented command exits with 0
    And each documented command gives its documented output

  Scenario: The JSON output gives its documented output
    Given the section "JSON output" of the documentation page "cli/model-convert.md"
    And a valid IR v4 model in the file "morphir-ir.json"
    Then the page documents these commands
      | command                                                                                |
      | morphir model convert --input morphir-ir.json --output tree --layout=document-tree --json |
      | morphir model convert --input tree --output copy.json --layout=single-file --json=document |
    When I run each documented command of the host as written in the work area
    Then each documented command exits with 0
    And each documented command gives its documented output

  Scenario: Nothing to do gives its documented report
    Given the section "Nothing to do" of the documentation page "cli/model-convert.md"
    And a valid IR v4 model in the file "morphir-ir.json"
    Then the page documents these commands
      | command                                                                       |
      | morphir model convert --input morphir-ir.json --output other.json --json=document |
    When I run each documented command of the host as written in the work area
    Then each documented command exits with 2
    And each documented command gives its documented output

  Scenario: The layout after a space gives its documented output
    Given the section "Layouts" of the documentation page "cli/model-convert.md"
    And a valid IR v4 model in the file "morphir-ir.json"
    Then the page documents these commands
      | command                                                                         |
      | morphir model convert --input morphir-ir.json --output tree --layout document-tree |
    When I run each documented command of the host as written in the work area
    Then each documented command exits with 0
    And each documented command gives its documented output

  Scenario: The compacted inputs give their documented output
    Given the section "Encodings" of the documentation page "cli/model-convert.md"
    And the v4 tree of the name-context examples, with "every file" compacted
    And the compacted v4 single file of the name-context examples in the file "compacted.json"
    Then the page documents these commands
      | command                                                                                      |
      | morphir model convert --input compacted.json --output plain.json --layout=single-file           |
      | morphir model convert --input model --output billing.json --layout=single-file --json=document |
    When I run each documented command of the host as written in the work area
    Then each documented command exits with 0
    And each documented command gives its documented output
