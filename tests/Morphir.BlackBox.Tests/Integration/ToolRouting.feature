@integration @needs-tool-routing
Feature: Route morphir mck to the separately installed morphir-mck
  morphir mck <args> runs the separately installed morphir-mck <args> (cli-tool-dispatch). These scenarios run each
  host with a stub morphir-mck, and compare what the stub received and wrote with a standalone run of the stub. The
  stub is a program of the development build (@needs-built-extensions).
  MckRoutingTests of Morphir.Tests keeps the scenarios that send a signal to the CLI or kill it.

  @needs-built-extensions
  Scenario Outline: The arguments pass through unchanged
    Given the stub morphir-mck is on the search path
    When I run mck with the arguments
      | argument      |
      | run           |
      | --suite       |
      | frontend cases |
      | --report      |
      | ünïcode.json  |
      | --help        |
      |               |
      | a"b c         |
      | @response.rsp |
      | --version     |
    Then the exit code is 0
    And the command ran on the "<host>" host
    And the tool received the arguments
      | argument      |
      | run           |
      | --suite       |
      | frontend cases |
      | --report      |
      | ünïcode.json  |
      | --help        |
      |               |
      | a"b c         |
      | @response.rsp |
      | --version     |

    @host-native
    Examples: The native host
      | host   |
      | native |

    @host-tool
    Examples: The tool host
      | host |
      | tool |

  @needs-built-extensions
  Scenario Outline: A routed run and a standalone run write the same and exit the same
    Given the stub morphir-mck is on the search path
    And the tool exits with 3
    When I run mck with "verify --gate frontend" and the tool alone with the same arguments
    Then the exit code is 3
    And the command ran on the "<host>" host
    And the routed run writes and exits as the tool alone
    And standard error contains "morphir-mck stub: standard error"

    @host-native
    Examples: The native host
      | host   |
      | native |

    @host-tool
    Examples: The tool host
      | host |
      | tool |

  @needs-built-extensions
  Scenario Outline: The tool reads the standard input of the caller in the directory of the caller
    Given the stub morphir-mck is on the search path
    And the tool reads its standard input, and the input is "piped text\nsecond line\n"
    When I run mck with "read" and the tool alone with the same arguments
    Then the tool read the input "piped text\nsecond line\n" in the directory of the tool alone, the work area
    And the command ran on the "<host>" host

    @host-native
    Examples: The native host
      | host   |
      | native |

    @host-tool
    Examples: The tool host
      | host |
      | tool |

  @needs-built-extensions
  Scenario Outline: With no tool the CLI says so on standard error and exits with 127
    Given no tool is on the search path
    When I run the command "mck run" on the search path
    Then the exit code is 127
    And the command ran on the "<host>" host
    And standard output is empty
    And standard error contains "morphir-mck"
    And standard error contains "MORPHIR_MCK"
    And standard error contains "finos/morphir"

    @host-native
    Examples: The native host
      | host   |
      | native |

    @host-tool
    Examples: The tool host
      | host |
      | tool |

    @host-managed
    Examples: The managed host
      | host    |
      | managed |

  @needs-built-extensions
  Scenario Outline: A tool in the current directory is not run
    Given no tool is on the search path
    And the stub morphir-mck is in the current directory, and the current directory is on the search path
    When I run the command "mck run" on the search path
    Then the exit code is 127
    And the command ran on the "<host>" host
    And standard output is empty

    @host-native
    Examples: The native host
      | host   |
      | native |

    @host-tool
    Examples: The tool host
      | host |
      | tool |

  @needs-built-extensions
  Scenario Outline: A relative MORPHIR_MCK is refused
    Given no tool is on the search path
    And the stub morphir-mck is in the current directory
    And MORPHIR_MCK is the relative name of the tool
    When I run the command "mck run" on the search path
    Then the exit code is 127
    And the command ran on the "<host>" host
    And standard output is empty
    And standard error contains "MORPHIR_MCK"
    And standard error contains "absolute"

    @host-native
    Examples: The native host
      | host   |
      | native |

    @host-tool
    Examples: The tool host
      | host |
      | tool |

  @needs-built-extensions
  Scenario Outline: An absolute MORPHIR_MCK runs with no search path
    Given no tool is on the search path
    And MORPHIR_MCK is the absolute path of the stub
    When I run the command "mck run" on the search path
    Then the exit code is 0
    And the command ran on the "<host>" host
    And the tool received the arguments
      | argument |
      | run      |

    @host-native
    Examples: The native host
      | host   |
      | native |

    @host-tool
    Examples: The tool host
      | host |
      | tool |

  @needs-built-extensions
  Scenario Outline: Another unknown command starts nothing
    Given the stub of a tool "morphir-lint" is on the search path
    And the tool exits with 42
    When I run the command "lint src" on the search path
    Then the exit code is neither 0 nor 42
    And the command ran on the "<host>" host
    And standard output is not a report of the tool
    And the output contains "lint"
    And the tool alone with the arguments "src" exits with 42

    @host-native
    Examples: The native host
      | host   |
      | native |

    @host-tool
    Examples: The tool host
      | host |
      | tool |

  @needs-built-extensions
  Scenario Outline: The help lists mck
    Given no tool is on the search path
    When I run the command "--help" on the search path
    Then the exit code is 0
    And the command ran on the "<host>" host
    And the help lists the command mck

    @host-native
    Examples: The native host
      | host   |
      | native |

    @host-tool
    Examples: The tool host
      | host |
      | tool |

  @needs-built-extensions @host-managed
  Scenario: The managed host passes the arguments and the exit code through
    Given the stub morphir-mck is on the search path
    And the tool exits with 3
    When I run mck with the arguments and the tool alone with the same arguments
      | argument       |
      | run            |
      | --suite        |
      | frontend cases |
      | ünïcode.json   |
      | --help         |
      |                |
    Then the exit code is 3
    And the command ran on the "managed" host
    And the tool received the arguments
      | argument       |
      | run            |
      | --suite        |
      | frontend cases |
      | ünïcode.json   |
      | --help         |
      |                |
    And the routed run writes and exits as the tool alone

  Scenario Outline: The experimental wasm-host commands go to the separate runner
    Given MORPHIR_MCK is the absolute path of a tool that is not there
    When I run the command "<command>" on the search path
    Then the exit code is 127
    And standard output is empty
    And standard error contains "morphir-mck"
    And standard error contains "MORPHIR_MCK"

    @host-native
    Examples: The native host
      | command                     |
      | mck wasm-host run --help    |
      | mck wasm-host report-check  |

    @host-tool
    Examples: The tool host
      | command                     |
      | mck wasm-host run --help    |
      | mck wasm-host report-check  |
