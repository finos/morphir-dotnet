@ConfigurationEditing @CLI
Feature: Edit one configuration file with a reviewable effective result
  Editing uses shared plans, preserves user text and keeps JSON stdout scriptable.

  Scenario Outline: Personal edits span all eligible primary layouts
    Given a configuration editing project
    And the editing project has mixed primary layouts and a personal override
    When I edit configuration with "<command>"
    Then editing exits 0 with outcome "written"
    And editing uses the existing personal target
    And the editing effective value is "<value>"

    Examples:
      | command                       | value |
      | set custom.value after --user | after |
      | unset custom.value --user     | lower |

  Scenario: Explicit personal edits retain another member's ownership
    Given a configuration editing project
    And the editing project is a workspace with two members
    When I edit configuration with "set workspace.out_dir edited --file b/morphir.user.toml --isolated"
    Then editing exits 0 with outcome "written"
    And the personal edit preserves member workspace ownership

  Scenario: Preview is read only and reports the effective environment winner
    Given a configuration editing project
    And an editing environment winner
    When I edit configuration with "set custom.value preview --dry-run"
    Then editing exits 0 with outcome "preview"
    And the editing target retains its original bytes
    And the editing effective value is "environment"

  Scenario Outline: Typed values and scoped targets use shared editing services
    Given a configuration editing project
    When I edit configuration with "<command>"
    Then editing exits <exit> with outcome "<outcome>"

    Examples:
      | command                                             | exit | outcome   |
      | set ui.color false --type bool                       | 0    | written   |
      | set custom.value 12 --type int                       | 0    | written   |
      | set custom.value 2.0 --type number                    | 0    | written   |
      | set custom.value [1,2] --type array                   | 0    | written   |
      | set custom.value false                              | 0    | written   |
      | set custom.value before                             | 0    | unchanged |
      | set custom.value after --user                        | 0    | written   |
      | set custom.value after --global                      | 0    | written   |
      | set custom.value after --project App                 | 0    | written   |
      | set custom.value after --isolated                    | 0    | written   |
      | set custom.value after --file selected.toml --dry-run | 0    | preview   |
      | set custom.value after --file settings.yaml          | 2    | failed    |
      | set ui.color bad --type bool                         | 2    | failed    |
      | set ui.color false                                  | 2    | failed    |
      | set custom.value after --workspace                   | 2    | failed    |
      | set custom.value after --user --global               | 2    | failed    |
      | set custom.value after --profiles INVALID            | 2    | failed    |
      | unset custom.missing                                | 1    | failed    |
      | unset custom.value                                  | 0    | written   |

  Scenario: A successful overridden write retains a zero exit status
    Given a configuration editing project
    And an editing environment winner
    When I edit configuration with "set custom.value written"
    Then editing exits 0 with outcome "written"
    And the editing effective value is "environment"
    And editing preserves unrelated comments

  Scenario: Opaque reference input never appears in a preview report
    Given a configuration editing project
    And the editing target has an opaque reference
    When I edit configuration with "set custom.reference replacement --dry-run"
    Then editing exits 0 with outcome "preview"
    And editing streams redact opaque reference inputs


  Scenario: System previews retain native filesystem safety
    Given a configuration editing project
    When I edit configuration with "set custom.value after --system --dry-run"
    Then system preview respects the native no-follow policy
    And the editing target retains its original bytes

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

  Scenario: Typed reference input is redacted before rendering
    Given a configuration editing project
    When I set an opaque typed reference
    Then editing exits 0 with outcome "preview"
    And editing streams redact opaque reference inputs
    And the editing target retains its original bytes

  Scenario: Literal dotted keys use the shared key grammar
    Given a configuration editing project
    When I edit a literal dotted key
    Then editing exits 0 with outcome "written"
    And the persisted editing value has type "boolean"

  Scenario: Unset reveals a lower classic JSON value
    Given a configuration editing project
    And a classic JSON value below the editing target
    When I edit configuration with "unset custom.value"
    Then editing exits 0 with outcome "written"
    And the editing effective value is "lower"
    And the lower JSON bytes and origin are retained
