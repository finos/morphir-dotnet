@integration
Feature: Every command writes its machine output by one JSON convention
  A bare --json streams JSON Lines that end with one completed record, which holds the report of the command, and
  --json=document writes that report alone. A mode is given only after an equals sign, so a bare --json never takes
  the next argument (adopt-streaming-json, capability cli-json-output). Each scenario runs each command with machine
  output; compile has its own scenarios, in its command tests and the compilation features.

  Scenario Outline: A bare flag streams
    Given the input of "<command>"
    When I run "<command>" with a bare JSON flag
    Then standard output is JSON Lines that end with one completed record

    @needs-model-validation
    Examples: validate
      | command  |
      | validate |

    @needs-generation
    Examples: generate
      | command  |
      | generate |

    @needs-configuration
    Examples: config show
      | command     |
      | config show |

    @needs-extensions
    Examples: The extension commands
      | command            |
      | extension list     |
      | extension runtimes |

    @needs-migration
    Examples: The migrations
      | command           |
      | project migrate   |
      | workspace migrate |

    @needs-model-verification
    Examples: model verify
      | command      |
      | model verify |

  Scenario Outline: Unknown mode
    Given the input of "<command>"
    When I run "<command>" with the JSON mode "lines"
    Then the exit code is 2
    And the output names the JSON modes "stream" and "document"

    @needs-model-validation
    Examples: validate
      | command  |
      | validate |

    @needs-generation
    Examples: generate
      | command  |
      | generate |

    @needs-configuration
    Examples: config show
      | command     |
      | config show |

    @needs-extensions
    Examples: The extension commands
      | command            |
      | extension list     |
      | extension runtimes |

    @needs-migration
    Examples: The migrations
      | command           |
      | project migrate   |
      | workspace migrate |

    @needs-model-verification
    Examples: model verify
      | command      |
      | model verify |

  Scenario Outline: Every line parses
    Given the input of "<command>"
    When I run "<command>" with a bare JSON flag
    Then the exit code is 0
    And every line of standard output is a JSON record with an event, and only the last is completed

    @needs-model-validation
    Examples: validate
      | command  |
      | validate |

    @needs-generation
    Examples: generate
      | command  |
      | generate |

    @needs-configuration
    Examples: config show
      | command     |
      | config show |

    @needs-extensions
    Examples: The extension commands
      | command            |
      | extension list     |
      | extension runtimes |

    @needs-migration
    Examples: The migrations
      | command           |
      | project migrate   |
      | workspace migrate |

    @needs-model-verification
    Examples: model verify
      | command      |
      | model verify |

  Scenario Outline: A usage error still ends with completed
    Given the input of "<command>"
    When I run "<command>" with a bare JSON flag and an option that it does not have
    Then the exit code is 2
    And standard output is one completed record
    And the report of "<command>" states the usage failure

    @needs-model-validation
    Examples: validate
      | command  |
      | validate |

    @needs-generation
    Examples: generate
      | command  |
      | generate |

    @needs-configuration
    Examples: config show
      | command     |
      | config show |

    @needs-extensions
    Examples: The extension commands
      | command            |
      | extension list     |
      | extension runtimes |

    @needs-migration
    Examples: The migrations
      | command           |
      | project migrate   |
      | workspace migrate |

    @needs-model-verification
    Examples: model verify
      | command      |
      | model verify |

  Scenario Outline: A command with nothing to stream
    Given the input of "<command>"
    When I run "<command>" with a bare JSON flag
    Then standard output is one completed record
    And standard output is one JSON document

    @needs-model-validation
    Examples: validate
      | command  |
      | validate |

    @needs-generation
    Examples: generate
      | command  |
      | generate |

    @needs-configuration
    Examples: config show
      | command     |
      | config show |

    @needs-extensions
    Examples: The extension commands
      | command            |
      | extension list     |
      | extension runtimes |

    @needs-migration
    Examples: The migrations
      | command           |
      | project migrate   |
      | workspace migrate |

    @needs-model-verification
    Examples: model verify
      | command      |
      | model verify |

  Scenario Outline: The two modes carry one report
    Given the input of "<command>"
    When I run "<command>" with a bare JSON flag and then with the JSON document
    Then the document is the report of the completed record

    @needs-model-validation
    Examples: validate
      | command  |
      | validate |

    @needs-generation
    Examples: generate
      | command  |
      | generate |

    @needs-configuration
    Examples: config show
      | command     |
      | config show |

    @needs-extensions
    Examples: The extension commands
      | command            |
      | extension list     |
      | extension runtimes |

    @needs-migration
    Examples: The migrations
      | command           |
      | project migrate   |
      | workspace migrate |

    @needs-model-verification
    Examples: model verify
      | command      |
      | model verify |

  Scenario Outline: A document is pipeable
    Given the input of "<command>"
    When I run "<command>" with the JSON document
    Then standard output is one report document whose exit code is the exit code of the command

    @needs-model-validation
    Examples: validate
      | command  |
      | validate |

    @needs-generation
    Examples: generate
      | command  |
      | generate |

    @needs-configuration
    Examples: config show
      | command     |
      | config show |

    @needs-migration
    Examples: The migrations
      | command           |
      | project migrate   |
      | workspace migrate |

    @needs-model-verification
    Examples: model verify
      | command      |
      | model verify |

  Scenario Outline: The next argument stays the command's
    Given the input of "<command>"
    When I run "<command>" with a bare JSON flag right before its input
    Then the exit code is 0
    And standard output is JSON Lines that end with one completed record
    And the report of "<command>" names its input

    @needs-migration
    Examples: The migrations
      | command           |
      | project migrate   |
      | workspace migrate |

    @needs-model-verification
    Examples: model verify
      | command      |
      | model verify |

  Scenario Outline: A separated mode is refused with a hint
    Given the input of "<command>"
    When I run "<command>" with the JSON mode "document" after a space
    Then the exit code is 2
    And the output contains "--json=document"

    @needs-model-validation
    Examples: validate
      | command  |
      | validate |

    @needs-generation
    Examples: generate
      | command  |
      | generate |

    @needs-configuration
    Examples: config show
      | command     |
      | config show |

    @needs-extensions
    Examples: The extension commands
      | command            |
      | extension list     |
      | extension runtimes |

  Scenario Outline: The old flag is unknown
    Given the input of "<command>"
    When I run "<command>" with the former streaming flag
    Then the exit code is 2
    And the output contains "--json-lines"

    @needs-model-validation
    Examples: validate
      | command  |
      | validate |

    @needs-generation
    Examples: generate
      | command  |
      | generate |

    @needs-configuration
    Examples: config show
      | command     |
      | config show |

    @needs-extensions
    Examples: The extension commands
      | command            |
      | extension list     |
      | extension runtimes |

    @needs-migration
    Examples: The migrations
      | command           |
      | project migrate   |
      | workspace migrate |

    @needs-model-verification
    Examples: model verify
      | command      |
      | model verify |

  Scenario Outline: A log line goes to stderr
    Given the input of "<command>" that makes it write a log line
    When I run "<command>" with the JSON output "<mode>"
    Then standard error has a line
    And standard output is the JSON output "<mode>"

    @needs-generation @needs-host-without-dynamic-code
    Examples: generate
      | command  | mode     |
      | generate | stream   |
      | generate | document |

    @needs-migration
    Examples: project migrate
      | command         | mode     |
      | project migrate | stream   |
      | project migrate | document |

  @needs-extensions
  Scenario: A named member in both modes
    When I list the extensions with a bare JSON flag and then with the JSON document
    Then the member "host" is at the top of the document and at the top of the report of the completed record
