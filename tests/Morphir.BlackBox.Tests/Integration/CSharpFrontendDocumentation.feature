@integration @host-native @needs-compilation
Feature: The documented C# compile runs as written on the example of the C# frontend page
  The section "Compile" of docs/content/docs/frontends/csharp/_index.md compiles the example of the section
  "Modules", the Acme.Trading domain with its types and its static class, saved as Domain.cs. The command runs as
  written in the directory of the example and writes the path of the published IR.

  Scenario: The documented compile of the example writes the published IR
    Given the section "Compile" of the documentation page "frontends/csharp/_index.md"
    Then the page documents these commands
      | command                                                                                        |
      | morphir compile --language csharp --input Domain.cs --package-name acme/trading --ir-version 4 |
    Given the first C# code block of the documentation page "frontends/csharp/_index.md" as the file "trading/Domain.cs"
    When I run the documented commands of the section in the directory "trading"
    Then the documented compile writes the path of the published IR of the project "trading"
