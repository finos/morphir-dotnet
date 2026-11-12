@integration @needs-compilation
Feature: The commands of the compile page give their documented output
  Each command of docs/content/docs/cli/compile.md runs against the sources of the MCK corpus cases
  frontend-fsharp-0116 (src/) and frontend-fsharp-0001 (broken/), and gives its documented output. The members output,
  sources.root and generation.output show example paths, so they are not compared. The sources are the text of the two
  cases of conformance/suites/frontend-fsharp: the black-box project does not see the library of the kit.

  Background:
    Given the documentation page "cli/compile.md"
    And the source file "src/Rates.fs":
      """
      namespace Acme.Demo
      module Rates =
          let dailyRate : int = 40
      """
    And the source file "src/Totals.fs":
      """
      namespace Acme.Demo
      module Totals =
          let total (days: int) : int = days * Rates.dailyRate
      """
    And the source file "broken/Pricing.fs":
      """
      namespace Acme.Demo
      module Pricing =
          let mutable total = 0
      """

  @host-tool @needs-in-process-fsharp-frontend
  Scenario: Each documented command of the dotnet tool gives its documented output
    Then the page documents these commands
      | command                                                                                                                                      |
      | dotnet-morphir compile --language fsharp --input src/Rates.fs src/Totals.fs --package-name acme/demo --target fsharp --generate-output generated --json |
      | dotnet-morphir compile --language fsharp --input src/Rates.fs src/Totals.fs --package-name acme/demo --json                                  |
      | dotnet-morphir compile --language fsharp --input broken/Pricing.fs --package-name acme/demo --json                                           |
      | dotnet-morphir compile --language fsharp --input src/Rates.fs --package-name acme/demo --ir-version 4.1.0-draft.1 --json                     |
      | morphir compile --language fsharp --input src/Rates.fs --package-name acme/demo --json                                                       |
      | dotnet-morphir compile --language fsharp --input src/Rates.fs src/Totals.fs --package-name acme/demo --ir-version 3 --json-lines             |
    When I run each documented command of the host in the work area
    Then each documented command gives its documented output

  @host-native
  Scenario: The documented command of the native host gives its documented output
    When I run each documented command of the host in the work area
    Then each documented command gives its documented output
