@smoke @needs-built-programs
Feature: morphirup plans a setup with no ICU
  As a person who installs Morphir on a machine with no ICU library
  I want morphirup to work in invariant globalization mode
  So that morphirup gives the same plan on each machine

  Scenario: A morphirup that can load no ICU still plans the recorded digest
    When I plan the morphirup setup of the probe with no ICU
    Then the exit code is 0
