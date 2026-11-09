@suite:frontend-fsharp @semantic @basis:hand-authored
Feature: Compile a record type and a field access
  A record type becomes a type alias of a record, and a function that reads a
  field becomes a value with a typed input and a field body. The expected IR is
  written by hand for each IR version from the format specifications, not
  captured from a writer. See the fsharp-source-frontend spec.

  Scenario Outline: frontend-fsharp-0100 A record field read compiles for every IR version
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          type Car = { Plate: string; Seats: int }
          let seats (car: Car) : int = car.Seats
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And the IR equals "expected/0100/ir-<version>.json"
    And calling "Pricing.seats" with [{"plate":"AB-1","seats":2}] returns 2

    Examples:
      | input        | version |
      | files        | 1       |
      | files        | 2       |
      | files        | 3       |
      | files        | 4       |
      | directory    | 1       |
      | directory    | 2       |
      | directory    | 3       |
      | directory    | 4       |
      | project file | 1       |
      | project file | 2       |
      | project file | 3       |
      | project file | 4       |
