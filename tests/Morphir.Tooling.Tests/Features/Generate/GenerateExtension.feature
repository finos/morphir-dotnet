@Generation @CLI
Feature: Select an external generation backend
  generate --extension and [codegen.<target>].extension select a backend that runs as a MEP guest process, as
  compile --extension and [frontend.<language>].extension select a frontend. Without a selection the built-in
  backend of the target runs, and the report is unchanged.

  Scenario: An explicit extension gives the built-in's files, and the report names the extension
    Given a daily-rate model at version "4" in "json"
    When I generate F# with the external F# backend extension
    Then generation exits with 0 and one JSON report
    And the report names the external F# backend over MEP
    And the generated files equal those of the built-in F# backend

  Scenario: The configuration key selects the extension, and --extension overrides it
    Given a daily-rate model at version "4" in "json"
    And a project whose configuration selects the external F# backend
    When I generate F# for the project
    Then generation exits with 0 and one JSON report
    And the report names the external F# backend over MEP
    When I generate F# for the project with the extension "no-such-backend"
    Then generation exits with 2 and one JSON report
    And the report has the diagnostic "extension.unavailable" that names "no-such-backend"

  Scenario: Without a selection the built-in backend runs, and the report keeps its members
    Given a daily-rate model at version "4" in "json"
    When I generate F# with explicit settings
    Then generation exits with 0 and one JSON report
    And the report names the built-in F# backend with the existing members only
