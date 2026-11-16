@suite:frontend-csharp @semantic @basis:hand-authored
Feature: Compile a constant
  A constant of a static class becomes a value with no parameters and a literal body. The static class is a module
  below its namespace. The expected IR is written by hand from the format specification, not captured from a
  writer. See the csharp-source-frontend spec.

  Scenario Outline: frontend-csharp-0101 A constant compiles to a value with a literal body
    Given the C# package "acme/demo" exposing "Rates.Pricing"
    And the source "Pricing.cs":
      """csharp
      namespace Acme.Demo.Rates;
      public static class Pricing
      {
          public const int Rate = 40;
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And the IR equals "expected/0101/ir-<version>.json"

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |
