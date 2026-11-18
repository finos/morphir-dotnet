@journey @needs-model-conversion @needs-model-verification
Feature: Store a v3 Specs model as a document tree and back, and verify it
  As a developer who publishes a v3 Specs model
  I want to store it as a document tree and as one file with model convert
  So that the file I publish is still a valid model that model verify accepts

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
