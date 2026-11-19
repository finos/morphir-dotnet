@integration @needs-knowledge-validation
Feature: Validate a knowledge bundle
  A knowledge author checks a bundle against the appropriate OKF version
  and can enforce the Morphir knowledge-base rules independently.

  Scenario: Current OKF permits project documentation
    Given a knowledge bundle containing project documentation
    When I check the knowledge bundle as JSON
    Then the exit code is 0
    And the knowledge report uses OKF "0.3" with conformance "conformant"

  Scenario: An explicit OKF 0.2 check requires concept frontmatter
    Given a knowledge bundle containing project documentation
    When I check the knowledge bundle as OKF "0.2"
    Then the exit code is 1
    And standard output is one JSON document
    And the knowledge report uses OKF "0.2" with conformance "non-conformant"
    And the knowledge report has the diagnostic "okf.frontmatter-required"

  Scenario: The Morphir policy enforces conventions separately
    Given a knowledge bundle with an unresolved local link
    When I check the knowledge bundle under Morphir policy
    Then the exit code is 1
    And the knowledge report uses OKF "0.3" with conformance "conformant"
    And the knowledge report has the diagnostic "morphir.unresolved-link"

  Scenario: Malformed YAML has a location in the JSON report
    Given a knowledge bundle with malformed frontmatter
    When I check the knowledge bundle as JSON
    Then the exit code is 1
    And standard output is one JSON document
    And the knowledge report has a located finding in the malformed concept

  Scenario: A missing directory is an input failure
    Given a knowledge bundle directory that is missing
    When I check the knowledge bundle as JSON
    Then the exit code is 2
    And the knowledge report has the diagnostic "okf.bundle-unavailable"

  Scenario: The command documents its options
    When I ask how to validate a knowledge bundle
    Then the exit code is 0
    And the output contains "--okf-version"
    And the output contains "--policy"
    And the output contains "--json"
