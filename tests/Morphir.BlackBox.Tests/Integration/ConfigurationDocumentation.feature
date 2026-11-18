@integration @host-native @needs-configuration
Feature: The commands of the config page give their documented output
  The example of the section "Containment from the environment" of docs/content/docs/cli/config.md runs in the work
  area with the variables that the page exports, and its report has the documented containment. The page shows the
  boundary with an example path; the boundary must be the work area.

  Scenario: The containment from the environment is in the report
    Given the section "Containment from the environment" of the documentation page "cli/config.md"
    Then the page documents these commands
      | command                                                   |
      | export MORPHIR_DISCOVERY_BOUNDARY=$PWD MORPHIR_ISOLATED=1 |
      | morphir config path --json=document                       |
    When I run each documented command of the host with the exported variables of the section
    Then each documented command exits with 0
    And the report of each documented command has the members of the JSON block of the section
