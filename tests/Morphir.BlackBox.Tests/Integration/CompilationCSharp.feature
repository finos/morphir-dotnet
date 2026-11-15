@integration @needs-compilation
Feature: Compile C# on each host
  The C# frontend runs on Roslyn, which runs under Native AOT, so it is built into every host: the native host and
  the managed host compile C# in process, as the tool host does. Nothing is downloaded or installed.

  @host-native
  Scenario: The native host compiles C# with the built-in frontend
    Given the C# source file "src/Rates.cs" of the package "acme/demo"
    When I compile the C# source file
    Then compilation exits with 0 and one JSON report
    And the command ran on the "native" host
    And the compilation report says that the compile succeeded with the frontend "morphir-dotnet.csharp (in-process)"

  @host-managed
  Scenario: The managed host compiles C# with the built-in frontend
    Given the C# source file "src/Rates.cs" of the package "acme/demo"
    When I compile the C# source file
    Then compilation exits with 0 and one JSON report
    And the command ran on the "managed" host
    And the compilation report says that the compile succeeded with the frontend "morphir-dotnet.csharp (in-process)"

  Scenario: A construct outside the subset is a diagnostic with its code and document
    Given the C# source file "src/Rates.cs" with a foreach loop, of the package "acme/demo"
    When I compile the C# source file
    Then compilation exits with 1 and one JSON report
    And the compilation report has the diagnostic "csharp.unsupported_loop" in "Rates.cs"
    And no IR is published
