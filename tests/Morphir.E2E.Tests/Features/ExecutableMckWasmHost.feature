Feature: Experimental mck wasm-host commands
  As a contributor qualifying a WASM extension host
  I want morphir mck wasm-host run and report-check in both executables
  So that the crafted cases run against any host's worker mode from the same command line

  The commands are internal and experimental (OpenSpec change add-wasm-extension-host, task 1.5).

  Background:
    Given a Morphir executable is available

  Scenario: The help of run says that the command is experimental
    When I run the command "mck wasm-host run --help"
    Then the exit code should be 0
    And the output should contain "[experimental, internal]"

  Scenario: The help of report-check says that the command is experimental
    When I run the command "mck wasm-host report-check --help"
    Then the exit code should be 0
    And the output should contain "[experimental, internal]"

  Scenario: run without its arguments is an invalid invocation with one JSON document on stdout
    When I run the command "mck wasm-host run"
    Then the exit code should be 2
    And stdout should be one JSON document
    And stdout should contain "mck.usage"

  Scenario: report-check of a report that does not exist is an invalid invocation
    When I run the command "mck wasm-host report-check --kit conformance/suites/wasm-host --report no-such-report.json"
    Then the exit code should be 2
    And stdout should be one JSON document
