@integration @needs-compilation
Feature: Compile F# on each host
  The native host has no built-in F# frontend. The managed host compiles F# in process, as the tool host does
  (Compilation.feature).

  @host-native
  Scenario: The native host reports that it has no built-in F# frontend
    Given an F# project whose morphir.toml names its project file
    When I compile the project
    Then compilation exits with 2 and one JSON report
    And the command ran on the "native" host
    And the command report has the diagnostic "compile.frontend_unavailable"

  @host-managed @needs-in-process-fsharp-frontend
  Scenario: The managed host compiles F# with the in-process frontend
    Given the F# source file "src/Rates.fs" of the package "acme/demo"
    When I compile the F# source file
    Then compilation exits with 0 and one JSON report
    And the command ran on the "managed" host
    And the compilation report says that the compile succeeded with the frontend "morphir-dotnet.fsharp (in-process)"
