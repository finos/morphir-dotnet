@suite:frontend-fsharp @semantic @basis:hand-authored @value-codec:1
Feature: Call values in value codec 1
  The calls of this feature use value codec 1, the shape of the morphir-elm
  DataCodec and of morphir-tests.json (see conformance/steps/steps-1.md). The
  feature tag is the canonical selector. Scenario 0121 overrides it with a
  scenario tag, scenario 0122 with the value codec step, and scenario 0123 with
  the suffix of one call step. These cases have no hand-written IR.

  Scenario Outline: frontend-fsharp-0120 Constructors, Maybe and Result in value codec 1
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          type Size =
              | Small
              | ExtraLarge of seats: int
              | Pair of count: int * name: string
          let resize (count: int) : Size = if count > 8 then ExtraLarge count else Small
          let label (size: Size) : string =
              match size with
              | Small -> "small"
              | ExtraLarge _ -> "extra large"
              | Pair(_, name) -> name
          let seatsOf (size: Size) : int option =
              match size with
              | ExtraLarge seats -> Some seats
              | _ -> None
          let validate (count: int) : Result<int, string> = if count > 0 then Ok count else Error "empty"
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calling "Pricing.resize" with [9] returns ["extraLarge",9]
    And calling "Pricing.resize" with [1] returns ["small"]
    And calling "Pricing.label" with [["pair",1,"x"]] returns "x"
    And calling "Pricing.seatsOf" with [["extraLarge",9]] returns 9
    And calling "Pricing.seatsOf" with [["small"]] returns null
    And calling "Pricing.validate" with [2] returns ["ok",2]
    And calling "Pricing.validate" with [0] returns ["err","empty"]

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

  @value-codec:2
  Scenario Outline: frontend-fsharp-0121 A scenario tag selects value codec 2
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          type Size =
              | Small
              | ExtraLarge of seats: int
              | Pair of count: int * name: string
          let resize (count: int) : Size = if count > 8 then ExtraLarge count else Small
          let label (size: Size) : string =
              match size with
              | Small -> "small"
              | ExtraLarge _ -> "extra large"
              | Pair(_, name) -> name
          let seatsOf (size: Size) : int option =
              match size with
              | ExtraLarge seats -> Some seats
              | _ -> None
          let validate (count: int) : Result<int, string> = if count > 0 then Ok count else Error "empty"
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calling "Pricing.resize" with [9] returns {"ExtraLarge":9}
    And calling "Pricing.seatsOf" with ["Small"] returns "Nothing"
    And calling "Pricing.validate" with [0] returns {"Err":"empty"}

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

  Scenario Outline: frontend-fsharp-0122 The value codec step selects value codec 2
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          type Size =
              | Small
              | ExtraLarge of seats: int
              | Pair of count: int * name: string
          let resize (count: int) : Size = if count > 8 then ExtraLarge count else Small
          let label (size: Size) : string =
              match size with
              | Small -> "small"
              | ExtraLarge _ -> "extra large"
              | Pair(_, name) -> name
          let seatsOf (size: Size) : int option =
              match size with
              | ExtraLarge seats -> Some seats
              | _ -> None
          let validate (count: int) : Result<int, string> = if count > 0 then Ok count else Error "empty"
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calls use value codec 2
    And calling "Pricing.label" with [{"Pair":[1,"x"]}] returns "x"
    And calling "Pricing.seatsOf" with [{"ExtraLarge":9}] returns {"Just":9}

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

  Scenario Outline: frontend-fsharp-0123 A call suffix selects the value codec of one call
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          type Size =
              | Small
              | ExtraLarge of seats: int
              | Pair of count: int * name: string
          let resize (count: int) : Size = if count > 8 then ExtraLarge count else Small
          let label (size: Size) : string =
              match size with
              | Small -> "small"
              | ExtraLarge _ -> "extra large"
              | Pair(_, name) -> name
          let seatsOf (size: Size) : int option =
              match size with
              | ExtraLarge seats -> Some seats
              | _ -> None
          let validate (count: int) : Result<int, string> = if count > 0 then Ok count else Error "empty"
      """
    When it is compiled from <input> with IR version <version>
    Then compilation succeeds with no error diagnostics
    And calling "Pricing.resize" with [9] returns ["extraLarge",9]
    And calling "Pricing.resize" with [9] returns {"ExtraLarge":9} using value codec 2

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
