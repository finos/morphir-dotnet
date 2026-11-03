Feature: Opt-in standard .NET configuration
  A non-CLI host consumes one canonical resolution without losing typed metadata.

  Scenario: A shorter winning array reaches source-generated options
    Given a canonical lower array replaced by one higher target
    When the host opts into Morphir configuration
    Then standard configuration and generated options contain only the winning target
    And the unrelated host setting is preserved

  Scenario: Present empty arrays differ from missing arrays
    Given a canonical empty targets array and an absent missing array
    When the host opts into Morphir configuration
    Then generated options have an empty targets array and an absent missing array

  Scenario: Typed canonical metadata remains available
    Given distinct canonical integer and string values
    When the host opts into Morphir configuration
    Then flattened strings agree while canonical types and origins remain distinct

  Scenario Outline: Unrepresentable paths cannot silently overwrite values
    Given canonical values with a <kind> collision
    When the host tries to project Morphir configuration
    Then projection fails with original paths and retains the canonical result
    Examples:
      | kind      |
      | case      |
      | separator |
      | prefix    |
