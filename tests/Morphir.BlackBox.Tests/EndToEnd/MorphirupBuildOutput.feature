@e2e @needs-built-programs
Feature: morphirup needs no globalization library, no platform cryptography and no regular expressions
  As a person who installs Morphir on a small machine
  I want morphirup to need no ICU, no OpenSSL and no regular expression engine
  So that morphirup runs on a machine that does not have those libraries

  Background:
    Given the build output of the program "morphirup"

  Scenario: morphirup runs in invariant globalization mode
    Then the runtime configuration "morphirup.runtimeconfig.json" sets "System.Globalization.Invariant" to true

  Scenario Outline: An assembly of morphirup calls no Unicode normalization of .NET
    Then the assembly "<assembly>" references no member "Normalize" of "System.String"
    And the assembly "<assembly>" references no member "IsNormalized" of "System.String"

    Examples:
      | assembly              |
      | Morphir.Toolchain.dll |
      | morphirup.dll         |

  # On Linux, System.Security.Cryptography calls the OpenSSL of the machine, and a machine without libssl stops the
  # process. SHA-256 comes from a managed digest instead.
  Scenario Outline: An assembly of morphirup uses no cryptography library of the platform
    Then the assembly "<assembly>" references no assembly "System.Security.Cryptography" and no type of its namespace

    Examples:
      | assembly              |
      | Morphir.Toolchain.dll |
      | morphirup.dll         |

  # The $ of .NET also matches before a final line feed, so an anchored pattern accepted "<128 hex>\n" (review of
  # #581, T1). Spellings are checked character by character instead.
  Scenario Outline: An assembly of morphirup uses no regular expressions
    Then the assembly "<assembly>" uses no type of the namespace "System.Text.RegularExpressions"

    Examples:
      | assembly              |
      | Morphir.Toolchain.dll |
      | morphirup.dll         |
