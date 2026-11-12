@integration @needs-generation
Feature: Generate C# from a Morphir model
  As a developer who uses Morphir
  I want to generate C# from a model with the CLI
  So that my build gets the same sources for the same model, and a failure changes nothing

  Scenario Outline: Each admitted wire form generates through the CLI
    Given the model fixture "WireForms/daily-rate-<input>"
    When I generate "csharp" from the model with the option "namespace=Pricing.Generated"
    Then the exit code is 0
    And the generation report says that the generation succeeded
    And the generation report has the backend "morphir.csharp"
    And the generation report has the source version "<version>"
    And each artifact of the generation report is a file of the output

    Examples:
      | version | input                |
      | 1       | 1.json               |
      | 2       | 2.json               |
      | 3       | 3.json               |
      | 3.0.0   | 3.0.0.json           |
      | 3.1.0   | 3.1.0.json           |
      | 4       | 4.json               |
      | 4.0.0   | 4.0.0.json           |
      | 3.0.0   | 3.0.0.text.unusual   |
      | 3.0.0   | 3.0.0.binary.unusual |
      | 3.1.0   | 3.1.0.text.unusual   |
      | 3.1.0   | 3.1.0.binary.unusual |
      | 4.0.0   | 4.0.0.text.unusual   |
      | 4.0.0   | 4.0.0.binary.unusual |

  Scenario Outline: Each admitted tree form generates through the CLI
    Given the model tree fixture "WireForms/daily-rate-<version>.tree"
    When I generate "csharp" from the model with the option "namespace=Pricing.Generated"
    Then the exit code is 0
    And the generation report says that the generation succeeded
    And the generation report has the backend "morphir.csharp"
    And the generation report has the source version "<version>"
    And each artifact of the generation report is a file of the output

    Examples:
      | version |
      | 3.0.0   |
      | 3.1.0   |
      | 4.0.0   |

  Scenario: The last option of a name wins before the backend checks the options
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "csharp" from the model with the options
      | option                     |
      | language-version=preview   |
      | language-version=15        |
      | language-version=14        |
      | namespace=First.Generated  |
      | namespace=Second.Generated |
    Then the exit code is 0
    And the first artifact of the generation report contains "namespace Second.Generated;" and not "First.Generated"
    When I generate "csharp" from the model with the options
      | option                   |
      | language-version=14      |
      | language-version=preview |
    Then the exit code is 2

  Scenario Outline: An option that is not valid is an invocation failure and changes no file
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "csharp" from the model
    Then the exit code is 0
    Given I remember the files of the output
    When I generate "csharp" from the model with the option '<option>'
    Then the exit code is 2
    And the generation report says that the generation failed with the kind "invocation"
    And the generation report has the backend "morphir.csharp"
    And the generation report has diagnostics
    And the generation report has no artifact
    And the files of the output are the same as I remembered

    Examples:
      | option                         |
      | language-version=preview       |
      | language-version=latest        |
      | language-version="14"          |
      | language-version=14.5          |
      | language-version=16            |
      | language-version=null          |
      | namespace=true                 |
      | namespace=14                   |
      | namespace=[]                   |
      | namespace=Bad;class Injected{} |
      | namespace=Bad..Name            |
      | unknown=14                     |

  Scenario: A model that the backend cannot generate is a semantic failure in the JSON report
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "csharp" from the model
    Then the exit code is 0
    Given I remember the files of the output
    When I replace '"WholeNumberLiteral": 5000' with '"DocumentLiteral": {"unsupported":true}' in the model
    And I generate "csharp" from the model
    Then the exit code is 1
    And the generation report says that the generation failed with the kind "semantic"
    And the first diagnostic of the generation report has the code "document-literal" and a location
    And the generation report has no artifact
    And the files of the output are the same as I remembered

  Scenario: A model that the backend cannot generate is a failure on standard error with no JSON report
    Given the model fixture "WireForms/daily-rate-4.json"
    When I generate "csharp" from the model
    Then the exit code is 0
    Given I remember the files of the output
    When I replace '"WholeNumberLiteral": 5000' with '"DocumentLiteral": {"unsupported":true}' in the model
    And I generate "csharp" from the model as text
    Then the exit code is 1
    And standard output is empty
    And standard error contains "document-literal"
    And the files of the output are the same as I remembered

  Scenario: An input that is not there is an operational failure
    Given no model file "missing.json"
    When I generate "csharp" from the model
    Then the exit code is 2
    And the generation report says that the generation failed with the kind "operational"
    And the output directory is not there

  Scenario: A second run keeps the files that did not change and removes only its own stale files
    Given the model fixture "Models/car-rental-v3.json"
    When I generate "csharp" from the model
    Then the exit code is 0
    Given the output has the file "User.cs" with the text "// user-owned content"
    And each file of the output has the time of the year 2000
    When I generate "csharp" from the model
    Then the exit code is 0
    And the generation report has no written file and has unchanged files
    And each file of the output still has the time of the year 2000
    When I remove the module "workflow" from the model
    And I generate "csharp" from the model
    Then the exit code is 0
    And the generation report says that it removed only "p_Car.Rental/m_WorkflowModule.g.cs"
    And the output has no file "p_Car.Rental/m_WorkflowModule.g.cs"
    And the output file "User.cs" has the text "// user-owned content"
