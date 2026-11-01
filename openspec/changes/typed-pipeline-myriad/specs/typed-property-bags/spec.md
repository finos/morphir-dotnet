# Spec Delta

## Purpose

Gives files, trees, configuration and processors a place to hold extra data where every value's type is known at compile time, without callers handling untyped values.

## ADDED Requirements

### Requirement: Typed properties

A property SHALL be a named object that carries its value type. Values SHALL only be stored and read through a property, and storing a value of the wrong type SHALL be a compile error.

#### Scenario: Typed read

- **WHEN** an integer property is set to 3 and read back
- **THEN** the read returns the integer 3 with no cast by the caller

#### Scenario: Wrong value type

- **WHEN** code stores a string through an integer property
- **THEN** the code does not compile

#### Scenario: Missing value

- **WHEN** a property with no stored value is read
- **THEN** the read reports no value without raising an exception

### Requirement: Property identity

Two properties SHALL refer to the same stored value only when both their name and value type match.

#### Scenario: Same name, different type

- **WHEN** a value is stored through a string property and read through an integer property with the same name
- **THEN** the read reports no value and raises no exception

#### Scenario: Separately created property

- **WHEN** two properties are created separately with the same name and value type
- **THEN** a value stored through one can be read through the other

### Requirement: Construction from typed values

A bag SHALL be constructible from a sequence of property values, where each value is checked against its property at compile time. When a property appears more than once, the last value SHALL win.

#### Scenario: Mixed value types

- **WHEN** a bag is built from an integer property value and a string property value
- **THEN** both values can be read back with their own types

#### Scenario: Repeated property

- **WHEN** a bag is built with two values for one property
- **THEN** reading that property returns the second value

### Requirement: Pipeline-friendly functions

Every bag function SHALL take the bag as its last parameter, so that calls compose with `|>`, `<|` and `>>`. Functions SHALL NOT change the bag they are given.

#### Scenario: Forward pipeline

- **WHEN** two values are set by piping an empty bag through two set calls
- **THEN** the resulting bag contains both values

#### Scenario: Composed step

- **WHEN** two set calls are composed with `>>` and applied to a bag
- **THEN** the resulting bag contains both values

#### Scenario: Original unchanged

- **WHEN** a value is set or removed
- **THEN** the original bag is unchanged

### Requirement: One bag model across owners

Files, trees, tree configuration and processors SHALL all use the property bag for their extra data. Each owner SHALL offer matching set, read and remove functions that take the owner last. Pipeline state such as whether a processor stays mutable SHALL NOT be stored in a bag.

#### Scenario: Setting data on a file

- **WHEN** a property is set by piping a file into the file's set function
- **THEN** the file's read function returns the value

#### Scenario: Bags compare by content

- **WHEN** two files have equal fields and bags holding equal values
- **THEN** the files compare as equal
