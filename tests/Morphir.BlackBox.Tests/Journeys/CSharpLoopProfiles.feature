@journey @needs-compilation @needs-generation
Feature: Compile C# loops and generate them in the form of a profile
  The C# frontend lowers loops to list functions and recursion, and the C# backend writes them back as loops (the
  imperative profile, the default) or as calls of Select, Where, FilterMap and Aggregate (the functional profile). The
  first scenario runs the example of the section "Profiles" of docs/content/docs/backends/csharp/_index.md as written.

  Scenario: The documented example compiles C# loops and generates the functional forms
    Given the section "Profiles" of the documentation page "backends/csharp/_index.md"
    Then the page documents these commands
      | command                                                                   |
      | morphir compile                                                           |
      | morphir generate --target csharp --output gen --option profile=functional |
    Given the morphir.toml of the section in the project directory "demo"
    And the C# block 1 of the section is the source file "demo/src/Totals.cs"
    When I run the documented commands of the section in the project directory "demo"
    Then the documented compile writes the path of the published IR of the project "demo"
    And the documented generate writes its documented files to "gen" of the project "demo"
    And the file "demo/gen/p_Acme.Demo/m_TotalsModule.g.cs" contains the C# block 2 of the section

  Scenario: Generate writes the loop forms by default
    Given the C# source file "src/Totals.cs" with a loop that sums a list, of the package "acme/demo"
    When I compile the C# source file
    And I generate "csharp" from the model
    Then generation exits with 0 and one JSON report
    And the first module artifact of the generation report contains "foreach (var amount in amounts)" and not "Aggregate"

  Scenario: The functional profile writes the functional forms
    Given the C# source file "src/Totals.cs" with a loop that sums a list, of the package "acme/demo"
    When I compile the C# source file
    And I generate "csharp" from the model with the option "profile=functional"
    Then generation exits with 0 and one JSON report
    And the first module artifact of the generation report contains ".Aggregate<int>(" and not "foreach"
