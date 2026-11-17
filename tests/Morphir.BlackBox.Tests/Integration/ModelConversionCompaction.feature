@integration @needs-model-conversion
Feature: Model conversion of compacted files
  As a developer who uses Morphir
  I want model convert to read compacted Morphir files
  So that I can turn a compacted model back into plain files that every tool reads

  The models are the parent's name-context examples (ir/name-compaction): single files and the files of one small
  tree, each compacted or as its plain twin. A compacted model is the same model as its plain twin, so each scenario
  that expands one compares what it wrote with what the plain twin converts to.

  Scenario: A compacted v4 single file becomes a plain single file
    Given the compacted v4 single file of the name-context examples
    When I convert the model to a single file
    Then the conversion succeeds
    And the conversion report states the encodings "compacted" and "plain"
    And the written single file is plain
    And the written single file says the format version 4
    When I convert the written model to a document tree
    And I convert the plain twin of the model to a document tree
    Then the conversion succeeds
    And the last two written trees are the same

  Scenario: A compacted v3 single file expands to v3
    Given the compacted v3 single file of the name-context examples
    When I convert the model to a single file
    Then the conversion succeeds
    And the conversion report states the encodings "compacted" and "plain"
    And the conversion report states the IR version "3"
    And the written single file is plain
    And the written single file says the format version 3
    When I convert the written model to a document tree
    And I convert the plain twin of the model to a document tree
    Then the conversion succeeds
    And the last two written trees are the same

  Scenario: A compacted tree becomes a plain single file
    Given the v4 tree of the name-context examples, with "every file" compacted
    When I convert the model to a single file
    Then the conversion succeeds
    And the conversion report states the layouts "document-tree" and "single-file"
    And the conversion report states the encodings "compacted" and "plain"
    And the written single file is plain
    When I convert the plain twin of the model to a single file
    Then the conversion succeeds
    And the last two written single files are the same

  Scenario: A tree with one compacted file is compacted
    Given the v4 tree of the name-context examples, with "only its value file" compacted
    When I convert the model to a single file
    Then the conversion succeeds
    And the conversion report states the encodings "compacted" and "plain"
    When I convert the plain twin of the model to a single file
    Then the conversion succeeds
    And the last two written single files are the same

  Scenario Outline: A refused context writes nothing
    Given the v4 tree of the name-context examples, with "<fault>" in its type file
    When I convert the model to a single file
    Then the conversion fails with the exit code 1 and the diagnostic "<code>"
    And the diagnostic "<code>" names the place of the fault
    And nothing is at the output

    Examples:
      | fault                                         | code                   |
      | a reference to an alias that no table defines | context.unknown_alias  |
      | a document before its context                 | context.after_document |

  Scenario: An undeclared envelope is refused
    Given the v4 tree of the name-context examples, with "an envelope schema that this version does not declare" in its type file
    When I convert the model to a single file
    Then the conversion fails with the exit code 1 and the diagnostic "context.undeclared_schema"
    And the diagnostic "context.undeclared_schema" names the place of the fault
    And the conversion report states no input encoding
    And nothing is at the output
