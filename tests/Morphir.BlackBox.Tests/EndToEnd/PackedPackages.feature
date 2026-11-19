@e2e @lane @packages
Feature: The packages that the build packs
  The pack step of the build (PackAll) packs the libraries, the Myriad plugins, the F# extensions and the tool. These
  scenarios read the packages from the folder that the build gives, as a user gets them.

  Scenario: Each Morphir assembly of the tool package has the assembly version of the changelog
    Given the packed package "Morphir.Tool"
    Then each Morphir assembly of the package below "tools/" has the assembly version of the changelog

  Scenario: Each reference between the Morphir assemblies of the tool package has the assembly version of the changelog
    Given the packed package "Morphir.Tool"
    Then each reference between the Morphir assemblies of the package below "tools/" has the assembly version of the changelog

  Scenario: Each Morphir dependency of a packed package is packed at its version
    Then each Morphir dependency of each packed package is packed at its version

  Scenario: The library packages and the tool package have one version
    Then the packed packages "Morphir.Core, Morphir.Tooling, Morphir.Tool, Morphir.SDK" have one version

  Scenario: The version of the tool package is the version of the changelog
    Given the packed package "Morphir.Tool"
    Then the version of the package is the version of the changelog

  Scenario: The tool package has the metadata of a dotnet tool of FINOS
    Given the packed package "Morphir.Tool"
    Then the manifest of the package has the id "Morphir.Tool"
    And the authors of the package include "FINOS"
    And the package has a license
    And the project URL of the package contains "morphir-dotnet"
    And the package type of the package is "DotnetTool"

  Scenario: The library packages and the tool package have release notes with sections
    Then each of the packed packages "Morphir.Core, Morphir.Tooling, Morphir.Tool, Morphir.SDK" has release notes that contain "###"

  Scenario: The tool package has the layout of a dotnet tool
    Given the packed package "Morphir.Tool"
    Then the package has an entry below "tools/net11.0/any/"
    And the package has the entries:
      | entry                                    |
      | tools/net11.0/any/dotnet-morphir.dll     |
      | tools/net11.0/any/DotnetToolSettings.xml |
    And the package has an entry that contains "Morphir.Core.dll"
    And the package has an entry that contains "Morphir.Tooling.dll"

  Scenario: The tool package has the managed composition and not the AOT-compatible entry point
    Given the packed package "Morphir.Tool"
    Then the package has the entries:
      | entry                                         |
      | tools/net11.0/any/Morphir.Tooling.Managed.dll |
      | tools/net11.0/any/Morphir.Frontend.FSharp.dll |
      | tools/net11.0/any/FSharp.Compiler.Service.dll |
    And the package has no entry with the file name "morphir.dll"

  Scenario: The tool settings name the command dotnet-morphir and its entry point
    Given the packed package "Morphir.Tool"
    Then the entry "tools/net11.0/any/DotnetToolSettings.xml" of the package contains the text:
      """
      <Command Name="dotnet-morphir"
      """
    And the entry "tools/net11.0/any/DotnetToolSettings.xml" of the package contains the text:
      """
      EntryPoint="dotnet-morphir.dll"
      """

  Scenario: A library package has the layout of a library
    Given the packed package "Morphir.Core"
    Then the package has an entry below "lib/net10.0/"
    And the package has an entry that contains "lib/net10.0/Morphir.Core.dll"
    And the package has no entry below "tools/"

  Scenario: The packages have no symbol file and no test file
    Then the packed packages "Morphir.Core, Morphir.Tooling, Morphir.Tool" have no entry that ends with ".pdb"
    And the packed packages "Morphir.Core, Morphir.Tooling, Morphir.Tool" have no entry that contains "Tests"

  Scenario: The System.Text.Json plugin package has the host, the plugin, the task and the build assets
    Given the packed package "Morphir.Myriad.SystemTextJson"
    Then the package has the entries:
      | entry                                                  |
      | tools/host/Myriad.dll                                  |
      | tools/host/FSharp.Core.dll                             |
      | tools/plugin/Morphir.Myriad.SystemTextJson.dll         |
      | tasks/net10.0/Morphir.Myriad.SystemTextJson.Build.dll  |
      | tasks/net10.0/Morphir.Myriad.Hosting.dll               |
      | build/Morphir.Myriad.SystemTextJson.props              |
      | build/Morphir.Myriad.SystemTextJson.targets            |
      | README.md                                              |

  Scenario: The annotations package has its library and its README
    Given the packed package "Morphir.Myriad.SystemTextJson.Annotations"
    Then the package has the entries:
      | entry                                                              |
      | lib/netstandard2.0/Morphir.Myriad.SystemTextJson.Annotations.dll   |
      | README.md                                                          |
