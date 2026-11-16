@suite:frontend-csharp @refusal @basis:hand-authored
Feature: Report what the C# compiler and the package layout refuse
  A C# error stops compilation, and its diagnostic keeps the code csharp.compiler. A type outside the package path has
  no Morphir module. The ranges are positions in the source, counted from zero.

  Scenario Outline: frontend-csharp-0010 A C# type error is reported with the compiler code
    Given the C# package "acme/demo" exposing "Pricing.Totals"
    And the source "Pricing.cs":
      """csharp
      namespace Acme.Demo.Pricing;
      public static class Totals
      {
          public static int Bad() => "text";
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.compiler","location":{"uri":"Pricing.cs","range":{"start":{"line":3,"character":31},"end":{"line":3,"character":37}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0011 A namespace outside the package has no module
    Given the C# package "acme/demo" exposing "Place"
    And the source "Quote.cs":
      """csharp
      namespace Other.Place;
      public sealed record Quote(int X);
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.module_outside_package","location":{"uri":"Quote.cs","range":{"start":{"line":1,"character":0},"end":{"line":1,"character":34}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |
