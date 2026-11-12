@integration
Feature: The status of the artifact management commands
  As a user of the Morphir CLI
  I want the dist, tool and extension commands to report their actual status
  So that I am not told that an install or a switch succeeded when nothing happened

  Scenario Outline: An unimplemented management command fails and links its issue
    When I run the command "<command>"
    Then the exit code is 1
    And the output contains "morphir <group> <subcommand> is not implemented yet"
    And the output contains "https://github.com/finos/morphir-dotnet/issues/<issue>"

    Examples:
      | group     | subcommand | command                                             | issue |
      | dist      | list       | dist list                                           | 490   |
      | dist      | install    | dist install https://example.com/d.tar.gz 1.0.0     | 490   |
      | dist      | use        | dist use 1.0.0                                      | 490   |
      | dist      | remove     | dist remove 1.0.0                                   | 490   |
      | dist      | which      | dist which                                          | 490   |
      | tool      | list       | tool list                                           | 491   |
      | tool      | install    | tool install t https://example.com/t.zip 1.0.0      | 491   |
      | tool      | use        | tool use t 1.0.0                                    | 491   |
      | tool      | remove     | tool remove t 1.0.0                                 | 491   |
      | tool      | which      | tool which t                                        | 491   |
      | extension | use        | extension use e 1.0.0                               | 492   |
      | extension | remove     | extension remove e 1.0.0                            | 492   |
      | extension | which      | extension which e                                   | 492   |

  Scenario: The obsolete URL arguments of extension install fail as usage
    When I run the command "extension install e https://example.com/e.zip 1.0.0"
    Then the exit code is 1
    And the output contains "Unrecognized command or argument 'https://example.com/e.zip'"
