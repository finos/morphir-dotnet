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

  Scenario: The layout is also accepted after a space
    Given a v4 single-file model
    When I convert the model to a document tree with the layout after a space
    Then the conversion succeeds
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
    And the store was asked neither to read nor to write
    And nothing is at the output

  Scenario: Nothing to do for a plain input that does not decode
    Given a single-file model that is not JSON
    When I convert the model without changing its layout
    Then the conversion fails with exit code 2 and the diagnostic "model.nothing_to_do"
    And the store was asked neither to read nor to write
    And nothing is at the output

  Scenario: Nothing to do for a plain tree
    Given a v4 document tree whose manifest has a version and a created date
    When I convert the model without changing its layout
    Then the conversion fails with exit code 2 and the diagnostic "model.nothing_to_do"
    And the message names the input as "a plain document tree"
    And the store was asked neither to read nor to write
    And nothing is at the output

  Scenario: A compacted input with its layout unchanged is expanded, not nothing to do
    Given a compacted v4 single-file model
    When I convert the model without changing its layout
    Then the conversion succeeds
    And the report states the input encoding "compacted" and the output encoding "plain"
    And the single file is plain
    And the single file says format version 4

  Scenario: A compacted v3 single file is expanded into a plain tree
    Given a compacted v3 single-file model
    When I convert the model to a document tree
    Then the conversion succeeds
    And the report states the input encoding "compacted" and the output encoding "plain"
    And the report states IR version "3"
    And every file at the output is plain

  Scenario: A tree with one compacted file other than the manifest is not nothing to do
    Given a v4 document tree with only its value file compacted
    When I convert the model without changing its layout
    Then the conversion succeeds
    And the report states the input encoding "compacted" and the output encoding "plain"
    And every file at the output is plain

  Scenario: A compacted tree is expanded into a plain single file
    Given a compacted v4 document tree
    When I convert the model to a single file
    Then the conversion succeeds
    And the report states the input encoding "compacted" and the output encoding "plain"
    And the single file is plain

  Scenario: A refused tree with a compacted file is stated as compacted
    Given a v4 document tree with only its value file compacted and no type file
    When I convert the model to a single file
    Then the conversion fails with exit code 1 and the diagnostic "missing_member"
    And the report states the input encoding "compacted"
    And nothing is at the output

  Scenario: A refused context names its file
    Given a v4 document tree whose type file references an alias that no table defines
    When I convert the model to a single file
    Then the conversion fails with exit code 1 and the diagnostic "context.unknown_alias"
    And the diagnostic "context.unknown_alias" is at the type file and names "zz"
    And the report states the input encoding "compacted"
    And nothing is at the output

  Scenario Outline: A compacted single file near the depth limit is read and refused by the writer with a report
    Given a compacted v3 single-file model nested "near" the depth limit
    When I convert the model to a <layout>
    Then the conversion fails with exit code 1 and the diagnostic "depth_limit_exceeded"
    And the diagnostic "depth_limit_exceeded" is at the output
    And the report states the input encoding "compacted"
    And the report does not have the diagnostic "invalid_json"
    And the stream ends with one completed record
    And nothing is at the output

    Examples:
      | layout        |
      | single file   |
      | document tree |

  Scenario: A file deeper than a tree reads is converted to a single file
    Given a compacted v3 single-file model nested deeper than a tree reads
    When I convert the model to a single file
    Then the conversion succeeds
    And the single file is plain
    And the stream ends with one completed record

  Scenario: A tree is not written with a file deeper than a tree reads
    Given a compacted v3 single-file model nested deeper than a tree reads
    When I convert the model to a document tree
    Then the conversion fails with exit code 1 and the diagnostic "depth_limit_exceeded"
    And the diagnostic "depth_limit_exceeded" is at the output
    And the stream ends with one completed record
    And nothing is at the output

  Scenario: A plain single file just past the depth limit is too deep
    Given a plain v3 single-file model nested just past the depth limit
    When I convert the model to a document tree
    Then the conversion fails with exit code 1 and the diagnostic "depth_limit_exceeded"
    And the report does not have the diagnostic "invalid_json"
    And the stream ends with one completed record
    And nothing is at the output

  Scenario: A compacted single file just past the depth limit is too deep
    Given a compacted v3 single-file model nested "just past" the depth limit
    When I convert the model to a document tree
    Then the conversion fails with exit code 1 and the diagnostic "depth_limit_exceeded"
    And the report states the input encoding "compacted"
    And the stream ends with one completed record
    And nothing is at the output

  Scenario: A single file in an undeclared envelope is refused without calling it compacted
    Given a single-file model in an envelope whose schema this version does not declare
    When I convert the model to a document tree
    Then the conversion fails with exit code 1 and the diagnostic "context.undeclared_schema"
    And the report states no input encoding
    And nothing is at the output

  Scenario: A tree file in an undeclared envelope is refused without calling the tree compacted
    Given a v4 document tree with a type file in an envelope whose schema this version does not declare
    When I convert the model to a single file
    Then the conversion fails with exit code 1 and the diagnostic "context.undeclared_schema"
    And the diagnostic "context.undeclared_schema" is at the type file and names "https://morphir.finos.org/schemas/ir-context/0.2.0/document.schema.json"
    And the report states no input encoding
    And nothing is at the output

  Scenario Outline: An output below a file is refused when it is written
    Given a <model>
    And the output's parent is an existing file
    When I convert the model to a <layout>
    Then the conversion fails with exit code 1 and the diagnostic "model.write_failed"
    And the stream ends with one completed record
    And the file in the way is unchanged

    Examples:
      | model                                                            | layout        |
      | v4 document tree whose manifest has a version and a created date | single file   |
      | v4 single-file model                                             | document tree |

  Scenario: A file that appears at the output during the write is kept
    Given a v4 document tree whose manifest has a version and a created date
    When I convert the model to a single file while a file appears at the output
    Then the conversion fails with exit code 1 and the diagnostic "model.write_failed"
    And the stream ends with one completed record
    And the file in the way is unchanged
    And no staging file or directory is left beside the output

  Scenario: A file that appears in the empty output directory during the write is kept
    Given a v4 single-file model
    And the output is an empty directory
    When I convert the model to a document tree while a file appears at the output
    Then the conversion fails with exit code 1 and the diagnostic "model.write_failed"
    And the stream ends with one completed record
    And the file in the way is unchanged
    And no staging file or directory is left beside the output

  Scenario: A read that fails with a disk error ends with a report
    Given a v4 single-file model
    And reading the model fails with a disk error
    When I convert the model to a document tree
    Then the conversion fails with exit code 1 and the diagnostic "model.read_failed"
    And the read stage is completed as not passed
    And the stream ends with one completed record
    And nothing is at the output

  Scenario: An empty input path is a usage error
    When I convert a model whose input path is empty
    Then the conversion fails with exit code 2 and the diagnostic "model.invalid_arguments"
    And the stream ends with one completed record
    And the store was asked neither to read nor to write

  Scenario Outline: A single file is compacted and stays one file
    Given the plain v<ir> single file of the compaction policy's cases
    When I convert the model with compaction
    Then the conversion succeeds
    And the report states the input encoding "plain" and the output encoding "compacted"
    And the report states the compaction mode "names" and the writer policy
    And the single file is compacted
    When I convert the converted model back to a single file
    Then the conversion succeeds
    And the single file is plain
    And the single file says format version <ir>

    Examples:
      | ir |
      | 4  |
      | 3  |

  Scenario: A compacted output that no alias pays for is written plain, with a notice
    Given a v4 single-file model
    When I convert the model to a document tree with compaction
    Then the conversion succeeds
    And the report states the input encoding "plain" and the output encoding "plain"
    And the report states the compaction mode "names" and the writer policy
    And the report has the notice "model.compaction_declined"
    And the streamed notices equal the report's notices
    And every file at the output is plain

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
