@smoke @lane @native-aot @needs-built-programs
Feature: Each probe and each library host gives the same output with Native AOT
  As a developer who publishes an application that uses a Morphir library with Native AOT
  I want each library and each generated serializer to work after a Native AOT publish
  So that a trim or AOT defect is found before a release

  # The F# compiler does not run the trim analyzers, so only a publish finds an AOT problem in F# code. The two
  # summary warnings of FSharp.Core are in each F# program that is published with Native AOT.
  Scenario Outline: A program published with Native AOT writes the output of the built program
    When I run the program "<program>"
    Then the exit code is 0
    When I publish the program with Native AOT
    Then the publish has no trim or AOT warning except the summary warnings of "<allowed>"
    When I run the published program
    Then the exit code is 0
    And the published program writes the same output as the built program

    Examples:
      | program                        | allowed     |
      | myriad-json-probe              | FSharp.Core |
      | mep-probe                      | FSharp.Core |
      | morphirup                      | FSharp.Core |
      | compilation-smoke              | FSharp.Core |
      | configuration-io-smoke         | FSharp.Core |
      | configuration-inspection-smoke | FSharp.Core |
      | configuration-microsoft-smoke  | FSharp.Core |
      | conformance-smoke              | FSharp.Core |
