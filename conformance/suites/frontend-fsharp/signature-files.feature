@suite:frontend-fsharp @refusal @basis:hand-authored
Feature: Refuse signature files
  Access comes from F# visibility and the exposed modules, not from a signature
  file. See the fsharp-source-frontend spec and the rule catalog.

  Scenario Outline: frontend-fsharp-0081 A signature file has a precise refusal at its start
    Given the F# package "acme/demo" exposing "Pricing"
    And the source "Pricing.fsi":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          val rate: int
      """
    And the source "Pricing.fs":
      """fsharp
      namespace Acme.Demo
      module Pricing =
          let rate = 40
      """
    When it is compiled from <input> with IR version <version>
    Then compilation fails with diagnostics:
      """json
      [{"severity":"error","code":"fsharp.unsupported_signature_file","location":{"uri":"Pricing.fsi","range":{"start":{"line":0,"character":0},"end":{"line":0,"character":0}}}}]
      """

    Examples:
      | input        | version |
      | files        | 1       |
      | files        | 2       |
      | files        | 3       |
      | files        | 4       |
      | project file | 1       |
      | project file | 2       |
      | project file | 3       |
      | project file | 4       |

    @excluded
    Examples:
      | input | version | reason |
      | directory | 1 | A directory gives Pricing.fs before Pricing.fsi in ordinal order, and F# refuses an implementation before its signature, so admission does not run. The files and project-file rows cover the refusal. |
      | directory | 2 | A directory gives Pricing.fs before Pricing.fsi in ordinal order, and F# refuses an implementation before its signature, so admission does not run. The files and project-file rows cover the refusal. |
      | directory | 3 | A directory gives Pricing.fs before Pricing.fsi in ordinal order, and F# refuses an implementation before its signature, so admission does not run. The files and project-file rows cover the refusal. |
      | directory | 4 | A directory gives Pricing.fs before Pricing.fsi in ordinal order, and F# refuses an implementation before its signature, so admission does not run. The files and project-file rows cover the refusal. |
