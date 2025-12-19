Feature: Native AOT Compilation
  As a CLI developer
  I want to compile morphir-dotnet to Native AOT
  So that users get fast startup times and small binaries

  Background:
    Given a morphir-dotnet CLI project

  Scenario: Successful AOT compilation
    Given PublishAot is enabled in the project
    When I build the project with PublishAot=true
    Then the build should succeed without errors
    And the output should be a native executable
    And no IL2XXX warnings should be present

  Scenario: AOT with size optimizations
    Given PublishAot is enabled
    And IlcOptimizationPreference is set to Size
    And InvariantGlobalization is enabled
    When I build with all size optimizations
    Then the build should succeed
    And the executable size should be less than 12 MB for linux-x64
    And the executable size should be less than 15 MB for win-x64

  Scenario: AOT executable runs correctly
    Given an AOT-compiled morphir executable
    When I run the --version command
    Then the command should succeed
    And the version should be displayed

  Scenario: All CLI commands work in AOT
    Given an AOT-compiled morphir executable
    When I run the --help command
    Then the command should succeed
    And the help text should be displayed
    When I run the ir verify command with a valid IR file
    Then the command should succeed
    And the verification result should be correct

  Scenario: JSON output works in AOT
    Given an AOT-compiled morphir executable
    When I run ir verify with --json flag
    Then the command should succeed
    And the output should be valid JSON
    And no serialization errors should occur

  Scenario: Detecting reflection usage during build
    Given a project with reflection usage
    And AOT analyzers are enabled
    When I build the project
    Then IL2026 warnings should be present
    And the warnings should suggest source generators

  Scenario: Size target for minimal CLI
    Given a minimal morphir CLI with basic features only
    And all size optimizations are enabled
    When I build with PublishAot=true
    Then the executable size should be between 5 MB and 8 MB

  Scenario: Size target for feature-rich CLI
    Given a full-featured morphir CLI
    And all size optimizations are enabled
    When I build with PublishAot=true
    Then the executable size should be between 8 MB and 12 MB

  Scenario: Cross-platform AOT builds
    Given a morphir-dotnet CLI project
    When I build for linux-x64 with PublishAot=true
    Then the build should succeed
    When I build for win-x64 with PublishAot=true
    Then the build should succeed
    When I build for osx-x64 with PublishAot=true
    Then the build should succeed

  Scenario: AOT build performance
    Given an AOT-compiled morphir executable
    When I measure startup time for --version command
    Then the startup time should be less than 100ms
    And memory usage should be less than 50MB
