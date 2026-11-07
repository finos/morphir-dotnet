Feature: Morphir Executable Artifact Management Commands
  As a user of the Morphir executable
  I want the dist, tool and extension commands to say they are not implemented yet
  So that I am not told an install or switch succeeded when nothing happened

  Background:
    Given a Morphir executable is available

  Scenario Outline: Unimplemented management command fails and links its issue
    When I run the command "<command>"
    Then the exit code should be 1
    And the output should contain "not implemented yet"
    And the output should contain "issues/<issue>"

    Examples:
      | command                                         | issue |
      | dist list                                       | 490   |
      | dist install https://example.com/d.tar.gz 1.0.0 | 490   |
      | tool use t 1.0.0                                | 491   |
      | extension which e                               | 492   |
