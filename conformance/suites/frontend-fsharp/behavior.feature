@suite:frontend-fsharp @semantic @basis:hand-authored
Feature: Run compiled values
  Each case compiles a small F# module, and then calls its values. The expected
  results are the results of the F# source, written by hand in the MCK value
  encoding (see conformance/steps/steps-1.md). The .NET runner checks them
  through F# that the F# backend generates from the compiled IR. These cases have
  no hand-written IR, so the structural verdict only checks that the model is
  valid JSON.

  Scenario Outline: frontend-fsharp-0110 A record update changes only the named field
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          type Car = { Plate: string; Seats: int }
          let grow (car: Car) : Car = { car with Seats = car.Seats + 1 }
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calling "Pricing.grow" with [{"plate":"AB-1","seats":2}] returns {"plate":"AB-1","seats":3}

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

  Scenario Outline: frontend-fsharp-0111 A union case with a named field is built and matched
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          type Size =
              | Small
              | Large of seats: int
          let label (size: Size) : string =
              match size with
              | Small -> "small"
              | Large n -> if n > 4 then "large" else "medium"
          let resize (count: int) : Size = if count > 2 then Large count else Small
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calling "Pricing.label" with ["Small"] returns "small"
    And calling "Pricing.label" with [{"Large":6}] returns "large"
    And calling "Pricing.label" with [{"Large":3}] returns "medium"
    And calling "Pricing.resize" with [4] returns {"Large":4}
    And calling "Pricing.resize" with [1] returns "Small"

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

  Scenario Outline: frontend-fsharp-0112 A list function and an optional result
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let doubled (prices: int list) : int list = List.map (fun price -> price * 2) prices
          let firstPrice (prices: int list) : int option = List.tryHead prices
          let prepend (price: int) (prices: int list) : int list = price :: prices
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calling "Pricing.doubled" with [[1,2,3]] returns [2,4,6]
    And calling "Pricing.firstPrice" with [[]] returns "Nothing"
    And calling "Pricing.firstPrice" with [[5,6]] returns {"Just":5}
    And calling "Pricing.prepend" with [1,[2,3]] returns [1,2,3]

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

  Scenario Outline: frontend-fsharp-0113 A tuple pattern swaps the items
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let swap (pair: int * string) : string * int =
              match pair with
              | (count, name) -> (name, count)
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calling "Pricing.swap" with [[2,"AB-1"]] returns ["AB-1",2]

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

  Scenario Outline: frontend-fsharp-0114 Decimal and character values
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let fee (rate: decimal) (extra: decimal) : decimal = rate + extra
          let grade (count: int) : char = if count > 2 then 'A' else 'B'
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calling "Pricing.fee" with ["10.50","2.25"] returns "12.75"
    And calling "Pricing.grade" with [3] returns "A"
    And calling "Pricing.grade" with [1] returns "B"

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

  Scenario Outline: frontend-fsharp-0115 A parameter can have the name of a module value
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let seats : int = 4
          let next (seats: int) : int = seats + 1
          let total (count: int) : int = count + seats
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calling "Pricing.seats" with [] returns 4
    And calling "Pricing.next" with [2] returns 3
    And calling "Pricing.total" with [2] returns 6

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

  Scenario Outline: frontend-fsharp-0116 A value uses a value of another module
    Given the F# package "acme/demo" exposing "Totals"
    And the source "Rates.fs":
      """fsharp
      namespace Acme.Demo
      module Rates =
          let dailyRate : int = 40
      """
    And the source "Totals.fs":
      """fsharp
      namespace Acme.Demo
      module Totals =
          let total (days: int) : int = days * Rates.dailyRate
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calling "Totals.total" with [3] returns 120

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
