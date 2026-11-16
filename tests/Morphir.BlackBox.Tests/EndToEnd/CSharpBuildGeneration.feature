@e2e @lane @packages @needs-generation
Feature: Generate C# from a Morphir model in the build of a C# project
  The package Morphir.Lang.CSharp generates C# from each *.morphir.json additional file of a project. By default its
  source generator adds the code to the compilation. When the project sets MorphirGenerateCSharpFiles, its MSBuild
  target writes the code into the intermediate output folder. Both call the pipeline function of the CLI, so they give
  the paths and the bytes of morphir generate. The consumer targets net10.0, so the output has the polyfill file. The
  scenarios put the packages of the pack step into the feed of the workspace.

  Background:
    Given the packed packages of C# generation in the feed of the workspace
    And the model fixture "Models/daily-rate-v4.json"

  Scenario: The source generator compiles the code of the CLI into the program
    Given a C# consumer of the model
    When I build and run the C# consumer
    Then the C# consumer prints "daily rate: 5000"
    When I generate "csharp" from the model
    Then the exit code is 0
    And the files of the source generator in the C# consumer are the files of the output

  Scenario: The MSBuild target writes the code of the CLI into the intermediate output folder
    Given a C# consumer of the model that writes the generated files with the MSBuild target
    When I build and run the C# consumer
    Then the C# consumer prints "daily rate: 5000"
    And the source generator wrote no file in the C# consumer
    When I generate "csharp" from the model
    Then the exit code is 0
    And the files of the MSBuild target in the C# consumer are the files of the output

  Scenario Outline: A model that is not valid fails the build with the reason
    Given the model fixture "Models/malformed-model.txt" as "model.json"
    And <consumer>
    When I build the C# consumer and it fails
    Then the output contains "MORPHIR2001"
    And the output contains "car-rental.morphir.json"
    And the output contains "invalid-json"

    Examples:
      | consumer                                                                     |
      | a C# consumer of the model                                                   |
      | a C# consumer of the model that writes the generated files with the MSBuild target |

  Scenario: A library and an application each have their own polyfill file, and a match across them is exhaustive
    Given the model fixture "Models/Shapes/shapes-v4.json"
    And a C# library of the model and a C# consumer that matches on the types of the library
    When I build and run the C# consumer
    Then the C# consumer prints "circle 2, small, daily rate 5000"
    And the C# library and the C# consumer each have the polyfill file
    And the last build of the C# consumer has no warning

  Scenario Outline: The compiler checks a match on a type of the library as exhaustive
    Given the model fixture "Models/Shapes/shapes-v4.json"
    And a C# library of the model and a C# consumer that matches on the types of the library
    And the match of the C# consumer has no arm for the case "<case>"
    When I build the C# consumer and it fails
    Then the output contains "CS8509"

    Examples:
      | case  |
      | Rect  |
      | Large |

  Scenario: The commands of the page of the build integration run as written
    When I follow the documentation page "backends/csharp/build-integration.md" with the packed packages
    Then the exit code is 0
