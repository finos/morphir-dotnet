@integration @needs-generation
Feature: Generate F# libraries from Morphir models
  Generation uses explicit settings and the same behavior for each representation. Generate.feature of
  Morphir.Tooling.Tests also runs the generated F# against the common corpus of the product, in the test process.

  Scenario Outline: A nonempty model generates F# with one JSON report on standard output
    Given the <kind> fixture "<fixture>"
    When I generate "fsharp" from the model
    Then generation exits with 0 and one JSON report
    And each artifact of the generation report is a file of the output

    Examples:
      | kind       | fixture                         |
      | model      | Models/daily-rate-v3.json       |
      | model      | WireForms/daily-rate-4.json     |
      | model tree | WireForms/daily-rate-3.0.0.tree |
      | model tree | WireForms/daily-rate-4.0.0.tree |

  Scenario Outline: A missing setting is an invocation failure
    Given the model fixture "Models/daily-rate-v3.json"
    When I generate "fsharp" from the model without the setting "<setting>"
    Then generation exits with 2 and one JSON report
    And the output directory is not there

    Examples:
      | setting |
      | input   |
      | target  |
      | output  |

  Scenario Outline: A refused input keeps the output of an earlier run
    Given the model fixture "<fixture>"
    And the output has the file "previous.fs" with the text "previous content"
    And the model has the problem "<problem>"
    When I generate "fsharp" from the model
    Then generation exits with 1 and one JSON report
    And the output file "previous.fs" has the text "previous content"

    Examples:
      | fixture                     | problem          |
      | Models/daily-rate-v3.json   | malformed        |
      | WireForms/daily-rate-4.json | malformed        |
      | WireForms/daily-rate-4.json | document-literal |

  Scenario: Equivalent physical representations keep the source and the declared order
    Given the model fixture "Models/daily-rate-v3.json"
    When I generate "fsharp" from the model
    Then generation exits with 0 and one JSON report
    Given I remember the artifacts of the generation report
    And the model tree fixture "WireForms/daily-rate-3.0.0.tree"
    When I generate "fsharp" from the model
    Then generation exits with 0 and one JSON report
    And the generation report has the remembered artifacts with the same text in the same order

  Scenario Outline: An invocation that is not valid exits with 2 and publishes nothing
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "fsharp" from the model with the arguments "<arguments>"
    Then generation exits with 2 and one JSON report
    And the generation report has the backend "morphir.fsharp"
    And the output directory is not there

    Examples:
      | arguments                        |
      | --option unknown=true            |
      | --option optimize=not-a-boolean  |

  Scenario Outline: An invocation option that is not valid exits with 2 and publishes nothing
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "fsharp" from the model with the arguments "<arguments>"
    Then generation exits with 2 and one JSON report
    And the output directory is not there

    Examples:
      | arguments           |
      | --input-format yaml |
      | --unavailable       |
      | --option            |

  Scenario: An unknown target exits with 2 and publishes nothing
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "missing" from the model
    Then generation exits with 2 and one JSON report
    And the first diagnostic of the generation report has the code "unknown-target"
    And the output directory is not there

  Scenario: Text output keeps standard output for the artifacts
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "fsharp" from the model as text
    Then the exit code is 0
    And standard output is the text "Inventory.fs"

  Scenario: Text output of an unknown target writes nothing to standard output
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "unknown" from the model as text
    Then the exit code is 2
    And standard output is empty
    And standard error contains "unknown-target"
    And the output directory is not there

  Scenario: A repeated option keeps an explicit false and the last value
    Given the model fixture "WireForms/daily-rate-4.0.0.binary.unusual"
    When I generate "fsharp" from the model with the input format "ion" and the options
      | option         |
      | optimize=true  |
      | optimize=false |
    Then generation exits with 0 and one JSON report
    And the generation report has artifacts

  Scenario: An input that is not there is an operational failure of the F# backend
    Given no model file "missing-model.json"
    When I generate "fsharp" from the model
    Then the exit code is 2
    And the generation report says that the generation failed with the kind "operational"
    And the output directory is not there
