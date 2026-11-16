@integration @needs-generation
Feature: Generate C# through the existing command
  As a Morphir developer
  I want to generate immutable C# libraries
  So that an ordinary consumer can execute the model

  Scenario Outline: The registered C# backend generates a nonempty library
    Given the model fixture "<fixture>"
    When I generate "csharp" from the model
    Then generation exits with 0 and one JSON report
    And the generation report has the target "csharp"
    And the generation report has the backend "morphir.csharp"
    And the generation report has the polyfill file and one C# module artifact that contains "5000" and "#nullable enable"

    Examples:
      | fixture                     |
      | WireForms/daily-rate-3.json |
      | WireForms/daily-rate-4.json |

  Scenario: An unsupported C# language alias is an invocation failure
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "csharp" from the model with the option "language-version=preview"
    Then generation exits with 2 and one JSON report
    And the first diagnostic of the generation report has the code "invalid-backend-option"
    And the generation report has no artifact
    And the output directory is not there

  Scenario: An older C# language version names the supported version
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "csharp" from the model with the option "language-version=14"
    Then generation exits with 2 and one JSON report
    And the first diagnostic of the generation report has the code "unsupported-language"
    And the generation report has the diagnostic "unsupported-language" that names "15"
    And the generation report has no artifact
    And the output directory is not there

  Scenario: A semantic refusal is structured command data
    Given the model fixture "WireForms/daily-rate-4.json"
    And the model has the problem "document-literal"
    When I generate "csharp" from the model
    Then generation exits with 1 and one JSON report
    And the first diagnostic of the generation report has the code "document-literal"
    And the generation report has no member "passes"
    And the generation report has no artifact
    And the output directory is not there

  Scenario: The pass before the C# backend is listed in the report
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "csharp" from the model
    Then generation exits with 0 and one JSON report
    And the generation report lists the pass "morphir-dotnet.csharp.admission"

  # A pull request runs this scenario on the development build (JIT), and the release and manual workflows run it on
  # each published target, also on the Native AOT executable. So the Native AOT output is the JIT output.
  Scenario: The generated C# of the Acme.Trading model is the reviewed C#
    Given the model fixture "CSharpGeneration/acme-trading/model.json"
    When I generate "csharp" from the model
    Then generation exits with 0 and one JSON report
    And the generated files are the files of the fixture "CSharpGeneration/acme-trading/generated"

  @needs-all-hosts
  Scenario: Both hosts write the same C# bytes
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "csharp" from the model
    And I generate "csharp" from the model on the other host
    Then generation exits with 0 and one JSON report
    And the files that both hosts generated are the same
