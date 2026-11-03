Feature: Reusable typed C# library generation
  A host supplies a decoded Morphir library without invoking the CLI.

  Scenario: An empty library succeeds
    Given a supported classic or v4 Library with no modules
    When the in-memory generator runs
    Then it succeeds with zero sources and preserves the family and source version

  Scenario: A modern compiler host adds generated source
    Given a typed library exporting an integer constant
    When a test-only .NET 10 generator driver supplies the model
    Then AddSource contributes a compilable source with the expected constant
    And the generator core depends on Models without Tooling, Ion or Roslyn

  Scenario: Caller cancellation
    Given a cancelled caller token
    When the in-memory generator runs
    Then it returns cancellation with no sources

  Scenario: Function values preserve lexical scope and evaluation order
    Given a generic identity, curried top-level functions and captured local lets
    When a compiled consumer supplies an observable callback and shadowed locals
    Then arguments execute once in order and closures retain their original binding
    And partial application and returned nested functions retain their concrete types

  Scenario: Ordered patterns and persistent records
    Given constructor, tuple, list, wildcard, literal and alias patterns
    When the compiled consumer evaluates ordered branches and recursive list sum
    Then the first matching branch executes with correctly typed bindings
    And record construction and updates evaluate each input once in source order
    And the original record remains unchanged

  Scenario Outline: The shared behavior corpus
    Given an admitted <family> library
    When it is generated for numeric C# <language>
    Then a real compiled consumer observes the independent expected behavior
    Examples:
      | family  | language |
      | classic | 14       |
      | v4      | 14       |
      | classic | 15       |
      | v4      | 15       |

  Scenario: Whole-model refusal
    Given an unsupported declaration after a valid module
    When generation runs
    Then a located rule diagnostic is returned without any sources

  Scenario: Immutable generic types retain SDK meaning
    Given equivalent classic and v4 aliases, generic records and custom cases
    When both numeric compiler profiles compile and execute the type consumer
    Then payloads are get-only and Result has error first and value second
    And a separate consumer cannot access private constructors

  Scenario: Structural equality uses the SDK protocol
    Given independently allocated nested records, lists and custom cases
    When the compiled consumer compares typed and boxed values and hashes
    Then every payload follows Morphir equality including NaN and signed zero
    And decimal scale does not change equality or hashing

  Scenario: Transparent aliases preserve structural visibility
    Given a public alias of a private closed record
    When the compiled consumer constructs the public alias
    Then a public immutable structural representation is available
    But a public payload exposing a private nominal type fails generation
