@integration @needs-development-packages
Feature: Build an F# consumer of the Myriad plugin package
  The Myriad plugin (Morphir.Myriad) generates F# modules from the Morphir models of a project (MorphirModel items)
  when the project builds. The test packs the plugin from the development build into the feed of its workspace. A
  model with modules is the classic daily rate model with one module for each row.

  Background:
    Given the package "Morphir.Myriad" packed from the development build

  Scenario: A configured output directory accepts a separator at its end
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    And the model "model.json" of the consumer has the output directory "generated" with the separator of the operating system at its end
    When I build and run the consumer
    Then the consumer prints "5000"
    And the consumer has the file "generated/Inventory.fs"
    When the model "model.json" of the consumer has the full path of the output directory "generated" with the separator of the operating system at its end
    And I build and run the consumer
    Then the consumer prints "5000"
    And the consumer has the file "generated/Inventory.fs"

  Scenario: Repeat builds keep the text and the time of the generated sources and manifests
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    When I build and run the consumer
    Then the consumer prints "5000"
    Given I remember the generated files of the consumer
    And I wait 50 milliseconds
    When I build the consumer
    Then the generated files of the consumer are the same as I remembered

  Scenario: A change of the model changes what the program does
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    When I build and run the consumer
    Then the consumer prints "5000"
    When the model of the consumer changes to the modules:
      | module    | daily rate |
      | inventory | 6000       |
    And I build and run the consumer
    Then the consumer prints "6000"

  Scenario: An added module compiles, and a removed module removes only its own generated output
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    When I build the consumer
    And the model of the consumer changes to the modules:
      | module    | daily rate |
      | inventory | 5000       |
      | other     | 42         |
    And the consumer program prints the daily rate of "Inventory + Other"
    And I build and run the consumer
    Then the consumer prints "5042"
    Given I put the file "user-file.txt" with the text "keep" beside the generated file "Other.fs"
    When the model of the consumer changes to the modules:
      | module    | daily rate |
      | inventory | 5000       |
    And the consumer program prints the daily rate of "Inventory"
    And I build and run the consumer
    Then the consumer has 0 generated files "Other.fs"
    And the file "user-file.txt" beside the generated files has the text "keep"
    And the consumer prints "5000"

  Scenario: A model that is not valid after a successful build fails the build and does not use the old source
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    When I build the consumer
    And the model of the consumer is the fixture "Models/malformed-model.txt"
    And I build the consumer and it fails
    Then the output contains "model.json"

  Scenario: A failure of the Myriad host keeps the generated output
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    When I build the consumer
    Given I remember the text of the generated file "Inventory.fs"
    And the consumer has the property "MorphirMyriadHostPath" with the value "does-not-exist/Myriad.dll"
    When the model of the consumer changes to the modules:
      | module    | daily rate |
      | inventory | 6000       |
    And I build the consumer and it fails
    Then the output contains "Myriad host could not be found"
    And the text of the generated file "Inventory.fs" is the same as I remembered

  Scenario: Module paths that collide fail the build before the host starts
    Given a Myriad consumer of a model with the modules:
      | module | daily rate |
      | a bc   | 5000       |
      | ab c   | 5000       |
    And the consumer has the property "MorphirMyriadHostPath" with the value "does-not-exist/Myriad.dll"
    When I build the consumer and it fails
    Then the output contains "name-collision"
    And the output does not contain "Myriad host could not be found"
    And the consumer has no generated file "*.fs" in a folder "morphir"

  Scenario: A value of the wrong type fails the build with its rule
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    And the daily rate of the module "inventory" of the model is the string "not a number"
    When I build the consumer and it fails
    Then the output contains "type-mismatch"
    And the output contains "Inventory.dailyRate"

  Scenario: A model file that is not there fails the build with its path
    Given a Myriad consumer with no model file
    When I build the consumer and it fails
    Then the output contains "model.json"

  Scenario: Two models with the same file name have different output directories
    Given a Myriad consumer of the models:
      | file              | module    | daily rate |
      | first/model.json  | inventory | 5000       |
      | second/model.json | other     | 42         |
    And the consumer program prints the daily rate of "Inventory + Other"
    When I build and run the consumer
    Then the consumer prints "5042"
    And the consumer has 2 generated files "manifest.json"

  Scenario: An output directory can have a space in its name
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    And the model "model.json" of the consumer has the output directory "generated modules"
    When I build and run the consumer
    Then the consumer prints "5000"
    And the consumer has the file "generated modules/Inventory.fs"

  Scenario: Two models cannot own one output directory
    Given a Myriad consumer of the models:
      | file       | module    | daily rate |
      | model.json | inventory | 5000       |
      | other.json | other     | 42         |
    And the model "model.json" of the consumer has the output directory "same"
    And the model "other.json" of the consumer has the output directory "same/"
    When I build the consumer and it fails
    Then the output contains "output collision"
    And the consumer has no directory "same"

  Scenario: An output directory that is a symbolic link cannot send the generation to another folder
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    And the consumer folder "generated" is a symbolic link to its folder "outside"
    And the model "model.json" of the consumer has the output directory "generated"
    When I build the consumer and it fails
    Then the output contains "symbolic link"
    And the consumer directory "outside" has no file

  Scenario: Two configurations that build at the same time have their own generated output
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    When I build the consumer
    And I build the consumer in the configurations "Debug" and "Release" at the same time with no restore
    Then the consumer has 2 generated files "manifest.json"
    And the consumer has 2 generated files "Inventory.fs"
    When I run the consumer
    Then the consumer prints "5000"

  Scenario: A clean removes the generated output of the default directory
    Given a Myriad consumer of a model with the modules:
      | module    | daily rate |
      | inventory | 5000       |
    When I build the consumer
    Then the consumer has 1 generated file "Inventory.fs"
    When I clean the consumer
    Then the consumer has 0 generated files "Inventory.fs"
    And the consumer has 0 generated files "manifest.json"
