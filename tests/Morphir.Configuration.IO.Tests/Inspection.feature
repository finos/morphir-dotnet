Feature: Inspect configuration from an embedded host
  Queries use one resolved result without a CLI or additional tree reads.

  Scenario: Scoped inspection retains only its own values
    Given a resolved inspection workspace and member
    When the embedded host inspects source scopes
    Then workspace and member values remain separate without fallback

  Scenario: Stable entries retain empty and quoted values
    Given a resolved inspection workspace and member
    When the embedded host lists member settings
    Then entries are ordered and empty arrays and literal dots remain addressable

  Scenario: Explain the complete array winner
    Given a resolved inspection workspace and member
    When the embedded host explains the targets array
    Then both contributions identify the member as the array winner

  Scenario: Safe nested document and source reports
    Given a resolved inspection workspace and member
    When the embedded host shows the document and paths
    Then secrets are redacted and missing source reports remain visible
