@smoke @needs-built-programs
Feature: Each host of a shipped library runs as a managed program
  As a developer who uses a Morphir library in an application
  I want each library that the CLI does not reach to run in a small host program
  So that I know that the library starts and does its basic functions outside the CLI

  Scenario Outline: A host that writes one JSON result reports success
    When I run the program "<host>"
    Then the exit code is 0
    And the JSON document on standard output has the field "success" with the value true

    Examples:
      | host                           |
      | configuration-io-smoke         |
      | configuration-microsoft-smoke  |
      | configuration-inspection-smoke |

  Scenario: The host of the compilation library passes each of its checks
    When I run the program "compilation-smoke"
    Then the exit code is 0
    And the output contains "ok   diagnostic text"

  # The netstandard2.0 build of Morphir.Files is what a compiler or MSBuild host loads through Morphir.Models; the CLI
  # runs the net11.0 build. The host reads files, content and a property, uses the tree as an IFileProvider, and
  # publishes the tree three times, the last time while another handle holds an output file open for a short time.
  # The solution build builds both with warnings as errors. A Native AOT application compiles the net11.0 build, so
  # this host has no row in NativeAot.feature.
  Scenario: The host of the netstandard2.0 virtual file library passes each of its checks
    When I run the program "files-netstandard-smoke"
    Then the exit code is 0
    And the output contains "ok   the netstandard2.0 assembly"
    And the output contains "ok   the third publication waits for a short-lived handle, then replaces the file"
    And the output does not contain "FAIL"

  Scenario: The host of the compatibility kit writes one report of the kit
    When I run the program "conformance-smoke"
    Then the exit code is 0
    And standard output is one JSON document

  Scenario: The diagram library renders an offline player and carries metadata in a C# program
    When I run the program "diagrams-smoke"
    Then the exit code is 0
    And the output contains "<svg"
    And the output contains "Download SVG"
    And the output contains "metadata round trip: ok"
    And the output contains "bpmn render: ok"
