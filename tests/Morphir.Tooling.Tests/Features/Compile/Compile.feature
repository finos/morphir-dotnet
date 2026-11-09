@Compilation @CLI
Feature: Compile F# source to Morphir IR
  The dotnet tool compiles F# in process. A compile writes one JSON report, publishes the IR
  and a task record, and can continue into generation. The AOT executable has no built-in F# frontend.
  These scenarios were written before the command and observed red. Each one stays @ignore until the
  add-fsharp-compile task named by its @pending tag makes it pass; that task removes both tags.
  Task 4.7 delivered the command. The scenarios that read a project file also need the MSBuild
  project-file provider, so they wait for task 4.4.

  Scenario Outline: The dotnet tool compiles explicit input files to every IR version
    Given an F# project without a configured project file
    When I compile its input files with the dotnet tool at IR version "<requested>"
    Then compilation exits with 0 and one JSON report
    And the report IR version is "<requested>"
    And the report lists the source documents "Rates.fs, Pricing.fs" in that order
    And the published IR declares format version <written>

    Examples:
      | requested | written |
      | 1         | 1       |
      | 2         | 2       |
      | 3         | 3       |
      | 4         | 4       |
      | 4.0.0     | 4       |

  Scenario: A pre-release IR version that no writer declares is refused before compilation
    Given an F# project without a configured project file
    When I compile its input files with the dotnet tool at IR version "4.1.0-draft.1"
    Then compilation exits with 2 and one JSON report
    And the report has the diagnostic "compile.unsupported_ir_version"
    And no IR is published

  Scenario: An unsupported construct in an input file fails with one JSON report
    Given an F# project without a configured project file
    And the source "Pricing.fs" uses a mutable binding
    When I compile its input files with the dotnet tool at IR version "4"
    Then compilation exits with 1 and one JSON report
    And the report has the diagnostic "fsharp.unsupported_mutable" in "Pricing.fs"
    And no IR is published

  @ignore @pending:4.4
  Scenario Outline: The dotnet tool compiles F# to every IR version
    Given an F# project whose morphir.toml names its project file
    When I compile it with the dotnet tool at IR version "<requested>"
    Then compilation exits with 0 and one JSON report
    And the report IR version is "<requested>"
    And the published IR declares format version <written>

    Examples:
      | requested | written |
      | 1         | 1       |
      | 2         | 2       |
      | 3         | 3       |
      | 4         | 4       |
      | 4.0.0     | 4       |

  @ignore @pending:4.4
  Scenario: A pre-release IR version that no writer declares is refused
    Given an F# project whose morphir.toml names its project file
    When I compile it with the dotnet tool at IR version "4.1.0-draft.1"
    Then compilation exits with 2 and one JSON report
    And the report has the diagnostic "compile.unsupported_ir_version"
    And no IR is published

  @ignore @pending:4.4
  Scenario: A project file named on the command line supplies the ordered sources
    Given an F# project without a configured project file
    When I compile it with the dotnet tool using --project-file "src/Acme.CarRental.fsproj"
    Then compilation exits with 0 and one JSON report
    And the report lists the source documents "Rates.fs, Pricing.fs" in that order

  @ignore @pending:4.4
  Scenario: An unsupported construct fails with one JSON report
    Given an F# project whose morphir.toml names its project file
    And the source "Pricing.fs" uses a mutable binding
    When I compile it with the dotnet tool at IR version "4"
    Then compilation exits with 1 and one JSON report
    And the report has the diagnostic "fsharp.unsupported_mutable" in "Pricing.fs"
    And no IR is published

  Scenario: The AOT executable reports that it has no built-in F# frontend
    Given an F# project whose morphir.toml names its project file
    When I compile it with the morphir executable
    Then compilation exits with 2 and one JSON report
    And the report has the diagnostic "compile.frontend_unavailable"

  @ignore @pending:5.4
  Scenario: Generate reads the IR of the most recent compile
    Given an F# project whose morphir.toml names its project file
    When I compile it with the dotnet tool at IR version "4"
    And I generate F# for the project without an input
    Then generation exits with 0 and one JSON report
    And the generation input is the IR named by the compile record

  @ignore @pending:6.2
  Scenario: Compile with a target equals compile followed by generate
    Given an F# project whose morphir.toml names its project file
    When I compile it with the dotnet tool with target "fsharp"
    And I compile it with the dotnet tool and then generate F# from the compiled IR
    Then both generated outputs have the same paths and bytes

  @ignore @pending:5.3
  Scenario: A failed compile leaves a tombstone that keeps the install ledger
    Given an F# project whose morphir.toml names its project file
    When I compile it with the dotnet tool and install the IR to "dist"
    Then compilation exits with 0 and one JSON report
    Given the source "Pricing.fs" uses a mutable binding
    When I compile it with the dotnet tool and install the IR to "dist"
    Then compilation exits with 1 and one JSON report
    And the compile record is a tombstone that keeps the earlier install ledger
    And no IR is published
