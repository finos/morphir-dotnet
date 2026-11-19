@integration @needs-development-packages
Feature: Generate System.Text.Json contexts in an F# consumer of the plugin packages
  The System.Text.Json plugin (Morphir.Myriad.SystemTextJson) generates a JSON context for the F# types of a file
  (an FSharpJsonContext item) when the project builds. Its attributes are in Morphir.Myriad.SystemTextJson.Annotations.
  The test packs both packages from the development build, at one version, into the feed of its workspace.

  Background:
    Given the packages "Morphir.Myriad.SystemTextJson, Morphir.Myriad.SystemTextJson.Annotations" packed from the development build

  Scenario: A clean consumer with an empty package folder builds and runs a context
    Given a System.Text.Json consumer
    When I build and run the consumer
    Then the consumer has 1 generated file "AppJson.g.fs"
    And the consumer prints:
      """
      {"Name":"Ada","Age":36}
      {"kind":"Circle","radius":1.5}
      """

  Scenario: A repeat build does not write the generated context again
    Given a System.Text.Json consumer
    When I build the consumer
    Then the consumer has 1 generated file "AppJson.g.fs"
    Given I remember the generated files of the consumer
    And I wait 1100 milliseconds
    When I build the consumer
    Then the generated files of the consumer are the same as I remembered

  Scenario: One project uses the Myriad plugin and the System.Text.Json plugin together
    Given the packages "Morphir.Myriad" packed from the development build
    And a System.Text.Json consumer
    And the consumer also uses the Myriad plugin with the model fixture "Models/daily-rate-v3.json"
    And the consumer program also prints the daily rate of "Inventory"
    When I build and run the consumer
    Then the first line that the consumer prints is:
      """
      {"Name":"Ada","Age":36}
      """
    And the consumer prints 3 lines

  Scenario: Wire defaults, extension data and migration work from the packed packages
    Given a System.Text.Json consumer
    And the context of the consumer has the metadata:
      | metadata       | value                         |
      | Naming         | CamelCase                     |
      | UnionEncoding  | Default, UnwrapFieldlessTags  |
      | UnionTagNaming | CamelCase                     |
    And the consumer types are the fixture "SystemTextJson/WireRules.fs"
    And the consumer program is the fixture "SystemTextJson/WireRulesProgram.fs"
    When I build and run the consumer
    Then the consumer prints:
      """
      {"cancellation":false}
      {"message":"test","kind":"unspecified"}
      {"amount":12.50,"items":[1]}
      {"claimsVersion":"0.1.0-draft.2","critical":["claimsVersion"]}
      {"known":true,"future":{"items":[1,null]}}
      """

  Scenario: A context that the generator refuses blocks compilation
    Given a System.Text.Json consumer
    And the context of the consumer has the name "Not A Name"
    When I build the consumer and it fails
    Then the output contains "Not A Name"
    And the consumer has no file "bin/Release/net10.0/Consumer.dll"

  Scenario: A type that the generator cannot encode is a build error at its line and column
    Given a System.Text.Json consumer
    And the consumer types are:
      """
      namespace Consumer

      type Page<'T> = { Items: 'T list }
      """
    And the consumer program is:
      """
      printfn "unused"
      """
    When I build the consumer and it fails
    Then the output contains "Types.fs(3,"
    And the output contains "error MSTJ0010: Page: generic types are not supported"

  Scenario Outline: The compiler checks the methods and the return types of a default value provider
    Given a System.Text.Json consumer
    And the consumer types are:
      """
      namespace Consumer
      open Morphir.Myriad.SystemTextJson
      type Defaults() = <method>
      type Payment = { [<JsonDefaultValueProvider(typeof<Defaults>, "<name>")>] Amount: decimal }
      """
    And the consumer program is:
      """
      printfn "unused"
      """
    When I build the consumer and it fails
    Then the output contains "<expected>"

    Examples:
      | method                                                 | name    | expected   |
      | static member Create() : string = "wrong type"         | Create  | decimal    |
      | static member Create(value: int) : decimal = decimal value | Create | unit     |
      | member _.Create() : decimal = 1M                       | Create  | static     |
      | static member private Create() : decimal = 1M          | Create  | accessible |
      | static member Other() : decimal = 1M                   | Missing | Missing    |
