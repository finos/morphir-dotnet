# Spec Delta

## MODIFIED Requirements

### Requirement: Structural equality and hashing

Supported SDK values SHALL compare by Morphir value semantics, including nested collections, records and variants. Plain C# records and plain C# unions SHALL be supported values and SHALL compare with their own equality. Equal hashable values SHALL have equal hashes independent of insertion history. Unsupported ordering domains SHALL fail explicitly. The SDK SHALL NOT use reflection to find the kind of a value, so equality and hashing work under Native AOT.

#### Scenario: Independent equal structures

- **WHEN** two independently constructed values contain equal nested immutable collections
- **THEN** value equality succeeds and their hashes agree

#### Scenario: Distinct keys and values

- **WHEN** two dictionaries have the same keys with a different value at one key
- **THEN** they compare unequal while preserving the selected key ordering

#### Scenario: Plain record in a collection

- **WHEN** two lists hold equal plain records that do not implement the Morphir equality interface
- **THEN** value equality succeeds and their hashes agree

#### Scenario: Plain union

- **WHEN** two plain unions hold the same case and value
- **THEN** value equality succeeds, and two unions with different cases are not equal

#### Scenario: Plain record under Native AOT

- **WHEN** a Native AOT program compares two equal plain records, two lists of plain records, and two tuples that hold a plain record
- **THEN** each comparison succeeds and no `NotSupportedException` is thrown

#### Scenario: Function-typed field

- **WHEN** code that people write compares two values that hold the same delegate
- **THEN** the SDK compares the delegates by reference, as .NET does; generated code never reaches this case, because the backend refuses equality on a type that holds a function

## ADDED Requirements

### Requirement: Generic numeric and comparable operations

The SDK SHALL give generic overloads of the numeric operations of `Basics` for `T : INumber<T>`, with checked arithmetic, and `List<T>` SHALL implement `IComparable<List<T>>` with the Morphir order.

#### Scenario: Checked generic addition

- **WHEN** `Basics.Add<int>` adds two values whose sum is outside Int32
- **THEN** it throws `OverflowException`, as `Basics.Add(int, int)` does
