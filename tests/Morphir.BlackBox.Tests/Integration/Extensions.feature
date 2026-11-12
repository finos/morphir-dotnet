@integration @needs-extensions
Feature: List the extensions of a host and report its runtimes
  morphir extension list and morphir extension runtimes through the real CLI process.

  @host-native @needs-unprofiled-build
  Scenario Outline: The JSON list has the built-in extensions on standard output only
    When I list the extensions as JSON with the options "<options>"
    Then the exit code is 0
    And the extension list report has the host "morphir" and the extensions "morphir-dotnet.fsharp, morphir.csharp, morphir.fsharp, morphir.type-validator"
    And the extension list report states no deployment
    And the extension "morphir-dotnet.fsharp" is not available, and its reason names "morphir-fsharp-frontend"

    Examples:
      | options              |
      |                      |
      | --local              |
      | --platform linux-x64 |

  @host-native
  Scenario: The text list has one row for each built-in extension
    When I list the extensions as text
    Then the exit code is 0
    And the extension list text has a row for each extension
      | id                     | type              |
      | morphir-dotnet.fsharp  | frontend (fsharp) |
      | morphir.csharp         |                   |
      | morphir.fsharp         |                   |
      | morphir.type-validator | validator (1, 2, 3) |

  @host-managed @needs-unprofiled-build
  Scenario: The managed host lists the shared built-in extensions
    When I list the extensions as JSON with the options ""
    Then the exit code is 0
    And the command ran on the "managed" host
    And the extension list report has the host "morphir" and the extensions "morphir-dotnet.fsharp, morphir.csharp, morphir.fsharp, morphir.type-validator"
    And the extension list report states no deployment and the execution "jit"

  @host-native
  Scenario: The runtimes report of the host is one JSON document on standard output
    When I report the extension runtimes as JSON
    Then the exit code is 0
    And the extension runtimes report has the host "morphir" and the platform of this machine
    And the WASM runtime of the report has the engine version "50.0.1" and is available on this machine unless it is win-x64

  @host-native
  Scenario: The runtimes text names the runtime and its engine
    When I report the extension runtimes as text
    Then the exit code is 0
    And the runtimes text names the WASM runtime of this machine and the engine "wasmtime 50.0.1"
