Feature: Generate C# through the existing command
  As a Morphir developer
  I want to generate immutable C# libraries
  So that an ordinary consumer can execute the model

  Scenario: Registered C# provider generates a nonempty classic library
    Given a C# daily-rate library at version "3" in "json"
    When I invoke the real C# generation command
    Then the C# command exits with 0 and one JSON report
    And the C# report identifies the registered provider
    And the C# daily-rate source is published

  Scenario: Registered C# provider generates a nonempty v4 library
    Given a C# daily-rate library at version "4" in "json"
    When I invoke the real C# generation command
    Then the C# command exits with 0 and one JSON report
    And the C# report identifies the registered provider
    And the C# daily-rate source is published
