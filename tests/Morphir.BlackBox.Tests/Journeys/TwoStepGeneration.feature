@journey @host-tool @needs-compilation @needs-generation @needs-in-process-fsharp-frontend
Feature: Generate F# from the IR of a prior compile, as the generate page documents
  The two-step example of docs/content/docs/cli/generate.md ("Input from a prior compile") runs with the real
  dotnet-morphir on the sources of the MCK corpus case frontend-fsharp-0116. Each command gets --project and an
  absolute output.

  Scenario: The documented two-step example compiles a project and generates F# from its last compile
    Given the section "Input from a prior compile" of the documentation page "cli/generate.md"
    Then the page documents these commands
      | command                                              |
      | dotnet-morphir compile                               |
      | dotnet-morphir generate --target fsharp --output gen |
    Given the morphir.toml of the section in the project directory "demo"
    And the source file "demo/src/Rates.fs":
      """
      namespace Acme.Demo
      module Rates =
          let dailyRate : int = 40
      """
    And the source file "demo/src/Totals.fs":
      """
      namespace Acme.Demo
      module Totals =
          let total (days: int) : int = days * Rates.dailyRate
      """
    When I run the documented commands of the section in the project directory "demo"
    Then the documented compile writes the path of the published IR of the project "demo"
    And the documented generate writes its documented files to "gen" of the project "demo"
