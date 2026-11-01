Feature: Compile Morphir car rental models through Myriad
  F# applications consume generated business rules from classic Morphir JSON.

  Scenario: Call a generated daily rate through a locally packaged plugin
    Given a classic car rental model with a daily rate of 5000 cents
    When a clean F# consumer builds and runs through the packaged plugin
    Then the consumer prints "5000"

  Scenario Outline: Execute the complete rental lifecycle from classic IR
    Given the supplied car rental model in version <version>
    When a clean F# consumer builds and runs through the packaged plugin
    Then the consumer reports a complete rental costing 15000 cents
    And the consumer rejects invalid rental operations

    Examples:
      | version |
      | 1       |
      | 2       |
      | 3       |

  Scenario: Rebuild after changing the model's daily rate
    Given the supplied car rental model in version 3
    When a clean F# consumer builds and runs through the packaged plugin
    Then the consumer reports a complete rental costing 15000 cents
    And the model's daily rate changes to 6000 cents
    When the existing consumer rebuilds and runs
    Then the consumer reports a complete rental costing 18000 cents

  Scenario Outline: Invalid input blocks a rebuild after successful generation
    Given a classic car rental model with a daily rate of 5000 cents
    When a clean F# consumer builds and runs through the packaged plugin
    Then the consumer prints "5000"
    When the model changes to invalid input "<input>"
    And the existing consumer build fails
    Then the build diagnostic includes "<category>" and the model path

    Examples:
      | input        | category                 |
      | missing      | unsupported-version      |
      | string       | unsupported-version      |
      | fractional   | unsupported-version      |
      | zero         | unsupported-version      |
      | future       | unsupported-version      |
      | malformed    | parse                    |
      | non-library  | unsupported-distribution |
