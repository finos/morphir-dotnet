@integration @needs-configuration
Feature: Contain configuration discovery
  The discovery boundary and isolation come from a flag or the environment, and a flag wins.
  Spec: openspec discovery-containment.

  Background:
    Given an inspection workspace with a member and a personal override

  Scenario: Environment boundary applies
    Given environment variable MORPHIR_DISCOVERY_BOUNDARY is "<member>"
    When I inspect without a boundary flag with "path"
    Then inspection exits with 0 and one JSON result
    And inspection consults no project source outside "<member>"

  Scenario: Flag overrides the environment
    Given environment variable MORPHIR_DISCOVERY_BOUNDARY is "<member>"
    When I inspect with "path"
    Then inspection exits with 0 and one JSON result
    And inspection consults the workspace primary

  Scenario: Environment boundary excludes the current directory
    Given environment variable MORPHIR_DISCOVERY_BOUNDARY is "<outside>"
    When I inspect without a boundary flag with "path"
    Then inspection exits with 2 and one JSON result
    And inspection diagnostics include "discovery.boundary"
    And the inspection diagnostic names "MORPHIR_DISCOVERY_BOUNDARY"
    And inspection consults no sources

  Scenario: Environment isolates
    Given environment variable MORPHIR_ISOLATED is "1"
    When I inspect with "path"
    Then inspection exits with 0 and one JSON result
    And machine and personal sources are not consulted

  Scenario: Flag turns isolation off
    Given environment variable MORPHIR_ISOLATED is "true"
    When I inspect with "path --isolated false"
    Then inspection exits with 0 and one JSON result
    And machine and personal sources are consulted

  Scenario: Bare flag isolates
    Given environment variable MORPHIR_ISOLATED is "0"
    When I inspect with "path --isolated"
    Then inspection exits with 0 and one JSON result
    And machine and personal sources are not consulted

  Scenario: Unrecognized isolation value
    Given environment variable MORPHIR_ISOLATED is "yes-please"
    When I inspect with "get project.name"
    Then inspection exits with 2 and one JSON result
    And the inspection diagnostic names "MORPHIR_ISOLATED"
    And inspection consults no sources

  Scenario: Path reports the effective controls and their sources
    Given environment variable MORPHIR_DISCOVERY_BOUNDARY is "<member>"
    And environment variable MORPHIR_ISOLATED is "1"
    When I inspect without a boundary flag with "path"
    Then inspection exits with 0 and one JSON result
    And the reported boundary is "<member>" from "environment"
    And the reported isolation is true from "environment"

  Scenario: Path reports flag controls over the environment
    Given environment variable MORPHIR_ISOLATED is "1"
    When I inspect with "path --isolated false"
    Then inspection exits with 0 and one JSON result
    And the reported boundary is "<workspace>" from "flag"
    And the reported isolation is false from "flag"

  Scenario: Help ignores invalid variables
    Given environment variable MORPHIR_ISOLATED is "yes-please"
    When I request inspection help
    Then inspection help succeeds without configuration diagnostics
