@integration
Feature: The WASM runtime of the host
  As a user of the Morphir CLI
  I want the host to report whether it can run WASM extensions, and to work without its engine library
  So that a missing engine is a clear report and not a crash

  The runtime report and extension.runtime_unavailable come from task 4.2 of add-wasm-extension-host. A copy of the
  folder of the host with each engine library deleted is a host that starts without its engine library. It reports
  engine_missing on a platform that can run guests, or the safety refusal of the platform. The real build output is
  never changed. A host that carries no engine library file skips the scenarios of the copy with that reason.

  Scenario: The host reports the WASM availability of its platform with the engine of its package
    When I ask for the runtimes of the host as JSON
    Then the exit code is 0
    And the runtime report names the host of the run
    And the WASM runtime is available exactly when the platform can run guests
    And the WASM runtime has the engine of the pinned Wasmtime package, the profile "extism-kernel-v1" and 268435456 bytes of memory

  Scenario: Without its engine library the host reports the applicable unavailable reason
    Given a copy of the host without its engine library
    When I ask for the runtimes of the copy as JSON
    Then the exit code is 0
    And the WASM runtime is not available, with the reason of a missing engine on this platform
    And the WASM runtime has the engine version of the pinned Wasmtime package
    And standard error does not contain "DllNotFoundException"

  Scenario: Without its engine library a worker is refused by name with no bytes on standard output
    Given a copy of the host without its engine library
    When the copy serves "extism-echo" with the frames "initialize"
    Then the worker of the copy exits with status 3 and no bytes on standard output
    And the worker of the copy has the events "refused, exited"
    And the worker of the copy is refused with "extension.runtime_unavailable" and the reason of a missing engine on this platform

  Scenario Outline: Without its engine library the commands that need no WASM still work
    Given a copy of the host without its engine library
    When I run "<command>" with the copy
    Then the exit code is 0
    And the command of the copy wrote its result with no word of the engine

    Examples:
      | command                                     |
      | --version                                   |
      | --help                                      |
      | extension list --json                       |
      | ir verify Models/valid-ir-v3.json --json    |
