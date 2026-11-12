@integration @host-tool @needs-compilation @needs-in-process-fsharp-frontend
Feature: Compile F# source to Morphir IR
  The dotnet tool compiles F# in process. A compile writes one JSON report, publishes the IR
  and a task record, and can continue into generation. The AOT executable has no built-in F# frontend.
  CompilationHosts.feature has the compile of the native host, which has no built-in F# frontend, and of
  the managed host.


  Scenario Outline: The dotnet tool compiles explicit input files to every IR version
    Given an F# project without a configured project file
    When I compile its input files at the IR version "<requested>"
    Then compilation exits with 0 and one JSON report
    And the compilation report has the IR version "<requested>"
    And the compilation report lists the source documents "Rates.fs, Pricing.fs" in that order
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
    When I compile its input files at the IR version "4.1.0-draft.1"
    Then compilation exits with 2 and one JSON report
    And the command report has the diagnostic "compile.unsupported_ir_version"
    And no IR is published

  Scenario: An unsupported construct in an input file fails with one JSON report
    Given an F# project without a configured project file
    And the source "Pricing.fs" uses a mutable binding
    When I compile its input files at the IR version "4"
    Then compilation exits with 1 and one JSON report
    And the compilation report has the diagnostic "fsharp.unsupported_mutable" in "Pricing.fs"
    And no IR is published

  Scenario Outline: The dotnet tool compiles F# to every IR version
    Given an F# project whose morphir.toml names its project file
    When I compile the project at the IR version "<requested>"
    Then compilation exits with 0 and one JSON report
    And the compilation report has the IR version "<requested>"
    And the published IR declares format version <written>

    Examples:
      | requested | written |
      | 1         | 1       |
      | 2         | 2       |
      | 3         | 3       |
      | 4         | 4       |
      | 4.0.0     | 4       |

  Scenario: A pre-release IR version that no writer declares is refused
    Given an F# project whose morphir.toml names its project file
    When I compile the project at the IR version "4.1.0-draft.1"
    Then compilation exits with 2 and one JSON report
    And the command report has the diagnostic "compile.unsupported_ir_version"
    And no IR is published

  Scenario: A project file named on the command line supplies the ordered sources
    Given an F# project without a configured project file
    When I compile the project with the project file "src/Acme.CarRental.fsproj"
    Then compilation exits with 0 and one JSON report
    And the compilation report lists the source documents "Rates.fs, Pricing.fs" in that order

  Scenario: An unsupported construct fails with one JSON report
    Given an F# project whose morphir.toml names its project file
    And the source "Pricing.fs" uses a mutable binding
    When I compile the project at the IR version "4"
    Then compilation exits with 1 and one JSON report
    And the compilation report has the diagnostic "fsharp.unsupported_mutable" in "Pricing.fs"
    And no IR is published

  Scenario: Generate reads the IR of the most recent compile
    Given an F# project whose morphir.toml names its project file
    When I compile the project at the IR version "4"
    And I generate F# for the project from its last compile
    Then generation exits with 0 and one JSON report
    And the generation input is the IR named by the compile record

  Scenario: Generate without a prior compile fails and publishes nothing
    Given an F# project whose morphir.toml names its project file
    When I generate F# for the project from its last compile
    Then generation exits with 2 and one JSON report
    And the command report has the diagnostic "compile.record_missing"
    And the output directory is not there

  Scenario Outline: Compile with a target equals compile followed by generate
    Given an F# project <project>
    When I compile the project from <input> with the target "<target>"
    And I compile the project from <input> and then generate "<target>" from the compiled IR
    Then both generated outputs have the same paths and bytes
    And both reports name the same backend

    Examples:
      | project                                | input               | target |
      | without a configured project file      | its input files     | fsharp |
      | without a configured project file      | its input files     | csharp |
      | whose morphir.toml names its project file | its project file | fsharp |
      | whose morphir.toml names its project file | its project file | csharp |

  Scenario Outline: Generation options that are not complete are refused before compilation
    Given an F# project without a configured project file
    When I compile its input files with the options "<options>"
    Then compilation exits with 2 and one JSON report
    And no IR is published

    Examples:
      | options                                                |
      | --target fsharp --target csharp --generate-output gen  |
      | --target fsharp                                        |

  Scenario: A failed compile leaves a tombstone that keeps the install ledger
    Given an F# project whose morphir.toml names its project file
    When I compile the project and install the IR to "dist"
    Then compilation exits with 0 and one JSON report
    Given the source "Pricing.fs" uses a mutable binding
    When I compile the project and install the IR to "dist"
    Then compilation exits with 1 and one JSON report
    And the compile record is a tombstone that keeps the earlier install ledger
    And no IR is published
