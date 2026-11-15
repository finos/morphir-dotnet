@suite:frontend-csharp @refusal @basis:hand-authored
Feature: Refuse what the Morphir subset leaves out
  Each construct that the C# frontend refuses has one case with its code and its range. The ranges are positions in
  the source, counted from zero. The cases are written by hand from the rule catalog, not captured from the
  frontend. See the csharp-source-frontend spec.

  Scenario Outline: frontend-csharp-0001 A foreach loop is refused
    Given the C# package "acme/demo" exposing "Pricing.Totals"
    And the source "Pricing.cs":
      """csharp
      namespace Acme.Demo.Pricing;
      public static class Totals
      {
          public static int Total(int[] xs)
          {
              foreach (var x in xs) { }
              return 0;
          }
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_loop","location":{"uri":"Pricing.cs","range":{"start":{"line":5,"character":8},"end":{"line":5,"character":33}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0002 A change of a parameter is refused
    Given the C# package "acme/demo" exposing "Pricing.Totals"
    And the source "Pricing.cs":
      """csharp
      namespace Acme.Demo.Pricing;
      public static class Totals
      {
          public static int Bump(int a)
          {
              a = a + 1;
              return a;
          }
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_mutation","location":{"uri":"Pricing.cs","range":{"start":{"line":5,"character":8},"end":{"line":5,"character":17}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0003 dynamic is refused
    Given the C# package "acme/demo" exposing "Pricing.Totals"
    And the source "Pricing.cs":
      """csharp
      namespace Acme.Demo.Pricing;
      public static class Totals
      {
          public static int Size(dynamic d) => 1;
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_dynamic","location":{"uri":"Pricing.cs","range":{"start":{"line":3,"character":35},"end":{"line":3,"character":36}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0004 A property with a set accessor is refused
    Given the C# package "acme/demo" exposing "Pricing.Totals"
    And the source "Pricing.cs":
      """csharp
      namespace Acme.Demo.Pricing;
      public sealed record Quote
      {
          public int Value { get; set; }
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_mutable_property","location":{"uri":"Pricing.cs","range":{"start":{"line":3,"character":28},"end":{"line":3,"character":31}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0005 A class that is not a record is refused
    Given the C# package "acme/demo" exposing "Pricing.Totals"
    And the source "Pricing.cs":
      """csharp
      namespace Acme.Demo.Pricing;
      public class Plain { }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_type_kind","location":{"uri":"Pricing.cs","range":{"start":{"line":1,"character":13},"end":{"line":1,"character":18}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0006 A field that can change is refused
    Given the C# package "acme/demo" exposing "Pricing.Totals"
    And the source "Pricing.cs":
      """csharp
      namespace Acme.Demo.Pricing;
      public static class Totals
      {
          public static int Counter;
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_mutable_field","location":{"uri":"Pricing.cs","range":{"start":{"line":3,"character":22},"end":{"line":3,"character":29}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0007 A suppression of a Morphir rule is refused
    Given the C# package "acme/demo" exposing "Pricing.Totals"
    And the source "Pricing.cs":
      """csharp
      namespace Acme.Demo.Pricing;
      [System.Diagnostics.CodeAnalysis.SuppressMessage("Morphir", "MORPHIR1001")]
      public static class Totals
      {
          public static int One() => 1;
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_suppression","location":{"uri":"Pricing.cs","range":{"start":{"line":1,"character":1},"end":{"line":1,"character":74}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |
