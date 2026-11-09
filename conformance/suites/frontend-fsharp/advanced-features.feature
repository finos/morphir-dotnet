@suite:frontend-fsharp @refusal @basis:hand-authored
Feature: Refuse active patterns, units of measure and quotations
  Morphir patterns match constructors and values, numbers carry no unit, and a
  model holds values, not code. See the fsharp-source-frontend spec and the rule
  catalog.

  Scenario Outline: frontend-fsharp-0040 An active pattern has a precise refusal
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let (|Long|Short|) days = if days > 7 then Long else Short
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_active_pattern","location":{"uri":"Pricing.fs","range":{"start":{"line":2,"character":4},"end":{"line":2,"character":62}}}}]
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

  Scenario Outline: frontend-fsharp-0041 A unit of measure has a precise refusal
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          [<Measure>] type km
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_unit_of_measure","location":{"uri":"Pricing.fs","range":{"start":{"line":2,"character":4},"end":{"line":2,"character":23}}}}]
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

  Scenario Outline: frontend-fsharp-0042 A quotation has a precise refusal
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let rule = <@ 2 * 3 @>
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_quotation","location":{"uri":"Pricing.fs","range":{"start":{"line":2,"character":15},"end":{"line":2,"character":26}}}}]
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
