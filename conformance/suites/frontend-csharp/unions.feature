@suite:frontend-csharp @refusal @basis:hand-authored
Feature: Refuse a union that cannot convert to a custom type
  A C# 15 union converts to a Morphir custom type when each case type is a Morphir type reference. A union whose case
  has no name, whose case another union of the module also has, or whose use reaches the state with no case is
  refused with one error for each reason. The ranges are positions in the source, counted from zero. The cases are
  written by hand from the csharp-source-frontend spec, not captured from the frontend.

  Scenario Outline: frontend-csharp-0020 The default of a union is refused
    Given the C# package "acme/demo" exposing "Shapes.Make"
    And the source "Shapes.cs":
      """csharp
      namespace Acme.Demo.Shapes;
      public sealed record Circle(double R);
      public sealed record Rect(double W);
      public union Shape(Circle, Rect);
      public static class Make
      {
          public static Shape None() => default;
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_union_default","location":{"uri":"Shapes.cs","range":{"start":{"line":6,"character":34},"end":{"line":6,"character":41}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0021 A null arm on a union is refused
    Given the C# package "acme/demo" exposing "Shapes.Make"
    And the source "Shapes.cs":
      """csharp
      namespace Acme.Demo.Shapes;
      public sealed record Circle(double R);
      public sealed record Rect(double W);
      public union Shape(Circle, Rect);
      public static class Make
      {
          public static int Size(Shape s) => s switch { Circle c => 1, Rect r => 2, null => 3 };
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_union_null","location":{"uri":"Shapes.cs","range":{"start":{"line":6,"character":78},"end":{"line":6,"character":82}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0022 The Value property of a union is refused
    Given the C# package "acme/demo" exposing "Shapes.Make"
    And the source "Shapes.cs":
      """csharp
      namespace Acme.Demo.Shapes;
      public sealed record Circle(double R);
      public sealed record Rect(double W);
      public union Shape(Circle, Rect);
      public static class Make
      {
          public static bool Raw(Shape s) => s.Value is Circle;
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_union_value","location":{"uri":"Shapes.cs","range":{"start":{"line":6,"character":39},"end":{"line":6,"character":46}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0023 IUnion is refused
    Given the C# package "acme/demo" exposing "Shapes.Make"
    And the source "Shapes.cs":
      """csharp
      namespace Acme.Demo.Shapes;
      public sealed record Circle(double R);
      public sealed record Rect(double W);
      public union Shape(Circle, Rect);
      public static class Make
      {
          internal static int Size(System.Runtime.CompilerServices.IUnion u) => 1;
      }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_union_interface","location":{"uri":"Shapes.cs","range":{"start":{"line":6,"character":29},"end":{"line":6,"character":67}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0024 A union with a type parameter as a case is refused
    Given the C# package "acme/demo" exposing "Shapes"
    And the source "Shapes.cs":
      """csharp
      namespace Acme.Demo.Shapes;
      public sealed record Nothing;
      public union Opt<T>(T, Nothing);
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_union_case","location":{"uri":"Shapes.cs","range":{"start":{"line":2,"character":20},"end":{"line":2,"character":21}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0025 A member of a union is refused
    Given the C# package "acme/demo" exposing "Shapes"
    And the source "Shapes.cs":
      """csharp
      namespace Acme.Demo.Shapes;
      public sealed record Circle(double R);
      public sealed record Rect(double W);
      public union Shape(Circle, Rect) { public int Size => 1; }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.unsupported_union_member","location":{"uri":"Shapes.cs","range":{"start":{"line":3,"character":35},"end":{"line":3,"character":56}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |

  Scenario Outline: frontend-csharp-0026 Two unions of one module with one case type are refused
    Given the C# package "acme/demo" exposing "Shapes"
    And the source "Shapes.cs":
      """csharp
      namespace Acme.Demo.Shapes;
      public sealed record Circle(double R);
      public sealed record Rect(double W);
      public union Shape(Circle, Rect);
      public union Other(Circle, Rect);
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"csharp.name_collision","location":{"uri":"Shapes.cs","range":{"start":{"line":4,"character":19},"end":{"line":4,"character":25}}}},{"severity":"error","code":"csharp.name_collision","location":{"uri":"Shapes.cs","range":{"start":{"line":4,"character":27},"end":{"line":4,"character":31}}}}]
      """

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |
