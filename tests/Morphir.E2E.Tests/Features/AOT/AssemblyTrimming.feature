Feature: Assembly Trimming
  As a CLI developer
  I want trimmed assemblies
  So that I reduce deployment size

  Background:
    Given a morphir-dotnet CLI project

  Scenario: Trimming with link mode
    Given a self-contained morphir-dotnet build
    And PublishTrimmed is enabled
    And TrimMode is set to link
    When I publish the application
    Then unused assemblies should be removed
    And unused types should be trimmed
    And the output size should be reduced compared to untrimmed

  Scenario: Preserving types with DynamicDependency
    Given types marked with [DynamicDependency] attributes
    And PublishTrimmed is enabled
    When I trim the application
    Then those types should not be removed
    And reflection should still work on preserved types

  Scenario: Trimming warnings detection
    Given a project with reflection usage
    And trim analyzers are enabled
    When I build with PublishTrimmed=true
    Then trim warnings should be present
    And warnings should identify trimming risks

  Scenario: JSON serialization preservation
    Given types used for JSON serialization
    And source-generated JsonSerializerContext is used
    When I build with trimming enabled
    Then the build should succeed without warnings
    And JSON serialization should work at runtime

  Scenario: Embedded resources in trimmed build
    Given JSON schemas as embedded resources
    When I build with trimming enabled
    Then embedded resources should be preserved
    And resources should be loadable at runtime

  Scenario: Trimmed build size comparison
    Given a self-contained morphir CLI
    When I build without trimming
    Then the executable size should be recorded as baseline
    When I build with PublishTrimmed=true
    Then the executable should be at least 50% smaller than baseline

  Scenario: Trimming with third-party dependencies
    Given morphir-dotnet with all dependencies
    And PublishTrimmed is enabled
    When I build the application
    Then all AOT-compatible dependencies should trim correctly
    And no runtime errors should occur from over-trimming

  Scenario: Feature switches for size reduction
    Given feature switches are configured
    And EventSourceSupport is disabled
    And HttpActivityPropagationSupport is disabled
    When I build with trimming
    Then the executable size should be further reduced
    And disabled features should not be included

  Scenario: Trimmer root descriptors
    Given custom types that must be preserved
    And a TrimmerRootDescriptor.xml file exists
    When I build with trimming
    Then types specified in descriptor should be preserved
    And trimming should respect the descriptor rules

  Scenario: Invariant globalization size savings
    Given InvariantGlobalization is enabled
    When I build with trimming
    Then culture-specific assemblies should be removed
    And approximately 5 MB should be saved
    And the application should work without culture-specific formatting
