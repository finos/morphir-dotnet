@e2e @lane @published @needs-native-aot-executable
Feature: The Native AOT executable
  As a person who installs the Native AOT executable of a release
  I want one native file that needs no .NET runtime
  So that the executable starts fast from wherever I put it

  The release workflows publish the executable (PublishExecutable, or PackRelease for the native-aot offering); the
  target of the run is that executable.

  Scenario: The Native AOT executable has no dependency file of a .NET application beside it
    Then the folder of the host has no file "morphir.deps.json"
    And the folder of the host has no file "morphir.runtimeconfig.json"
