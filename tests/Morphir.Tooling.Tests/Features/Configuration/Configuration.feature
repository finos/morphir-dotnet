@ConfigurationInspection @CLI @PlatformSpecific
Feature: Inspect configuration without mutating the project
  Inspection shares canonical resolution with embedded hosts and keeps stdout scriptable.

  Scenario Outline: Reports preserve supported configuration depth
    Given an inspection workspace with a member and a personal override
    And the member contains a TOML table with <depth> path segments
    When I inspect with "show --isolated"
    Then inspection exits with 0 and one JSON result
    And the nested table at <depth> path segments retains its values

    Examples:
      | depth |
      | 65    |
      | 127   |

  Scenario: Host initialization failures preserve structured inspection output
    Given an inspection workspace with a member and a personal override
    And the invocation directory contains malformed host appsettings
    When I inspect with "get custom.winner --isolated"
    Then inspection exits with 2 and one JSON result
    And inspection diagnostics include "config.load"

  Scenario: Invalid arguments precede host initialization failures
    Given an inspection workspace with a member and a personal override
    And the invocation directory contains malformed host appsettings
    When I inspect with "get custom.winner --profiles INVALID --isolated"
    Then inspection exits with 2 and one JSON result
    And inspection diagnostics include "config.usage"

  Scenario Outline: Automatic selection preserves invalid workspace defaults
    Given an inspection workspace with a member and a personal override
    And the inspection workspace has an invalid default member
    When I inspect with "<command>"
    Then inspection exits with 2 and one JSON result
    And inspection diagnostics include "discovery.invalid_default"

    Examples:
      | command                       |
      | get ir.format --isolated      |
      | get custom.winner --user      |

  Scenario Outline: Independent scopes bypass automatic member selection
    Given an inspection workspace with a member and a personal override
    And the inspection workspace has an invalid default member
    When I inspect with "<command>"
    Then inspection exits with <exit> and one JSON result
    And inspection diagnostics exclude "discovery.invalid_default"

    Examples:
      | command                                             | exit |
      | get custom.workspace_only --workspace --isolated     | 0    |
      | get custom.machine --global                         | 0    |
      | get custom.missing --system                         | 1    |
      | get custom.winner --file selected.toml --isolated    | 0    |

  Scenario Outline: Every read command returns one JSON result
    Given an inspection workspace with a member and a personal override
    When I inspect with "<command>"
    Then inspection exits with 0 and one JSON result
    And the inspection result operation is "<operation>"

    Examples:
      | command                                  | operation |
      | get custom.empty --show-origin --show-scope | get     |
      | list --show-origin --show-scope           | list      |
      | show                                     | show      |
      | path                                     | path      |
      | explain custom.winner                    | explain   |

  Scenario: Origin and scope flags retain safe winner metadata
    Given an inspection workspace with a member and a personal override
    When I inspect with "get custom.empty --show-origin --show-scope"
    Then inspection exits with 0 and one JSON result
    And the get entry identifies its member origin

  Scenario: Global scope reads only global values
    Given an inspection workspace with a member and a personal override
    When I inspect with "get custom.machine --global"
    Then inspection exits with 0 and one JSON result
    And the inspection value is "global"

  Scenario: A missing system value does not inherit member settings
    Given an inspection workspace with a member and a personal override
    When I inspect with "get custom.member --system"
    Then inspection exits with 1 and one JSON result
    And the queried key is absent

  Scenario: Human output redacts sensitive values
    Given an inspection workspace with a member and a personal override
    When I request human inspection output
    Then human inspection succeeds without exposing sensitive values

  Scenario: Quoted paths retain literal dots
    Given an inspection workspace with a member and a personal override
    When I inspect a key containing a literal dot
    Then inspection exits with 0 and one JSON result
    And the inspection value is true

  Scenario: Scope does not fill from effective values
    Given an inspection workspace with a member and a personal override
    When I inspect with "get custom.member --workspace"
    Then inspection exits with 1 and one JSON result
    And the queried key is absent

  Scenario: Member explanation retains contributing layers
    Given an inspection workspace with a member and a personal override
    When I inspect with "explain custom.winner --isolated"
    Then inspection exits with 0 and one JSON result
    And the explanation identifies workspace and member sources

  Scenario: Empty arrays remain present
    Given an inspection workspace with a member and a personal override
    When I inspect with "get custom.empty_array"
    Then inspection exits with 0 and one JSON result
    And the inspection value is an empty array

  Scenario: Mixed format warnings preserve JSON purity
    Given an inspection workspace with a member and a personal override
    And the inspection workspace contains sibling YAML
    When I inspect with "show"
    Then inspection exits with 0 and one JSON result
    And structured warnings appear in JSON and human diagnostics only on stderr

  Scenario: Warning suppression is an explicit host control
    Given an inspection workspace with a member and a personal override
    And the inspection workspace contains sibling YAML
    When I inspect with "show --suppress-warning config.yaml_beside_toml"
    Then inspection exits with 0 and one JSON result
    And the mixed format warning is suppressed

  Scenario: Inspection redacts sensitive leaves and secret references
    Given an inspection workspace with a member and a personal override
    When I inspect with "show"
    Then inspection exits with 0 and one JSON result
    And neither stream exposes secret values or executes the secret command

  Scenario Outline: Controls select only the permitted sources
    Given an inspection workspace with a member and a personal override
    When I inspect with "<command>"
    Then inspection exits with 0 and one JSON result
    And the inspection value is "<value>"

    Examples:
      | command                                         | value    |
      | get custom.winner                               | personal |
      | get custom.winner --isolated                     | member   |
      | get custom.winner --profiles ci --isolated        | profile  |
      | get custom.winner --no-profiles --isolated        | member   |
      | get custom.winner --project app                  | member   |
      | get custom.winner --user                         | personal |
      | get custom.selected --config selected.toml --isolated | selected |
      | get custom.winner --file selected.toml           | selected |

  Scenario Outline: Invalid usage and loading failures return two
    Given an inspection workspace with a member and a personal override
    When I inspect with "<command>"
    Then inspection exits with 2 and one JSON result

    Examples:
      | command                                       |
      | get custom..winner                            |
      | get custom.winner --profiles INVALID           |
      | get custom.winner --profiles ci --no-profiles   |
      | get custom.winner --workspace --user           |
      | show --file missing.toml                       |
      | show --unknown                                |
      | get                                           |
      | get custom.winner --project unavailable        |

  Scenario: Malformed configuration fails without exposing input
    Given an inspection workspace with a member and a personal override
    And the inspection member configuration is malformed
    When I inspect with "show"
    Then inspection exits with 2 and one JSON result

  Scenario: Help succeeds in a broken workspace
    Given an inspection workspace with a member and a personal override
    And the inspection member configuration is malformed
    When I request inspection help
    Then inspection help succeeds without configuration diagnostics

  Scenario: Help avoids unrelated host appsettings
    Given an inspection workspace with a member and a personal override
    And the inspection member configuration is malformed
    And the invocation directory contains malformed host appsettings
    When I request inspection help
    Then inspection help succeeds without configuration diagnostics

  Scenario: Invalid environment profiles fail before discovery
    Given an inspection workspace with a member and a personal override
    And the inspection member configuration is malformed
    And environment profiles contain an empty name
    When I inspect with "get custom.winner"
    Then inspection exits with 2 and one JSON result
    And the failure is a usage diagnostic without a source read

  Scenario: Nonfinite and temporal values keep canonical type metadata
    Given an inspection workspace with a member and a personal override
    And the member contains nonfinite and temporal values
    When I inspect with "show --isolated"
    Then inspection exits with 0 and one JSON result
    And special values preserve their canonical kinds

  Scenario: Explanation serializes nonfinite layer contributions
    Given an inspection workspace with a member and a personal override
    And the member contains nonfinite and temporal values
    When I inspect with "explain custom.nan --isolated"
    Then inspection exits with 0 and one JSON result
    And nonfinite explanation metadata identifies its value and contribution
