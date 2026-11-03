Feature: Shared typed configuration contracts
  Embedded hosts and file adapters use the same typed documents and resolution rules.

  Scenario: Equivalent serialization formats retain typed project values
    Given equivalent project documents in JSON5 YAML and TOML
    When those documents are read independently
    Then the canonical project values are equal with distinct source identities

