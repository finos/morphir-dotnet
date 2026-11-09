@suite:frontend-fsharp @refusal @basis:hand-authored
Feature: Refuse patterns outside the supported set
  A Morphir case has one pattern without a condition, and matches constructor
  fields by position. See the fsharp-source-frontend spec and the rule catalog.

  Scenario Outline: frontend-fsharp-0070 A pattern guard has a precise refusal
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let band days =
              match days with
              | d when d > 7 -> "long"
              | _ -> "short"
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_pattern_guard","location":{"uri":"Pricing.fs","range":{"start":{"line":4,"character":17},"end":{"line":4,"character":22}}}}]
      """

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

  Scenario Outline: frontend-fsharp-0071 An or pattern has a precise refusal
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let weekend day =
              match day with
              | 6 | 7 -> true
              | _ -> false
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_or_pattern","location":{"uri":"Pricing.fs","range":{"start":{"line":4,"character":10},"end":{"line":4,"character":15}}}}]
      """

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

  Scenario Outline: frontend-fsharp-0072 A record pattern has a precise refusal
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          type Car = { Seats: int }
          let seats car =
              match car with
              | { Seats = n } -> n
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_record_pattern","location":{"uri":"Pricing.fs","range":{"start":{"line":5,"character":10},"end":{"line":5,"character":23}}}}]
      """

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

  Scenario Outline: frontend-fsharp-0073 A list pattern with elements has a precise refusal
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let only rates =
              match rates with
              | [ r ] -> r
              | _ -> 0
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_list_pattern","location":{"uri":"Pricing.fs","range":{"start":{"line":4,"character":10},"end":{"line":4,"character":15}}}}]
      """

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

  Scenario Outline: frontend-fsharp-0074 A named field pattern has a precise refusal
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          type Booking = Booked of days: int
          let length booking =
              match booking with
              | Booked(days = n) -> n
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_named_field_pattern","location":{"uri":"Pricing.fs","range":{"start":{"line":5,"character":10},"end":{"line":5,"character":26}}}}]
      """

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
