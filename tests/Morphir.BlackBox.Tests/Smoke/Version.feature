@smoke
Feature: Each host starts and gives its version
  As a person who installs Morphir
  I want each entry point to start and to give its version
  So that I know that the artifact runs on my operating system

  Scenario: The default host of the run gives its version
    When I run the command "--version"
    Then the exit code is 0
    And standard output is a version

  @host-native
  Scenario: The native host gives its version
    When I run the command "--version"
    Then the exit code is 0
    And standard output is a version
    And the command ran on the "native" host

  @host-managed
  Scenario: The managed host gives its version
    When I run the command "--version"
    Then the exit code is 0
    And standard output is a version
    And the command ran on the "managed" host

  @host-tool
  Scenario: The tool host gives its version
    When I run the command "--version"
    Then the exit code is 0
    And standard output is a version
    And the command ran on the "tool" host
