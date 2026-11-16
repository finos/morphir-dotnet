@journey @needs-compilation @needs-generation
Feature: Generate C# again from a compiled C# model and keep its handling options
  The frontend keeps the backend options of the attributes in the model. So morphir generate on the compiled IR applies
  them again, and the command line still wins over them.

  Scenario: Generate on the compiled IR keeps the options of the attributes
    Given the C# source file "src/Quote.cs" with handling attributes, of the package "acme/trading"
    When I compile the C# source file
    And I generate "csharp" from the model
    Then generation exits with 0 and one JSON report
    And the first module artifact of the generation report contains "MorphirEquals" and not "MorphirEmit"

  Scenario: The command line wins over the attributes
    Given the C# source file "src/Quote.cs" with handling attributes, of the package "acme/trading"
    When I compile the C# source file
    And I generate "csharp" from the model with the option "float-equality=clr"
    Then generation exits with 0 and one JSON report
    And the first module artifact of the generation report contains "record Quote" and not "MorphirEquals"
