Feature: Shared typed configuration contracts
  Embedded hosts and file adapters use the same typed documents and resolution rules.

  Scenario: Equivalent serialization formats retain typed project values
    Given equivalent project documents in JSON5 YAML and TOML
    When those documents are read independently
    Then the canonical project values are equal with distinct source identities

  Scenario: Runtime overrides follow the complete source ladder
    Given an explicitly supplied source at every precedence layer
    When the host resolves the sources
    Then the explicit value wins and higher arrays replace complete lower arrays

  Scenario: Shorter environment scalar paths win
    Given conflicting scalar and nested environment paths
    When the host resolves the sources
    Then the shorter scalar path wins independently of enumeration order

  Scenario: Later workspace profile names win over earlier member names
    Given postgres then metrics are active with workspace and member profile files
    When the host resolves the sources
    Then workspace metrics wins over member postgres

  Scenario: Disabled profiles never invoke supplied file readers
    Given disabled profiles with an invalid profile file reader
    When the adapter loads and resolves the explicit candidates
    Then the invalid profile reader was not invoked
