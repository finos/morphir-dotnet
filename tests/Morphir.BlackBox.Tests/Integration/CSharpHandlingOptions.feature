@integration @needs-compilation @needs-generation
Feature: C# handling options in compile and generate
  A user selects how Morphir handles a C# model with attributes in the C# source or with keys in morphir.toml. The
  nearest carrier wins: the command line, an attribute, a key of [codegen.csharp], the default. The journey
  Journeys/CSharpHandlingOptions.feature generates again from the compiled IR.

  Scenario: Attributes need no morphir.toml
    Given the C# source file "src/Quote.cs" with handling attributes, of the package "acme/trading"
    When I compile the C# source file with the target "csharp"
    Then compilation exits with 0 and one JSON report
    And the C# that the compile generated contains "MorphirEquals"
    And the C# that the compile generated contains "[global::Morphir.MorphirEmit(Traversal = global::Morphir.TraversalForm.Functional)]"

  Scenario: A key of [codegen.csharp] applies to a model with no attribute
    Given the C# source file "src/Price.cs" with a record of a double, of the package "acme/trading"
    And the morphir.toml of the project sets the key "float_equality" of [codegen.csharp] to "ieee"
    When I compile the C# source file with the target "csharp"
    Then compilation exits with 0 and one JSON report
    And the C# that the compile generated contains "MorphirEquals"

  Scenario: A malformed key of [codegen.csharp] fails the compile before any file is written
    Given the C# source file "src/Price.cs" with a record of a double, of the package "acme/trading"
    And the morphir.toml of the project sets the key "float_equality" of [codegen.csharp] to "maybe"
    When I compile the C# source file with the target "csharp"
    Then compilation exits with 2 and one JSON report
    And no IR is published
    And the compile generated no C#

  Scenario Outline: A malformed handling option fails before any file is written
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "csharp" from the model with the option "<option>"
    Then generation exits with 2 and one JSON report
    And the first diagnostic of the generation report has the code "invalid-backend-option"
    And the generation report has no artifact
    And the output directory is not there

    Examples:
      | option                   |
      | float-equality=maybe     |
      | traversal=sideways       |
      | tail-recursion=recursion |
      | profile=declarative      |
