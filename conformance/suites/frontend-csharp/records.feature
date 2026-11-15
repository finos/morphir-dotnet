@suite:frontend-csharp @semantic @basis:hand-authored
Feature: Compile a record type
  A plain record becomes a type alias of a record, and each field type is located at its C# type syntax. The
  expected IR is written by hand from the format specification, not captured from a writer. See the
  csharp-source-frontend spec.

  Scenario Outline: frontend-csharp-0100 A record compiles to a record type alias
    Given the C# package "acme/demo" exposing "Records"
    And the source "Car.cs":
      """csharp
      namespace Acme.Demo.Records;
      public sealed record Car(string Plate, int Seats);
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And the IR equals "expected/0100/ir-<version>.json"

    Examples:
      | input        | version |
      | files        | 4       |
      | directory    | 4       |
      | project file | 4       |
