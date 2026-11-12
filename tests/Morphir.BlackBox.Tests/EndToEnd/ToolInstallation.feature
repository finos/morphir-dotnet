@e2e @lane @packages
Feature: The packed tools install from a local feed and start
  A user installs a tool package of the build with dotnet tool install from a folder of packages. These scenarios put
  the package that the pack step of the build made into the feed of the workspace and install it into a tool folder.

  Scenario: The tool package installs from a local feed
    Given the packed package "Morphir.Tool" in the feed of the workspace
    When I install the tool "Morphir.Tool" from the feed into a tool folder
    Then the exit code is 0
    And the tool folder has the command "dotnet-morphir"

  Scenario: The command of the installed tool runs
    Given the packed package "Morphir.Tool" in the feed of the workspace
    When I install the tool "Morphir.Tool" from the feed into a tool folder
    Then the exit code is 0
    When I run the installed command "dotnet-morphir" with "--version"
    Then the exit code is 0
    And standard output is a version

  Scenario: The installed tool uninstalls
    Given the packed package "Morphir.Tool" in the feed of the workspace
    When I install the tool "Morphir.Tool" from the feed into a tool folder
    Then the exit code is 0
    When I uninstall the tool "Morphir.Tool" from the tool folder
    Then the exit code is 0
    And the tool folder has no command "dotnet-morphir"

  Scenario Outline: An F# extension tool answers describe with its claims and its package version
    Given the packed package "<package>" in the feed of the workspace
    When I install the tool "<package>" from the feed into a tool folder
    Then the exit code is 0
    When I send a describe request to the installed command "<command>"
    Then the exit code is 0
    And the describe reply has the extension id "<id>"
    And the describe reply has the claims version "0.1.0-draft.2"
    And the extension version of the describe reply is the version of the package "<package>"
    And the describe reply claims the model transport tuples of "<operation>" for the IR versions "4.0.0, 3.0.0"

    Examples:
      | package                                 | command                 | id                                 | operation                     |
      | Morphir.Frontend.FSharp.Extension       | morphir-fsharp-frontend | org.finos.morphir.fsharp-frontend  | morphir.frontend.compileModel |
      | Morphir.CodeGeneration.FSharp.Extension | morphir-fsharp-backend  | org.finos.morphir.fsharp-backend   | morphir.backend.generateModel |
