@WorkspaceMigration @CLI @Containment @PlatformSpecific
Feature: Contain workspace migration
  The migrated directory must lie inside the discovery boundary from a flag or the environment.
  Spec: openspec discovery-containment.

  Scenario: Workspace directory outside the environment boundary
    Given a workspace migration repository
    And workspace migration environment variable MORPHIR_DISCOVERY_BOUNDARY is "<outside>"
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 2 with outcome "blocked"
    And workspace migration diagnostics include "discovery.boundary"
    And workspace migration diagnostics name "MORPHIR_DISCOVERY_BOUNDARY"
    And workspace migration writes no destinations

  Scenario: Workspace directory outside the boundary flag
    Given a workspace migration repository
    When I migrate the workspace with boundary "<outside>" and "--dry-run"
    Then workspace migration exits 2 with outcome "blocked"
    And workspace migration diagnostics include "discovery.boundary"
    And workspace migration diagnostics name "--discovery-boundary"
    And workspace migration writes no destinations

  Scenario: Workspace directory inside the environment boundary
    Given a workspace migration repository
    And workspace migration environment variable MORPHIR_DISCOVERY_BOUNDARY is "<root>"
    When I migrate the workspace with "--dry-run"
    Then workspace migration exits 0 with outcome "preview"
    And workspace migration reports members "apps/a,apps/b"
