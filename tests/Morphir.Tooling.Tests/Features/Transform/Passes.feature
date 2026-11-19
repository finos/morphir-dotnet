@Transform @PlatformSpecific
Feature: Run the passes of the middle end in compile and generate
  A pass of the stage after a frontend runs in morphir compile, between the frontend and the writer. A pass of the stage
  before a backend runs in morphir generate, right before the backend of its target. The passes read the current IR only,
  so a model of an older family skips them with one info diagnostic, and a pass error stops the command before it
  publishes. The scenarios register test passes and test backends in the test process: the built-in passes of the CLI
  cannot give a skip or a pass error. The black-box features CSharpGenerationCommand and CompilationCSharp check the
  built-in pass for C# through the CLI.

  Scenario: Compile runs a pass after the frontend and reports it
    Given a pass "check" of the stage after a frontend
    When I compile the F# sources at the IR version "4"
    Then the compile exits with 0
    And the pass "check" ran on the modules "rates, pricing"
    And the stage "transform" ran after the stage "frontend" and before the stage "write"
    And the compile report lists the pass "check"

  Scenario: Compile skips the passes on a model of an older family with one info diagnostic
    Given a pass "check" of the stage after a frontend
    When I compile the F# sources at the IR version "3"
    Then the compile exits with 0
    And no pass ran
    And the compile has one info diagnostic "transform.skipped" in the stage "transform"
    And the IR is published

  Scenario: A pass error in compile stops before publication
    Given a pass "failing" of the stage after a frontend that fails with "test.failed"
    When I compile the F# sources at the IR version "4"
    Then the compile exits with 1
    And the compile has the error "test.failed"
    And the stage "write" did not start
    And no IR is published

  Scenario: Generate runs the pass before the backend of its target only
    Given a pass "check" of the stage before the backend "current-only"
    And a pass "other" of the stage before the backend "another"
    When I generate "current-only" from the daily-rate model at the version "4.0.0"
    Then the generation succeeds
    And the pass "check" ran before the backend
    And the pass "other" did not run
    And the generation report lists the pass "check"

  Scenario: Generate skips the passes on a classic model for a backend that reads classic models
    Given a pass "check" of the stage before the backend "classic"
    When I generate "classic" from the daily-rate model at the version "3.0.0"
    Then the generation succeeds
    And no pass ran
    And the generation has one info diagnostic "transform.skipped"

  Scenario: A pass error in generate stops before the backend and publishes nothing
    Given a pass "failing" of the stage before the backend "current-only" that fails with "test.failed"
    When I generate "current-only" from the daily-rate model at the version "4.0.0"
    Then the generation fails with the error "test.failed"
    And the backend did not run
    And no generated file is published
