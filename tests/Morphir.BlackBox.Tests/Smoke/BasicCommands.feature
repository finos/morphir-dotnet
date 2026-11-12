@smoke
Feature: The basic commands of the host start
  As a person who installs Morphir
  I want the help and the info command to work
  So that I know that the artifact starts and knows its commands

  Scenario: The help lists the commands
    When I run the command "--help"
    Then the exit code is 0
    And the output contains "Morphir command line"
    And the output contains "Commands:"

  Scenario: The info command exits with 0
    When I run the command "info"
    Then the exit code is 0
