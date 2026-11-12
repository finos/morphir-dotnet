@ConfigurationEditing @CLI @PlatformSpecific
Feature: Edit one configuration file with a reviewable effective result
  The scenarios of this feature check the edited file with the configuration library of the product, in the test
  process. Each other scenario of the edit command, and the same scenarios with the check through the command line,
  are in tests/Morphir.BlackBox.Tests/Integration/ConfigurationEditing.feature.

  Scenario: Explicit personal edits retain another member's ownership
    Given a configuration editing project
    And the editing project is a workspace with two members
    When I edit configuration with "set workspace.out_dir edited --file b/morphir.user.toml --isolated"
    Then editing exits 0 with outcome "written"
    And the personal edit preserves member workspace ownership

  Scenario Outline: Explicit input typing is retained in TOML
    Given a configuration editing project
    When I edit configuration with "<command>"
    Then editing exits 0 with outcome "written"
    And the persisted editing value has type "<type>"

    Examples:
      | command                          | type    |
      | set custom.value false           | string  |
      | set custom.value false --type bool | boolean |
      | set custom.value 12 --type int    | integer |
      | set custom.value 2.0 --type number | number  |
      | set custom.value 100000000000000000000 --type number | number |
      | set custom.value [1,2] --type array | array   |

  Scenario: Complete typed objects reach the reusable editor
    Given a configuration editing project
    When I set a complete typed object
    Then editing exits 0 with outcome "written"
    And the persisted editing value has type "object"

  Scenario: Literal dotted keys use the shared key grammar
    Given a configuration editing project
    When I edit a literal dotted key
    Then editing exits 0 with outcome "written"
    And the persisted editing value has type "boolean"
