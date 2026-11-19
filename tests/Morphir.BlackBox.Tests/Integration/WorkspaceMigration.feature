@integration @needs-migration
Feature: Adopt classic projects through one bounded workspace migration
  Workspace adoption reuses independently verified project conversion and retains sources.


  Scenario Outline: A directory without a workspace supports current and explicit bounded adoption
    Given a workspace migration repository
    When I migrate the workspace with "<command>"
    Then workspace migration exits 0 with outcome "<outcome>"
    And workspace migration retains every classic source
    And workspace migration verifies the aggregate and every conversion
    And workspace migration reports members "apps/a,apps/b"

    Examples:
      | command       | outcome |
      | --dry-run     | preview |
      | . --dry-run   | preview |
      | .             | written |

  Scenario: Preview writes no destination
    Given a workspace migration repository
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 0 with outcome "preview"
    And workspace migration writes no destinations

  Scenario: Repeated include and exclude filters are root relative
    Given a workspace migration repository
    And workspace migration project "tools/c"
    When I migrate the workspace with "--include apps/* --include tools/* --exclude apps/b --exclude tools/c --dry-run"
    Then workspace migration exits 0 with outcome "preview"
    And workspace migration reports members "apps/a"
    And workspace migration reports excluded "apps/b,tools/c"

  Scenario: Default skips and nested declared workspaces bound adoption
    Given a workspace migration repository
    And workspace migration skipped and nested projects
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 0 with outcome "preview"
    And workspace migration reports members "apps/a,apps/b"
    And workspace migration reports boundary "nested"

  Scenario: Existing default and already migrated member remain unchanged
    Given a workspace migration repository
    And an existing workspace migration member and default
    When I migrate the workspace with ""
    Then workspace migration exits 0 with outcome "written"
    And workspace migration retains the existing member and default
    And workspace migration retains every classic source

  Scenario: Root projects retain root identity
    Given a workspace migration repository
    And a workspace migration root project
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 0 with outcome "preview"
    And workspace migration retains root project identity

  Scenario: Hidden primary layouts keep the member directory anchor
    Given a workspace migration repository
    And a hidden workspace migration member primary
    When I migrate the workspace with ""
    Then workspace migration exits 0 with outcome "written"
    And workspace migration retains every classic source
    And workspace migration keeps the hidden member anchor

  Scenario: An equivalent repeat preserves every target byte and timestamp
    Given a workspace migration repository
    When I migrate the workspace with ""
    Then workspace migration exits 0 with outcome "written"
    When I migrate the workspace with ""
    Then workspace migration exits 0 with outcome "unchanged"
    And workspace migration preserves every target byte and timestamp

  Scenario Outline: A blocked member prevents every destination
    Given a workspace migration repository
    And workspace migration fixture "<fixture>"
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 2 with outcome "blocked"
    And workspace migration reports diagnostics on stderr
    And workspace migration writes no destinations
    And workspace migration retains every classic source

    Examples:
      | fixture             |
      | dependencies        |
      | decorations         |
      | canonical-conflict  |
      | invalid-json5       |

  Scenario: An undeclared root with no eligible project cannot be adopted
    Given an empty workspace migration repository
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 2 with outcome "blocked"
    And workspace migration reports diagnostics on stderr
    And workspace migration writes no destinations

  Scenario: A valid workspace without conversions succeeds unchanged
    Given an empty workspace migration repository
    And a declared empty migration workspace
    When I migrate the workspace with ""
    Then workspace migration exits 0 with outcome "unchanged"

  Scenario: Ancestor and sibling projects are never adopted
    Given a workspace migration repository
    And workspace migration invoked from a bounded child
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 0 with outcome "preview"
    And workspace migration reports members ""
    And workspace migration retains root project identity

  Scenario: Secret values stay redacted throughout aggregate reports
    Given a workspace migration repository
    And workspace migration fixture "secrets"
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 0 with outcome "preview"
    And workspace migration streams redact secret values

  Scenario: Unsupported raw secret references remain in a redacted loss ledger
    Given a workspace migration repository
    And workspace migration fixture "unsupported-secrets"
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 2 with outcome "blocked"
    And workspace migration accounts for unsupported raw fields
    And workspace migration streams redact secret values
    And workspace migration writes no destinations

  Scenario: Deep raw values fit the aggregate JSON envelope
    Given a workspace migration repository
    And workspace migration fixture "deep"
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 0 with outcome "preview"
    And workspace migration verifies the aggregate and every conversion

  Scenario: Human output enumerates verified adoption
    Given a workspace migration repository
    When I migrate the workspace with human output
    Then workspace migration human output reports verified preview

  Scenario: Initialization failures retain JSON stdout
    Given a workspace migration repository
    And malformed workspace migration appsettings
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 2 with outcome "blocked"
    And workspace migration reports diagnostics on stderr
    And workspace migration writes no destinations

  Scenario Outline: Invalid controls retain the JSON result contract
    Given a workspace migration repository
    When I migrate the workspace with "<command>"
    Then workspace migration exits 2 with outcome "blocked"
    And workspace migration reports diagnostics on stderr
    And workspace migration writes no destinations

    Examples:
      | command              |
      | --unknown            |
      | --include            |
      | --exclude            |
      | first second         |
      | morphir.json         |
      | --include ../outside |

  Scenario: Configuration does not expose a migration alias
    Given a workspace migration repository
    When I request the configuration migration alias
    Then configuration migration remains unavailable

  @ParentPolicyReview

  Scenario: Existing workspace adoption cannot change unrelated new member values
    Given an empty workspace migration repository
    And workspace migration adoption fixture "incompatible"
    When I migrate the workspace with ""
    Then workspace migration exits 2 with outcome "blocked"
    And workspace migration reports diagnostics on stderr
    And workspace migration writes no destinations

  @ParentPolicyReview

  Scenario: A root exclusion retains root identity without conversion
    Given a workspace migration repository
    And a workspace migration root project
    When I migrate the workspace with "--exclude . --dry-run"
    Then workspace migration exits 0 with outcome "preview"
    And workspace migration reports excluded "."
    And workspace migration retains root project identity
    And workspace migration excludes root conversion

  @ParentPolicyReview

  Scenario: A new canonical member reports its standalone original context
    Given an empty workspace migration repository
    And workspace migration adoption fixture "compatible"
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 0 with outcome "preview"
    And workspace migration reports the new member original context
