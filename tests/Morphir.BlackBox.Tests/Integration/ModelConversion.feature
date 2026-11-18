@integration @needs-model-conversion
Feature: Model conversion
  As a developer who uses Morphir
  I want to store a Morphir model as one file or as a document tree with the CLI
  So that I can choose the layout that suits my tools without changing the model

  Each scenario names its model by intent, and the model conversion driver builds the command line. The kit's
  document-tree cases are in ModelConversionKitCases.feature.

  Scenario: A directory is read as a tree
    Given the tree that the store writes from a v4 Library with a dependency
    When I convert the model to a single file
    Then the conversion succeeds
    And the conversion report states the layouts "document-tree" and "single-file"

  Scenario: The IR version is kept
    Given a valid IR v3 model
    When I convert the model to a document tree
    Then the conversion succeeds
    And the conversion report states the IR version "3"
    When I convert the written model to a single file
    Then the conversion succeeds
    And the conversion report states the IR version "3"
    And the written single file says the format version 3

  Scenario: Nothing to do
    Given a valid IR v4 model
    When I convert the model without changing its layout or its encoding
    Then the conversion fails with the exit code 2 and the diagnostic "model.nothing_to_do"
    And the diagnostic "model.nothing_to_do" names the input as "a plain single file"
    And nothing is at the output

  Scenario: A refused tree writes nothing
    Given the tree that the store writes from a v4 Library with a dependency, with "no file for one value"
    When I convert the model to a single file
    Then the conversion fails with the exit code 1 and the diagnostic "missing_member"
    And the diagnostic "missing_member" names the place of the fault
    And nothing is at the output

  Scenario: A member that the classic model has no place for is refused, not dropped
    Given the tree that the store writes from a v3 Library with a dependency, with "an unknown member in one value definition"
    When I convert the model to a single file
    Then the conversion fails with the exit code 1 and the diagnostic "unknown_member"
    And the diagnostic "unknown_member" names the place of the fault
    And nothing is at the output

  Scenario: A non-empty output is refused
    Given a valid IR v4 model
    And the output is a directory that holds a file
    When I convert the model to a document tree
    Then the conversion fails with the exit code 1 and the diagnostic "model.output_not_empty"
    And the diagnostic "model.output_not_empty" names the output
    And the file in the output is unchanged

  Scenario: Every written file is reported
    Given a valid IR v4 model with two modules
    When I convert the model to a document tree
    Then the conversion succeeds
    And the conversion report lists every file of the written tree
    And the stream has one fileWritten record for each written file

  Scenario: Notices are in the report
    Given a v4 tree whose manifest has a version and a created date
    When I convert the model to a single file
    Then the exit code is 0
    And the conversion succeeds
    And the conversion report has 2 notices
    And the stream has the notices of the conversion report

  Scenario: A v3 Library returns its original label
    Given a valid IR v3 model
    When I convert the model to a document tree
    Then the conversion succeeds
    And every file of the written tree says the format version "3.1.0"
    When I convert the written model to a single file
    Then the conversion succeeds
    And the written single file says the format version 3

  Scenario: Version and created are reported
    Given a v4 tree whose manifest has a version and a created date
    When I convert the model to a single file
    Then the conversion succeeds
    And the conversion report has a notice "tree.dropped_manifest_member" that names "version"
    And the conversion report has a notice "tree.dropped_manifest_member" that names "created"

  Scenario: Ambiguous profile
    Given the tree that the store writes from a v4 Library with a dependency, with "a second manifest in the YAML profile"
    When I convert the model to a single file
    Then the conversion fails with the exit code 1 and the diagnostic "tree.ambiguous_profile"
    And the diagnostic "tree.ambiguous_profile" names the place of the fault
    And nothing is at the output

  Scenario: A valid v4 tree is valid when model convert wrote it
    Given the tree that the store writes from a v4 Library with a dependency
    When I convert the model to a single file
    And I convert the written model to a document tree
    Then the conversion succeeds
    When I verify the written tree as JSON
    Then the exit code is 0
    And the verification report says that the model is valid
    And the verification report has the schema version "4"
    And the verification report has the layout "document-tree"
    And the verification report counts every file of the tree

  Scenario: A valid v3 tree is valid when model convert wrote it
    Given the tree that the store writes from a v3 Library with a dependency
    When I convert the model to a single file
    And I convert the written model to a document tree
    Then the conversion succeeds
    When I verify the written tree as JSON
    Then the exit code is 0
    And the verification report says that the model is valid
    And the verification report has the schema version "3"
    And the verification report has the layout "document-tree"
    And the verification report counts every file of the tree

  Scenario: A v3 Specs single file is valid when model convert wrote it
    Given a valid IR v3 Specs model
    When I convert the model to a document tree
    And I convert the written model to a single file
    Then the conversion succeeds
    And the written single file says the format version "3.1.0"
    When I verify the written single file as JSON
    Then the exit code is 0
    And the verification report says that the model is valid
    And the verification report has the schema version "3"
    And the verification report has the layout "single-file"
