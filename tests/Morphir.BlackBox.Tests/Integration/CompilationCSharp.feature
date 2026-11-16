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
    Given the C# source file "src/Rates.cs" with a loop that changes nothing, of the package "acme/demo"
    When I compile the C# source file
    Then compilation exits with 1 and one JSON report
    And the compilation report has the diagnostic "csharp.unsupported_loop" in "Rates.cs"
    And no IR is published

  Scenario: The compile of C# with the target csharp runs the pass before the C# backend
    Given the C# source file "src/Rates.cs" of the package "acme/demo"
    When I compile the C# source file with the target "csharp"
    Then compilation exits with 0 and one JSON report
    And the compilation report lists the pass "morphir-dotnet.csharp.admission" before the backend
    And the compilation report has no member "passes"

  Scenario: A loop compiles to a list function of the model
    Given the C# source file "src/Totals.cs" with a loop that sums a list, of the package "acme/demo"
    When I compile the C# source file
    Then compilation exits with 0 and one JSON report
    And the published IR names the SDK function "morphir/SDK:list#foldl"

  Scenario: A loop with no Morphir form is a diagnostic that gives the reason
    Given the C# source file "src/Totals.cs" with a loop that writes a field, of the package "acme/demo"
    When I compile the C# source file
    Then compilation exits with 1 and one JSON report
    And the compilation report has the diagnostic "csharp.unsupported_loop" in "Totals.cs" with the message "The 'foreach' loop is not in the Morphir subset: it writes the field 'counter'"
    And no IR is published
