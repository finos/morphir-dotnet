@integration @needs-generation @needs-built-extensions
Feature: Select an external generation backend
  generate --extension and [codegen.<target>].extension select a backend that runs as a MEP guest process, as
  compile --extension and [frontend.<language>].extension select a frontend. Without a selection the built-in
  backend of the target runs, and the report is unchanged. GenerateExtension.feature of Morphir.Tooling.Tests also
  compares the files of the guest with the built-in backend in the test process.

  Scenario: An explicit extension generates F#, and the report names the extension
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "fsharp" from the model with the built F# backend extension
    Then generation exits with 0 and one JSON report
    And the generation report names the external F# backend over MEP
    And each artifact of the generation report is a file of the output

  Scenario: The report of an external backend has no formatting member, even when the host asked for format false
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "fsharp" from the model with the built F# backend extension and the option "format=false"
    Then generation exits with 0 and one JSON report
    And the generation report names the external F# backend over MEP
    And the generation report has no member "formatting"

  Scenario: The configuration key selects the extension, and --extension overrides it
    Given the model fixture "WireForms/daily-rate-4.json"
    And a project whose configuration selects the built F# backend extension for "fsharp"
    When I generate "fsharp" from the model for the project
    Then generation exits with 0 and one JSON report
    And the generation report names the external F# backend over MEP
    When I generate "fsharp" from the model for the project with the extension "no-such-backend"
    Then generation exits with 2 and one JSON report
    And the generation report has the diagnostic "extension.unavailable" that names "no-such-backend"

  Scenario: Without a selection the built-in backend runs, and the report keeps its members
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "fsharp" from the model
    Then generation exits with 0 and one JSON report
    And the generation report has the backend "morphir.fsharp"
    And the generation report has these members in this order
      | member            |
      | success           |
      | exitCode          |
      | target            |
      | backend           |
      | input             |
      | output            |
      | sourceVersion     |
      | family            |
      | distributionKind  |
      | representation    |
      | artifacts         |
      | written           |
      | unchanged         |
      | removed           |
      | diagnostics       |
      | failureKind       |
      | formatting        |

  Scenario: With format false the guest writes the frozen bytes of the toolchain case
    Given the model fixture "Toolchain/fsharp-compile-generate/ir-4.0.0.json"
    When I generate "fsharp" from the model with the built F# backend extension and the option "format=false"
    Then generation exits with 0 and one JSON report
    And the generation report names the external F# backend over MEP
    And the generation report lists only the artifact "Pricing.fs"
    And the output file "Pricing.fs" has the bytes of the fixture "Toolchain/fsharp-compile-generate/Pricing.fs"
