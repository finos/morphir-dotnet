@ModelConvert @CLI @PlatformSpecific
Feature: Model convert
  In-process checks of morphir model convert (add-model-convert 6.1): its events, its report and its refusals, with
  the command's store held or observed by the test. The command line comes from the shared model-conversion driver.
  The black-box scenarios of the command are in tests/Morphir.BlackBox.Tests/Integration/ModelConversion.feature.

  Scenario: Events are flushed before the next stage
    Given a v4 single-file model
    And the store holds each write until the test has read the stream
    When I start converting the model to a document tree
    Then the read and convert stages are on the stream before the write begins
    And the conversion succeeds

  Scenario: Every written file is reported
    Given a v4 single-file model with two modules
    When I convert the model to a document tree
    Then the conversion succeeds
    And the stream has one fileWritten record for each file the report lists
    And the report lists every file at the output
    And the report states the layouts "single-file" and "document-tree", the encoding "plain" and IR version "4"

  Scenario: Streamed diagnostics equal the report's
    Given a v4 document tree with a missing definition file
    When I convert the model to a single file
    Then the conversion fails with exit code 1 and the diagnostic "missing_member"
    And the streamed diagnostics equal the report's diagnostics
    And nothing is at the output

  Scenario: Streamed notices equal the report's
    Given a v4 document tree whose manifest has a version and a created date
    When I convert the model to a single file
    Then the conversion succeeds
    And the report has a notice "tree.dropped_manifest_member" that names "version"
    And the report has a notice "tree.dropped_manifest_member" that names "created"
    And the streamed notices equal the report's notices

  Scenario: A v3 Library returns its original label
    Given a v3 Library single-file model
    When I convert the model to a document tree
    And I convert the converted model back to a single file
    Then the conversion succeeds
    And the report states IR version "3"
    And the single file says format version 3

  Scenario Outline: An output that overlaps the input is refused before anything is read
    Given a <model>
    And the output is <place>
    When I convert the model to a <layout>
    Then the conversion fails with exit code 1 and the diagnostic "model.output_overlaps_input"
    And the store was asked neither to read nor to write
    And the input is unchanged

    Examples:
      | model                    | place                | layout        |
      | v4 single-file model     | the input itself     | single file   |
      | v4 single-file model     | the input's directory | document tree |
      | v4 document tree whose manifest has a version and a created date | the input itself     | document tree |
      | v4 document tree whose manifest has a version and a created date | inside the input     | single file   |

  Scenario Outline: An output that already holds content is refused before anything is read
    Given a v4 single-file model
    And the output is <place>
    When I convert the model to a <layout>
    Then the conversion fails with exit code 1 and the diagnostic "model.output_not_empty"
    And the store was asked neither to read nor to write
    And the existing output is unchanged

    Examples:
      | place                         | layout        |
      | an existing file              | document tree |
      | an existing file              | single file   |
      | a directory that holds a file | document tree |
      | a directory that holds a file | single file   |

  Scenario: An empty output directory receives the tree
    Given a v4 single-file model
    And the output is an empty directory
    When I convert the model to a document tree
    Then the conversion succeeds
    And the report lists every file at the output

  Scenario: Nothing to do
    Given a v4 single-file model
    When I convert the model without changing its layout
    Then the conversion fails with exit code 2 and the diagnostic "model.nothing_to_do"
    And the message names the input as "a plain single file"
    And nothing is at the output

  Scenario: A compacted input is refused
    Given a compacted single-file model
    When I convert the model to a document tree
    Then the conversion fails with exit code 1 and the diagnostic "model.unsupported_context"
    And the report states the input encoding "compacted"
    And nothing is at the output

  Scenario: A compacted output is not written yet
    Given a v4 single-file model
    When I convert the model to a document tree with compaction
    Then the conversion fails with exit code 1 and the diagnostic "tree.unsupported_option"
    And the report states the compaction mode "names"
    And nothing is at the output

  Scenario: A separated compaction mode is refused with a hint
    Given a v4 single-file model
    When I convert the model to a document tree with the compaction mode after a space
    Then the conversion fails with exit code 2 and the diagnostic "model.invalid_arguments"
    And the message suggests "--compact=names"
    And the store was asked neither to read nor to write

  Scenario: An unknown compaction mode lists the known mode
    Given a v4 single-file model
    When I convert the model to a document tree with the compaction mode "bogus"
    Then the conversion fails with exit code 2 and the diagnostic "model.invalid_arguments"
    And the message suggests "names"
    And the store was asked neither to read nor to write
