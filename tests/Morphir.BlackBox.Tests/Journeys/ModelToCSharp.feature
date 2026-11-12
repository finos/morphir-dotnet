@journey @needs-model-verification @needs-generation
Feature: From a Morphir model to a C# library
  As a developer who has a Morphir model
  I want to check the model and then generate C# from it
  So that my application can use the business logic of the model

  Scenario: A developer verifies a model and generates a C# library from it
    Given the model fixture "Models/daily-rate-v3.json"
    When I verify the model
    Then the exit code is 0
    And the output contains "✓ VALID"
    When I generate "csharp" from the model with the option "namespace=Pricing.Generated"
    Then the exit code is 0
    And the generation report says that the generation succeeded
    And each artifact of the generation report is a file of the output
    And the output has one C# file for the module "inventory" of the package "Car.Rental"
    And the first artifact of the generation report contains "namespace Pricing.Generated;" and not "error"
