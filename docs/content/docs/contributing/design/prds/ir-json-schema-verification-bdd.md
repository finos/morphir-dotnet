---
title: "BDD Test Scenarios: IR JSON Schema Verification"
linkTitle: "BDD Scenarios"
weight: 11
description: "Comprehensive BDD test scenarios in Gherkin syntax for IR schema verification feature"
---

# BDD Test Scenarios: IR JSON Schema Verification

This document defines comprehensive BDD scenarios using Gherkin syntax for the IR JSON Schema Verification feature. These scenarios will be implemented as Reqnroll feature files in `tests/Morphir.Core.Tests/Features/`.

**Related**: [IR JSON Schema Verification PRD](./ir-json-schema-verification.md)

---

## Feature 1: IR Schema Verification

**Feature File**: `IrSchemaVerification.feature`

```gherkin
Feature: IR Schema Verification
  As a Morphir developer
  I want to validate IR JSON files against schemas
  So that I can catch structural errors early

  Background:
    Given the Morphir CLI is installed
    And the schema files v1, v2, and v3 are available

  Rule: Valid IR files pass validation

    Scenario: Validate a valid v3 IR file
      Given a valid Morphir IR v3 JSON file "valid-v3.json"
      When I run "morphir ir verify valid-v3.json"
      Then the exit code should be 0
      And the output should contain "✓ Validation successful"
      And the output should contain "Schema: v3 (auto-detected)"
      And the output should contain "File: valid-v3.json"

    Scenario: Validate a valid v2 IR file
      Given a valid Morphir IR v2 JSON file "valid-v2.json"
      When I run "morphir ir verify valid-v2.json"
      Then the exit code should be 0
      And the output should contain "✓ Validation successful"
      And the output should contain "Schema: v2 (auto-detected)"

    Scenario: Validate a valid v1 IR file
      Given a valid Morphir IR v1 JSON file "valid-v1.json"
      When I run "morphir ir verify valid-v1.json"
      Then the exit code should be 0
      And the output should contain "✓ Validation successful"
      And the output should contain "Schema: v1 (auto-detected)"

    Scenario Outline: Validate various valid IR files across versions
      Given a valid Morphir IR <version> JSON file "<filename>"
      When I run "morphir ir verify <filename>"
      Then the exit code should be 0
      And the output should contain "✓ Validation successful"
      And the output should contain "Schema: <version> (auto-detected)"

      Examples:
        | version | filename              |
        | v1      | library-v1.json       |
        | v1      | complex-types-v1.json |
        | v2      | library-v2.json       |
        | v2      | complex-types-v2.json |
        | v3      | library-v3.json       |
        | v3      | complex-types-v3.json |

  Rule: Invalid IR files fail validation with clear errors

    Scenario: Validate an IR file with incorrect tag capitalization
      Given an invalid Morphir IR v3 JSON file "invalid-tags.json" with lowercase tags
      When I run "morphir ir verify invalid-tags.json"
      Then the exit code should be 1
      And the output should contain "✗ Validation failed"
      And the output should contain "Invalid type tag"
      And the output should contain "Expected: \"Public\" or \"Private\""
      And the output should contain "Found: \"public\""

    Scenario: Validate an IR file with missing required fields
      Given an invalid Morphir IR v3 JSON file "missing-fields.json" missing the "name" field
      When I run "morphir ir verify missing-fields.json"
      Then the exit code should be 1
      And the output should contain "✗ Validation failed"
      And the output should contain "Missing required field"
      And the output should contain "Path: $.package.modules"
      And the output should contain "Required property 'name' is missing"

    Scenario: Validate an IR file with invalid type structure
      Given an invalid Morphir IR v3 JSON file "invalid-structure.json" with malformed type definitions
      When I run "morphir ir verify invalid-structure.json"
      Then the exit code should be 1
      And the output should contain "✗ Validation failed"
      And the error count should be greater than 0

    Scenario: Validate an IR file with multiple errors
      Given an invalid Morphir IR v3 JSON file "multiple-errors.json" with 5 validation errors
      When I run "morphir ir verify multiple-errors.json"
      Then the exit code should be 1
      And the output should contain "5 errors found"
      And the output should list all 5 errors with JSON paths

  Rule: Schema version can be manually specified

    Scenario: Force validation against specific schema version
      Given a Morphir IR JSON file "mixed-version.json"
      When I run "morphir ir verify --schema-version 2 mixed-version.json"
      Then the validation should use schema v2
      And the output should contain "Schema: v2 (manual)"

    Scenario: Override auto-detection with explicit version
      Given a valid Morphir IR v3 JSON file "valid-v3.json"
      When I run "morphir ir verify --schema-version 3 valid-v3.json"
      Then the exit code should be 0
      And the output should contain "Schema: v3 (manual)"

    Scenario: Validate v2 file against v3 schema (should fail)
      Given a valid Morphir IR v2 JSON file "valid-v2.json"
      When I run "morphir ir verify --schema-version 3 valid-v2.json"
      Then the exit code should be 1
      And the output should contain "✗ Validation failed against schema v3"

    Scenario Outline: Validate with explicit version specification
      Given a valid Morphir IR <actual-version> JSON file "<filename>"
      When I run "morphir ir verify --schema-version <specified-version> <filename>"
      Then the exit code should be <exit-code>
      And the output should contain "Schema: <specified-version> (manual)"

      Examples:
        | filename        | actual-version | specified-version | exit-code |
        | valid-v1.json   | v1             | 1                 | 0         |
        | valid-v2.json   | v2             | 2                 | 0         |
        | valid-v3.json   | v3             | 3                 | 0         |
        | valid-v1.json   | v1             | 3                 | 1         |
        | valid-v2.json   | v2             | 1                 | 1         |

  Rule: Multiple output formats are supported

    Scenario: Output validation results as JSON
      Given an invalid Morphir IR JSON file "errors.json"
      When I run "morphir ir verify --json errors.json"
      Then the output should be valid JSON
      And the JSON should have field "valid" with value false
      And the JSON should have field "errors" as an array
      And each error should include "path", "message", "expected", and "found"

    Scenario: Output successful validation as JSON
      Given a valid Morphir IR v3 JSON file "valid-v3.json"
      When I run "morphir ir verify --json valid-v3.json"
      Then the output should be valid JSON
      And the JSON should have field "valid" with value true
      And the JSON should have field "schemaVersion" with value "3"
      And the JSON should have field "detectionMethod" with value "auto"
      And the JSON should have field "errorCount" with value 0

    Scenario: Quiet mode suppresses success messages
      Given a valid Morphir IR v3 JSON file "valid-v3.json"
      When I run "morphir ir verify --quiet valid-v3.json"
      Then the exit code should be 0
      And the output should be empty

    Scenario: Quiet mode shows only errors
      Given an invalid Morphir IR v3 JSON file "invalid-tags.json"
      When I run "morphir ir verify --quiet invalid-tags.json"
      Then the exit code should be 1
      And the output should contain error messages
      And the output should not contain "✗ Validation failed"
      And the output should not contain headers or decorations

    Scenario: Verbose mode shows detailed information
      Given a valid Morphir IR v3 JSON file "valid-v3.json"
      When I run "morphir ir verify --verbose valid-v3.json"
      Then the exit code should be 0
      And the output should contain "✓ Validation successful"
      And the output should contain "Schema: v3 (auto-detected)"
      And the output should contain "File: valid-v3.json"
      And the output should contain validation timestamp
      And the output should contain schema file path

  Rule: Error messages are clear and actionable

    Scenario: Error message includes JSON path
      Given an invalid Morphir IR v3 JSON file "bad-path.json" with error at "$.modules[0].types.MyType"
      When I run "morphir ir verify bad-path.json"
      Then the exit code should be 1
      And the output should contain "Path: $.modules[0].types.MyType"

    Scenario: Error message includes line and column numbers
      Given an invalid Morphir IR v3 JSON file "line-col-error.json" with error at line 42, column 12
      When I run "morphir ir verify line-col-error.json"
      Then the exit code should be 1
      And the output should contain "Line: 42, Column: 12"

    Scenario: Error message suggests fixes
      Given an invalid Morphir IR v3 JSON file "lowercase-tag.json" with lowercase "public" tag
      When I run "morphir ir verify lowercase-tag.json"
      Then the exit code should be 1
      And the output should contain 'Suggestion: Change "public" to "Public"'

  Rule: Edge cases and error handling

    Scenario: File not found
      When I run "morphir ir verify non-existent-file.json"
      Then the exit code should be 2
      And the output should contain "File not found: non-existent-file.json"

    Scenario: Malformed JSON
      Given a file "malformed.json" with invalid JSON syntax
      When I run "morphir ir verify malformed.json"
      Then the exit code should be 2
      And the output should contain "Invalid JSON"
      And the output should contain the JSON parsing error location

    Scenario: Empty file
      Given an empty file "empty.json"
      When I run "morphir ir verify empty.json"
      Then the exit code should be 2
      And the output should contain "File is empty"

    Scenario: Very large file
      Given a valid Morphir IR v3 JSON file "large-10mb.json" of size 10MB
      When I run "morphir ir verify large-10mb.json"
      Then the validation should complete within 2 seconds
      And the exit code should be 0

    Scenario: Invalid schema version specified
      Given a valid Morphir IR v3 JSON file "valid-v3.json"
      When I run "morphir ir verify --schema-version 5 valid-v3.json"
      Then the exit code should be 2
      And the output should contain "Schema version must be 1, 2, or 3"

    Scenario: File with invalid UTF-8 encoding
      Given a file "invalid-utf8.json" with invalid UTF-8 bytes
      When I run "morphir ir verify invalid-utf8.json"
      Then the exit code should be 2
      And the output should contain "Invalid file encoding"
```

---

## Feature 2: Version Detection

**Feature File**: `IrVersionDetection.feature`

```gherkin
Feature: IR Version Detection
  As a Morphir developer
  I want to automatically detect which schema version my IR uses
  So that I can validate against the correct schema

  Background:
    Given the Morphir CLI is installed
    And the schema files v1, v2, and v3 are available

  Rule: Auto-detection works for files with formatVersion field

    Scenario: Detect version from formatVersion field (v3)
      Given a Morphir IR JSON file "with-format-v3.json" containing "formatVersion": 3
      When I run "morphir ir verify with-format-v3.json"
      Then the validation should use schema v3
      And the output should contain "Schema: v3 (auto-detected)"

    Scenario Outline: Detect version from formatVersion field
      Given a Morphir IR JSON file "<filename>" containing "formatVersion": <version>
      When I run "morphir ir verify <filename>"
      Then the validation should use schema v<version>
      And the output should contain "Schema: v<version> (auto-detected)"

      Examples:
        | filename        | version |
        | format-v1.json  | 1       |
        | format-v2.json  | 2       |
        | format-v3.json  | 3       |

  Rule: Auto-detection uses tag capitalization when formatVersion is absent

    Scenario: Detect v1 from lowercase tags
      Given a Morphir IR JSON file "no-format-v1.json" without formatVersion
      And the file uses all lowercase tags like "library", "public", "apply"
      When I run "morphir ir verify no-format-v1.json"
      Then the validation should use schema v1
      And the output should contain "Schema: v1 (auto-detected)"

    Scenario: Detect v3 from capitalized tags
      Given a Morphir IR JSON file "no-format-v3.json" without formatVersion
      And the file uses all capitalized tags like "Library", "Public", "Apply"
      When I run "morphir ir verify no-format-v3.json"
      Then the validation should use schema v3
      And the output should contain "Schema: v3 (auto-detected)"

    Scenario: Detect v2 from mixed capitalization
      Given a Morphir IR JSON file "no-format-v2.json" without formatVersion
      And the file uses mixed case tags
      When I run "morphir ir verify no-format-v2.json"
      Then the validation should use schema v2
      And the output should contain "Schema: v2 (auto-detected)"

  Rule: Standalone version detection command

    Scenario: Detect version with dedicated command
      Given a Morphir IR JSON file "detect-me.json" with v3 structure
      When I run "morphir ir detect-version detect-me.json"
      Then the exit code should be 0
      And the output should contain "Detected schema version: v3"
      And the output should contain "Confidence: High"
      And the output should contain "Rationale:"

    Scenario: Version detection shows rationale
      Given a Morphir IR JSON file "v3-with-format.json" containing "formatVersion": 3
      When I run "morphir ir detect-version v3-with-format.json"
      Then the output should contain "Contains formatVersion: 3"

    Scenario: Version detection analyzes tag patterns
      Given a Morphir IR JSON file "v3-no-format.json" without formatVersion but with capitalized tags
      When I run "morphir ir detect-version v3-no-format.json"
      Then the output should contain 'All tags are capitalized ("Library", "Public", "Apply")'

    Scenario Outline: Detect version with varying confidence levels
      Given a Morphir IR JSON file "<filename>" with <indicators>
      When I run "morphir ir detect-version <filename>"
      Then the output should contain "Confidence: <confidence>"

      Examples:
        | filename            | indicators                    | confidence |
        | clear-v3.json       | formatVersion and cap tags    | High       |
        | likely-v1.json      | lowercase tags only           | Medium     |
        | ambiguous.json      | minimal structure             | Low        |
```

---

## Feature 3: Multiple File Support (Phase 2)

**Feature File**: `IrMultiFileVerification.feature`

```gherkin
Feature: Multiple File Verification
  As a Morphir developer working with multiple IR files
  I want to validate several files at once
  So that I can efficiently verify my entire project

  Background:
    Given the Morphir CLI is installed with Phase 2 features

  Rule: Multiple files can be validated in one command

    Scenario: Validate two valid files
      Given valid IR files "file1.json" and "file2.json"
      When I run "morphir ir verify file1.json file2.json"
      Then the exit code should be 0
      And the output should show results for "file1.json"
      And the output should show results for "file2.json"
      And both files should pass validation

    Scenario: Validate mix of valid and invalid files
      Given a valid IR file "valid.json"
      And an invalid IR file "invalid.json"
      When I run "morphir ir verify valid.json invalid.json"
      Then the exit code should be 1
      And the output should show "valid.json" passed
      And the output should show "invalid.json" failed with errors

    Scenario: Validate multiple files with summary
      Given 10 valid IR files
      And 3 invalid IR files
      When I run "morphir ir verify *.json"
      Then the exit code should be 1
      And the output should contain "Summary: 10 passed, 3 failed"

  Rule: Stdin support for piped input

    Scenario: Validate IR from stdin
      Given a valid Morphir IR v3 JSON file "valid-v3.json"
      When I run "cat valid-v3.json | morphir ir verify -"
      Then the exit code should be 0
      And the output should contain "✓ Validation successful"
      And the output should contain "Source: stdin"

    Scenario: Validate invalid IR from stdin
      Given an invalid Morphir IR JSON file "invalid.json"
      When I run "cat invalid.json | morphir ir verify -"
      Then the exit code should be 1
      And the output should contain "✗ Validation failed"

    Scenario: Combine file and stdin (stdin represented as -)
      Given a valid IR file "file.json"
      And valid IR JSON content in stdin
      When I run "cat stdin.json | morphir ir verify file.json -"
      Then the exit code should be 0
      And the output should show results for "file.json"
      And the output should show results for "stdin"

  Rule: Batch processing is efficient

    Scenario: Validate 100 files efficiently
      Given 100 valid IR files in "batch/" directory
      When I run "morphir ir verify batch/*.json"
      Then the validation should complete within 10 seconds
      And the exit code should be 0
      And the output should contain "Summary: 100 passed, 0 failed"

    Scenario: Stop on first error (--fail-fast option)
      Given 5 valid IR files and 1 invalid IR file
      When I run "morphir ir verify --fail-fast *.json"
      Then the validation should stop at the first error
      And the exit code should be 1
      And not all files should be processed
```

---

## Feature 4: Directory Validation (Phase 3)

**Feature File**: `IrDirectoryVerification.feature`

```gherkin
Feature: Directory Verification
  As a Morphir developer with many IR files
  I want to validate entire directories
  So that I can ensure all my IR files are correct

  Background:
    Given the Morphir CLI is installed with Phase 3 features

  Rule: Directories can be validated recursively

    Scenario: Validate all JSON files in directory
      Given a directory "ir-files/" with 5 valid IR JSON files
      When I run "morphir ir verify --recursive ir-files/"
      Then the exit code should be 0
      And all 5 files should be validated
      And the output should contain "5 files validated, 5 passed"

    Scenario: Validate directory with mixed results
      Given a directory "mixed/" with 3 valid and 2 invalid IR files
      When I run "morphir ir verify --recursive mixed/"
      Then the exit code should be 1
      And the output should contain "5 files validated, 3 passed, 2 failed"

    Scenario: Skip non-JSON files in directory
      Given a directory "mixed-types/" with JSON and non-JSON files
      When I run "morphir ir verify --recursive mixed-types/"
      Then only JSON files should be validated
      And the output should list which files were skipped

    Scenario: Validate nested directory structure
      Given a nested directory structure:
        """
        project/
        ├── src/
        │   ├── module1/
        │   │   └── ir.json
        │   └── module2/
        │       └── ir.json
        └── tests/
            └── fixtures/
                └── ir.json
        """
      When I run "morphir ir verify --recursive project/"
      Then all 3 IR files should be validated
      And the output should show the relative paths of all files

  Rule: Directory validation supports filtering

    Scenario: Validate only specific file patterns
      Given a directory with various JSON files
      When I run "morphir ir verify --recursive --pattern 'morphir-*.json' dir/"
      Then only files matching "morphir-*.json" should be validated

    Scenario: Exclude specific directories
      Given a directory structure with "node_modules/" and "src/"
      When I run "morphir ir verify --recursive --exclude 'node_modules' ."
      Then files in "node_modules/" should be skipped
      And files in "src/" should be validated
```

---

## Feature 5: Error Reporting Quality

**Feature File**: `IrValidationErrorReporting.feature`

```gherkin
Feature: Validation Error Reporting
  As a Morphir developer fixing validation errors
  I want detailed, actionable error messages
  So that I can quickly identify and fix issues

  Background:
    Given the Morphir CLI is installed

  Rule: Errors include precise location information

    Scenario: Error with JSON path
      Given an IR file "error.json" with invalid value at "$.modules[0].types.MyType.accessControlled[0]"
      When I run "morphir ir verify error.json"
      Then the output should contain the exact JSON path
      And the path should be formatted as "$.modules[0].types.MyType.accessControlled[0]"

    Scenario: Error with line and column numbers
      Given an IR file "error.json" with syntax error at line 42, column 12
      When I run "morphir ir verify error.json"
      Then the output should contain "Line: 42, Column: 12"

    Scenario: Error shows context snippet
      Given an IR file with error at line 42
      When I run "morphir ir verify --verbose error.json"
      Then the output should include a code snippet around line 42
      And the error line should be highlighted

  Rule: Errors explain what was expected vs found

    Scenario: Type mismatch error
      Given an IR file with string where number is expected
      When I run "morphir ir verify error.json"
      Then the output should contain "Expected: number"
      And the output should contain 'Found: "some string"'

    Scenario: Enum value error
      Given an IR file with invalid access control tag
      When I run "morphir ir verify error.json"
      Then the output should contain 'Expected: One of ["Public", "Private"]'
      And the output should contain 'Found: "public"'

    Scenario: Array length constraint error
      Given an IR file with array that violates length constraints
      When I run "morphir ir verify error.json"
      Then the output should contain "Expected: Array with 2 elements"
      And the output should contain "Found: Array with 3 elements"

  Rule: Errors provide helpful suggestions

    Scenario: Suggest capitalization fix
      Given an IR file with lowercase tag in v3 IR
      When I run "morphir ir verify error.json"
      Then the output should contain 'Suggestion: Change "public" to "Public"'

    Scenario: Suggest adding missing field
      Given an IR file missing required "name" field
      When I run "morphir ir verify error.json"
      Then the output should contain 'Suggestion: Add required field "name"'

    Scenario: Suggest similar field names for typos
      Given an IR file with "nmae" instead of "name"
      When I run "morphir ir verify error.json"
      Then the output should contain 'Did you mean "name"?'

  Rule: Multiple errors are clearly enumerated

    Scenario: List multiple errors with numbering
      Given an IR file with 3 validation errors
      When I run "morphir ir verify error.json"
      Then the output should contain "Error 1:"
      And the output should contain "Error 2:"
      And the output should contain "Error 3:"
      And the output should contain "3 errors found"

    Scenario: Group errors by category
      Given an IR file with type errors and missing field errors
      When I run "morphir ir verify error.json"
      Then errors should be grouped by type
      And the output should show "Type Errors (2)" and "Missing Fields (3)"

    Scenario: Limit error display with --max-errors option
      Given an IR file with 50 validation errors
      When I run "morphir ir verify --max-errors 10 error.json"
      Then only the first 10 errors should be displayed
      And the output should contain "... and 40 more errors"

  Rule: Error output is machine-readable in JSON mode

    Scenario: JSON error format includes all details
      Given an IR file with validation errors
      When I run "morphir ir verify --json error.json"
      Then the JSON output should include:
        | field           | description                    |
        | valid           | false                          |
        | errors          | Array of error objects         |
        | errors[].path   | JSON path to error             |
        | errors[].line   | Line number                    |
        | errors[].column | Column number                  |
        | errors[].message| Human-readable error message   |
        | errors[].code   | Machine-readable error code    |

    Scenario: Error codes are consistent and documented
      Given an IR file with a missing required field
      When I run "morphir ir verify --json error.json"
      Then the error should have code "MISSING_REQUIRED_FIELD"
      And the error code should be documented
```

---

## Feature 6: Performance and Scalability

**Feature File**: `IrValidationPerformance.feature`

```gherkin
Feature: Validation Performance
  As a developer integrating validation in CI/CD
  I want fast validation even for large files
  So that builds remain efficient

  Background:
    Given the Morphir CLI is installed

  Rule: Validation meets performance targets

    Scenario Outline: Validate files of varying sizes
      Given a valid Morphir IR v3 JSON file of size <size>
      When I run "morphir ir verify <filename>"
      Then the validation should complete within <max-time>
      And the exit code should be 0

      Examples:
        | size   | filename        | max-time |
        | 10KB   | small.json      | 100ms    |
        | 100KB  | medium.json     | 100ms    |
        | 1MB    | large.json      | 500ms    |
        | 10MB   | very-large.json | 2000ms   |

    Scenario: Schema caching improves performance
      Given 10 valid IR files
      When I run "morphir ir verify file1.json ... file10.json"
      Then schemas should only be loaded once
      And subsequent validations should be faster

    Scenario: Memory usage remains bounded
      Given a 50MB IR file
      When I run "morphir ir verify huge.json"
      Then memory usage should not exceed 500MB
      And validation should complete successfully

  Rule: Validation supports progress reporting

    Scenario: Show progress for multiple files
      Given 100 IR files to validate
      When I run "morphir ir verify --progress *.json"
      Then the output should show a progress indicator
      And the progress should update as files are validated

    Scenario: Show progress for large single file
      Given a 10MB IR file
      When I run "morphir ir verify --progress large.json"
      Then the output should show validation progress
```

---

## Implementation Notes

### Step Definition Organization

Step definitions should be organized in the following files within `tests/Morphir.Core.Tests/StepDefinitions/`:

- **`IrVerificationSteps.cs`**: Common steps for file setup, CLI execution, output assertions
- **`IrSchemaSteps.cs`**: Steps specific to schema validation
- **`IrVersionDetectionSteps.cs`**: Steps for version detection scenarios
- **`IrFileManagementSteps.cs`**: Steps for file and directory operations

### Test Data Strategy

Test IR JSON files should be stored in `tests/Morphir.Core.Tests/TestData/IrFiles/`:

```
TestData/
└── IrFiles/
    ├── v1/
    │   ├── valid/
    │   │   ├── library-v1.json
    │   │   └── complex-types-v1.json
    │   └── invalid/
    │       ├── invalid-tags-v1.json
    │       └── missing-fields-v1.json
    ├── v2/
    │   ├── valid/
    │   └── invalid/
    └── v3/
        ├── valid/
        └── invalid/
```

### Continuous Integration

These BDD scenarios should run:
- On every pull request
- On merge to main branch
- As part of the release validation process

Target: All scenarios pass with >95% reliability.

---

**Last Updated**: 2025-12-13
