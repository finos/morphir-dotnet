@suite:frontend-fsharp @refusal @basis:hand-authored
Feature: Refuse conditional compilation
  This frontend does not choose defines, so it must not choose a branch
  silently. See the fsharp-source-frontend spec and the rule catalog.

  Scenario Outline: frontend-fsharp-0080 Conditional compilation has a precise refusal
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
      #if PREMIUM
          let rate = 80
      #else
          let rate = 40
      #endif
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_conditional_compilation","location":{"uri":"Pricing.fs","range":{"start":{"line":2,"character":0},"end":{"line":2,"character":11}}}}]
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
