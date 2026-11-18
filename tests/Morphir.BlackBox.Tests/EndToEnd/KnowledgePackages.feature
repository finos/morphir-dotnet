@e2e @lane @packages
Feature: Consume the Morphir knowledge library packages

  Scenario Outline: A knowledge package carries both supported frameworks
    Given the packed package "<package>"
    Then the version of the package is the version of the changelog
    And the package has the entries:
      | entry                              |
      | lib/net10.0/<package>.dll           |
      | lib/net11.0/<package>.dll           |

    Examples:
      | package              |
      | Morphir.Knowledge     |
      | Morphir.Knowledge.Okf |
      | Morphir.Knowledge.Rdf |

  Scenario Outline: Fresh F# and C# projects use each package independently
    When fresh F# and C# consumers use the "<capability>" knowledge package on "<framework>"
    Then both knowledge consumers complete the example with their expected dependencies

    Examples:
      | capability | framework |
      | selection  | net10.0   |
      | selection  | net11.0   |
      | bundle     | net10.0   |
      | bundle     | net11.0   |
      | N-Quads    | net10.0   |
      | N-Quads    | net11.0   |
