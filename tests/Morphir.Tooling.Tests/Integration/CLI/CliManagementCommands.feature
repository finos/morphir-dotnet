Feature: CLI artifact management commands are not implemented yet
  As a user of the Morphir CLI
  I want the dist, tool and extension commands to say they are not implemented yet
  So that I am not told an install or switch succeeded when nothing happened

  Background:
    Given the Morphir CLI is available

  @Integration @CLI
  Scenario Outline: Unimplemented management command fails and links its issue
    When I run the CLI with command "<command>"
    Then the exit code should be 1
    And the output should contain "morphir <group> <subcommand> is not implemented yet"
    And the output should contain "https://github.com/finos/morphir-dotnet/issues/<issue>"

    Examples:
      | group     | subcommand | command                                          | issue |
      | dist      | list       | dist list                                        | 490   |
      | dist      | install    | dist install https://example.com/d.tar.gz 1.0.0  | 490   |
      | dist      | use        | dist use 1.0.0                                   | 490   |
      | dist      | remove     | dist remove 1.0.0                                | 490   |
      | dist      | which      | dist which                                       | 490   |
      | tool      | list       | tool list                                        | 491   |
      | tool      | install    | tool install t https://example.com/t.zip 1.0.0   | 491   |
      | tool      | use        | tool use t 1.0.0                                 | 491   |
      | tool      | remove     | tool remove t 1.0.0                              | 491   |
      | tool      | which      | tool which t                                     | 491   |
      | extension | list       | extension list                                   | 492   |
      | extension | install    | extension install e https://example.com/e.zip 1.0.0 | 492 |
      | extension | use        | extension use e 1.0.0                            | 492   |
      | extension | remove     | extension remove e 1.0.0                         | 492   |
      | extension | which      | extension which e                                | 492   |
