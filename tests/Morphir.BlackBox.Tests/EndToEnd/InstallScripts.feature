@e2e
Feature: The install script takes morphirup from the newest finos/morphir release that has it
  As a person who installs morphirup with the install script
  I want the script to take the newest finos/morphir release that has morphirup for my machine
  So that a release of another tool, such as the earlier Rust CLI, does not stop the install

  # The script of this machine runs: install-morphirup.ps1 on Windows, install-morphirup.sh elsewhere. A release list
  # is a fixture in the form of the GitHub API, and a release feed one in the form of releases.atom. A release mirror
  # runs on a local port and answers 404 for each file it does not have. Any other request goes to a proxy that
  # refuses the connection, so no request leaves the machine.

  Scenario: The script skips the releases without morphirup and downloads from finos/morphir
    Given the Morphir release list "releases-morphirup-after-others.json"
    When I run the install script of this machine with no version
    Then the install script chose the release "0.5.0-alpha.2"
    And the install script downloads morphirup from "https://github.com/finos/morphir/releases/download/v0.5.0-alpha.2/"
    And the install script installed nothing

  Scenario: The script refuses when no finos/morphir release has morphirup
    Given the Morphir release list "releases-without-morphirup.json"
    When I run the install script of this machine with no version
    Then the exit code is 1
    And the install script says "no release of finos/morphir has morphirup-"
    And the install script installed nothing

  Scenario: With no release list, the script reads the release feed and skips the Rust tags
    Given the Morphir release feed "releases-rust-tags-first.atom"
    And a release mirror with morphirup "0.5.0-alpha.2"
    When I run the install script of this machine with no version
    Then the install script chose the release "0.5.0-alpha.2"
    And the mirror was asked for the morphirup of "v0.4.0-beta.12"
    And the mirror was asked for the morphirup of "v0.4.0-beta.11"
    And the install script downloads morphirup "0.5.0-alpha.2" from the mirror
    And the install script installed morphirup

  Scenario: A version whose finos/morphir release has no morphirup is refused
    Given a release mirror with no morphirup
    When I run the install script of this machine for the version "0.4.0-beta.12"
    Then the exit code is 1
    And the install script says "finos/morphir release v0.4.0-beta.12 has no morphirup-"
    And the install script says "morphirup releases before 0.5.0-alpha.2 are on finos/morphir-dotnet"
    And the install script installed nothing
