@e2e
Feature: The install script takes morphirup from the newest finos/morphir release that has it
  As a person who installs morphirup with the install script and no version
  I want the script to take the newest finos/morphir release that has morphirup for my machine
  So that a release of another tool, such as the earlier Rust CLI, does not stop the install

  # A release list is a fixture of releases of finos/morphir, in the form of the GitHub API. The script of this
  # machine runs: install-morphirup.ps1 on Windows, install-morphirup.sh elsewhere. It reads the release list of the
  # scenario, and each download goes to a proxy that refuses the connection, so no request leaves the machine and
  # nothing is installed.

  Scenario: The script skips the releases without morphirup and downloads from finos/morphir
    Given the Morphir release list "releases-morphirup-after-others.json"
    When I run the install script of this machine with no version
    Then the install script chose the release "0.5.0-alpha.1"
    And the install script downloads morphirup from "https://github.com/finos/morphir/releases/download/v0.5.0-alpha.1/"
    And the install script installed nothing

  Scenario: The script refuses when no finos/morphir release has morphirup
    Given the Morphir release list "releases-without-morphirup.json"
    When I run the install script of this machine with no version
    Then the exit code is 1
    And the install script says "no release of finos/morphir has morphirup-"
    And the install script installed nothing
