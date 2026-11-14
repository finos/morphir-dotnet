@integration @needs-extensions
Feature: Install an extension from a NuGet feed
  As an extension author in .NET
  I want users to install my extension from the NuGet feeds they already use
  So that I publish one package and do not run a separate release server

  Scenario: An extension installs from a NuGet folder feed and records its package
    Given a NuGet folder feed with version "1.2.3" of the extension package "Acme.Morphir.Python"
    When I install the extension "nuget:Acme.Morphir.Python@1.2.3" from the NuGet folder feed
    Then the exit code is 0
    And the output contains "Installed com.acme.python 1.2.3"
    And the extension home records the NuGet package "acme.morphir.python" version "1.2.3"

  Scenario: A dry run from a NuGet folder feed installs nothing
    Given a NuGet folder feed with version "1.2.3" of the extension package "Acme.Morphir.Python"
    When I plan the installation of the extension "nuget:Acme.Morphir.Python" from the NuGet folder feed
    Then the exit code is 0
    And the output contains "Planned installation of com.acme.python 1.2.3"
    And the extension home has no installed extension

  Scenario: A package that no source has fails and names the source
    Given a NuGet folder feed with version "1.2.3" of the extension package "Acme.Morphir.Python"
    When I install the extension "nuget:Acme.Morphir.Missing@1.0.0" from the NuGet folder feed
    Then the exit code is 1
    And the output contains "extension.nuget_not_found"
    And the extension home has no installed extension
