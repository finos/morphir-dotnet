@integration @needs-generation
Feature: Generate from a recorded Orders tree with bounded model access

  @needs-built-extensions
  Scenario Outline: Read selected values from the descriptor path
    Then bounded generation reads order <order> from its recorded descriptor
    Examples:
      | order |
      | 73    |
      | 74    |

  @needs-built-extensions
  Scenario Outline: Enforce the negotiated wire limit
    Then bounded generation refuses oversized request "<request>"
    Examples:
      | request |
      | false   |
      | true    |

  @needs-wasm-guests
  Scenario: Reach the configured WASM loader without reading unrelated bodies
    Then the recorded tree reaches the configured WASM loader

  @needs-wasm-guests
  Scenario Outline: Enforce selected WASM state and full frame limits
    Then configured WASM enforces state <state> and frame <frame> with diagnostic "<diagnostic>"
    Examples:
      | state   | frame | diagnostic                   |
      | 1       | 32768 | maxGuestStateBytes           |
      | 1048576 | 4119  | maxFrameBytes                |
      | 1048576 | 4120  | with a response for the id 0  |

  @needs-wasm-guests
  Scenario Outline: Generate exact output with the shared Orders guest
    Then configured WASM generates order <order>
    Examples:
      | order |
      | 73    |
      | 74    |
      | 128   |

  @needs-built-extensions
  Scenario: Preserve the previous install when a late definition is invalid
    Then bounded generation records only completed products and preserves the prior install on late failure

  @needs-built-extensions
  Scenario: Protect another member's producer record
    Then bounded generation protects sibling task records

  @needs-built-extensions
  Scenario: Refuse a recorded FIFO before opening it
    Then bounded generation refuses a recorded FIFO

  @needs-wasm-guests
  Scenario: Process production payloads through the Orders scale profile
    Then the Orders scale profile generates original production payloads

  @needs-wasm-guests
  Scenario: Refuse a document one byte over through the Orders scale profile
    Then the Orders scale profile refuses a document one byte over
