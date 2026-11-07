@suite:frontend-fsharp @refusal @basis:hand-authored
Feature: Refuse mutable bindings
  Morphir values are immutable. This expectation is written from the frontend
  contract, not captured from a compiler. See the fsharp-source-frontend spec.

  Scenario Outline: frontend-fsharp-0001 Mutable bindings have a precise refusal
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let mutable total = 0
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_mutable","location":{"uri":"Pricing.fs","range":{"start":{"line":2,"character":8},"end":{"line":2,"character":15}}}}]
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
