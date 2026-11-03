Feature: Bounded project and workspace discovery
  Hosts share declaration-aware roots without changing legacy artifact locations.

  Scenario: A member primary does not hide its workspace
    Given a declared workspace above a standalone member
    When the host discovers from inside the member
    Then the owning workspace and member are both identified

  Scenario Outline: Every primary layout and extension identifies its enclosing location
    Given a primary at "<path>" with contents "<contents>"
    When discovery starts below its enclosing location
    Then the enclosing project location is selected
    Examples:
      | path                        | contents           |
      | morphir.json                | {project:{}}       |
      | morphir.json5               | {project:{}}       |
      | morphir.yaml                | project: {}        |
      | morphir.yml                 | project: {}        |
      | morphir.toml                | [project]          |
      | .morphir/morphir.json        | {project:{}}       |
      | .morphir/morphir.json5       | {project:{}}       |
      | .morphir/morphir.yaml        | project: {}        |
      | .morphir/morphir.yml         | project: {}        |
      | .morphir/morphir.toml        | [project]          |
      | .config/morphir/config.json  | {project:{}}       |
      | .config/morphir/config.json5 | {project:{}}       |
      | .config/morphir/config.yaml  | project: {}        |
      | .config/morphir/config.yml   | project: {}        |
      | .config/morphir/config.toml  | [project]          |

  Scenario: Same-group ambiguity retains every path
    Given conflicting TOML primaries across layouts
    When the host discovers from inside the member
    Then both conflicting paths are diagnosed

  Scenario: Isolated loading keeps active profiles and omits personal files
    Given a project with an active profile and personal settings
    When the host loads the project in isolation
    Then only the primary and active profile supply project settings
