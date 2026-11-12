@journey @needs-development-packages
Feature: Compile Morphir car rental models through Myriad
  An F# application uses the business rules that the Myriad plugin generates from a classic Morphir JSON model.
  The test packs the plugin from the development build into the feed of its workspace.

  Background:
    Given the package "Morphir.Myriad" packed from the development build

  Scenario: Call a generated daily rate through a locally packaged plugin
    Given a Myriad consumer of the model fixture "Models/daily-rate-v3.json"
    When I build and run the consumer
    Then the consumer prints "5000"

  Scenario Outline: Execute the complete rental lifecycle from classic IR
    Given a Myriad consumer of the model fixture "Models/car-rental-v<version>.json"
    And the consumer program is the car rental example
    When I build and run the consumer
    Then the consumer reports a complete rental costing 15000 cents
    And the consumer rejects invalid rental operations

    Examples:
      | version |
      | 1       |
      | 2       |
      | 3       |

  Scenario: Rebuild after changing the model's daily rate
    Given a Myriad consumer of the model fixture "Models/car-rental-v3.json"
    And the consumer program is the car rental example
    When I build and run the consumer
    Then the consumer reports a complete rental costing 15000 cents
    When the daily rate of the car rental model changes to 6000 cents
    And I build and run the consumer with no restore
    Then the consumer reports a complete rental costing 18000 cents

  Scenario Outline: Invalid input blocks a rebuild after successful generation
    Given a Myriad consumer of the model fixture "Models/daily-rate-v3.json"
    When I build and run the consumer
    Then the consumer prints "5000"
    When the model of the consumer changes to the invalid input "<input>"
    And I build the consumer with no restore and it fails
    Then the output contains "<category>"
    And the output contains "model.json"

    Examples:
      | input       | category                 |
      | missing     | unsupported-version      |
      | string      | unsupported-version      |
      | fractional  | unsupported-version      |
      | zero        | unsupported-version      |
      | future      | unsupported-version      |
      | malformed   | parse                    |
      | non-library | unsupported-distribution |
