Feature: F# runtime package migration
  Existing F# source and binaries retain their SDK identities when package ownership changes.

  Scenario Outline: A fresh package graph resolves one implementation
    Given the locally packed canonical F# runtime and dependency-only compatibility package
    And a fresh consumer package cache
    When unchanged consumer source installs <packages>
    Then restore and compilation succeed without dependency downgrade warnings
    And exactly one runtime assembly named Morphir.SDK is resolved
    And the compatibility package contains no runtime or reference assembly
    And the SDK and generated Order examples execute successfully

    Examples:
      | packages                       |
      | Morphir.SDK.FSharp              |
      | Morphir.SDK                     |
      | Morphir.SDK.FSharp, Morphir.SDK  |

  Scenario: A genuine pre-migration binary runs without recompilation
    Given the consumer compiled and executed against the untouched legacy package
    And its recorded pre-migration SHA-256
    When that exact binary runs with each migrated package graph's runtime
    Then the consumer binary hash is unchanged
    And Morphir.SDK.Order retains its assembly-qualified identity
    And the old consumer's public SDK calls succeed

  Scenario: Existing public declarations remain available
    Given the exported metadata captured from the untouched legacy runtime
    When the canonical package's runtime metadata is inspected
    Then every old public declaration remains available
    And generic arithmetic source still accepts its existing numeric types
    And Decimal.divide still returns decimal

  Scenario: Every admitted runtime case executes
    Given the frozen morphir-dotnet-sdk-v1 schema 1 profile dotnet-sdk-v1 corpus
    When the F# adapter executes every required case ID
    Then all 207 required IDs pass without skips or expected failures
    And eight representation exclusions are reported separately
    And callback errors and immutable operand snapshots are preserved
    And nested structural equality, hashes and ordering are checked

  Scenario: Union conflict precedence is corrected with a migration note
    Given the first dictionary contains a key with value one
    And the second dictionary contains the same key with value two
    When the dictionaries are unioned in that order
    Then the value is one
    And both input dictionaries remain unchanged
    And the migration guide describes the former second-dictionary precedence

  Scenario: Classic and v4 generation retain source compatibility
    Given the existing classic and v4 generation fixtures
    When source is generated and compiled against Morphir.SDK.FSharp
    Then the accepted model versions and generated public names remain unchanged
    And the generated source bytes remain unchanged

  Scenario: Covered integer arithmetic reports overflow
    Given signed Int32 boundary operands from the independent shared corpus
    When addition, subtraction, multiplication or negation exceeds Int32
    Then the runtime reports Overflow instead of wrapping
    And existing generic arithmetic source and public signatures remain compatible

  Scenario: Exact Decimal division has an additive optional-result API
    Given the existing Decimal.divide entry point returns decimal
    When the logical Decimal.div operation receives a zero divisor
    Then the additive optional-result runtime entry point returns Nothing
    And the existing Decimal.divide signature remains available

  Scenario: Decimal text is independent of authored scale and culture
    Given Decimal values retaining fractional zeros or a signed zero scale
    When the runtime formats the values under a non-invariant process culture
    Then text uses invariant positional notation without fractional trailing zeros
