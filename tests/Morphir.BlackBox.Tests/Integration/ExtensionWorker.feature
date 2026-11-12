@integration
Feature: The experimental extension worker
  As a conformance runner that qualifies a WASM extension host
  I want morphir extension worker in each host
  So that crafted modules run through the real runtime of the host over stdio

  The worker is internal and experimental (OpenSpec change add-wasm-extension-host, task 4.1). Product
  commands do not use it. "corpus:" names a module of the pinned crafted corpus Fixtures/MepWasm,
  "guest:" one of the worker guests of Fixtures/WorkerGuests.

  Scenario: The help as JSON gives the build identity and says that the worker is internal and experimental
    When I ask the worker for its help as JSON
    Then the worker exits with status 0
    And standard output of the worker is one command result whose build identity is 64 hexadecimal digits
    And the help in the command result contains "[experimental, internal]"

  Scenario: A guest that prints answers with frames and nothing else on standard output
    When the worker serves "corpus:extism-fd-write" with the frames "initialize, exit"
    Then the worker exits with status 0
    And standard output of the worker holds only these frames:
      | frame                                       |
      | {"jsonrpc":"2.0","id":1,"result":"printed"} |
    And the guest output contains "stdout: kit-stdout-marker"
    And the guest output contains "stderr: kit-stderr-marker"

  Scenario: A guest that logs answers with frames and nothing else on standard output
    When the worker serves "corpus:extism-log" with the frames "initialize, exit"
    Then the worker exits with status 0
    And standard output of the worker holds only these frames:
      | frame                                       |
      | {"jsonrpc":"2.0","id":1,"result":"level-2"} |
    And the guest output contains "log info: kit-info--marker"
    And the guest output contains "morphir_log trace: kit-morph-marker"

  Scenario: An echo guest answers each request with one frame and each notification with none
    When the worker serves "corpus:extism-echo" with the frames "initialize, initialized, shutdown, exit"
    Then the worker exits with status 0
    And standard output of the worker holds only these frames:
      | frame                                                                                             |
      | {"jsonrpc":"2.0","id":1,"method":"morphir.initialize","params":{"protocolVersion":"0.2.0-draft.1"}} |
      | {"jsonrpc":"2.0","id":2,"method":"morphir.shutdown"}                                                |
    And standard error of the worker has the events "started, exited"

  Scenario: A refused module gives one refused line, no bytes on standard output and status 3
    When the worker serves "corpus:env-open-socket" with the frames "initialize"
    Then the worker exits with status 3
    And standard output of the worker is empty
    And standard error of the worker has exactly one "refused" event with the code "extension.abi_unsupported"
    And standard error of the worker has no "started" event

  Scenario: A stale build identity is refused before the module is read
    When the worker is started with another build identity for a module file that is not there
    Then the worker exits with status 3
    And standard output of the worker is empty
    And standard error of the worker has exactly one "refused" event with the code "extension.worker_runtime_mismatch"
    And standard error of the worker has no "fault" event

  Scenario: A file that is not a WASM module is a fault before started
    When the worker serves a text file named "guest.wasm"
    Then the worker exits with status 1
    And standard output of the worker is empty
    And standard error of the worker has the events "fault, exited"
    And the fault has the kind "failure" and a message that contains "not a WASM module"

  Scenario: A module file that is not there is a fault before started
    When the worker serves a module file that is not there
    Then the worker exits with status 1
    And standard output of the worker is empty
    And standard error of the worker has the events "fault, exited"
    And the fault has the kind "failure" and a message that contains "there is no file"

  Scenario: An argument that the worker does not know is a usage error with no bytes on standard output
    When the worker is started with the extra argument "--verbose"
    Then the worker exits with status 2
    And standard output of the worker is empty
    And standard error of the worker has no "started" event

  Scenario: A guest that writes 20 MiB for each call keeps standard output to frames and its report bounded
    When the worker serves "guest:flood-output" with the frames "initialize, initialize, initialize, initialize, initialize, exit"
    Then the worker exits with status 0
    And standard output of the worker holds only 5 frames that are each {"jsonrpc":"2.0","id":1,"result":"flooded"}
    And each output event line of the worker is at most 4608 bytes
    And the output events state that more than 100 MiB of guest output were dropped
    And standard error of the worker holds less than 512 KiB
    And the peak working set of the worker is less than 64 MiB above that of an echo session

  Scenario: An interrupt stops a spinning guest within one second with status 130 and no fault
    When the worker serves "guest:workspace-then-spin" with the frames "initialize" and the arguments "--instructions 9007199254740991 --call-ms 600000"
    And the guest has made its private workspace
    And I interrupt the worker
    Then the worker exits with status 130 within one second of the interrupt
    And standard error of the worker has the events "started, exited"
    And the temporary directory of the worker is empty
