# Spec Delta

## Purpose

Reads Amazon Ion 1.0 text and binary into immutable values that lose nothing the Ion data model defines, so Morphir formats can be decoded and unknown data can be kept intact.

## ADDED Requirements

### Requirement: Text and binary produce the same values

The reader SHALL accept Ion 1.0 text and binary from bytes or a re-openable stream, and SHALL detect binary by the marker `E0 01 00 EA`. Text and binary spellings of one Ion value SHALL produce equal values.

#### Scenario: Reference fixture in both encodings

- **WHEN** `rich-model.ionb` and its text companion `rich-model.ion` are read
- **THEN** the two results are equal

#### Scenario: Unsupported Ion version

- **WHEN** the input starts with the text marker `$ion_1_1` or a binary marker other than `E0 01 00 EA`
- **THEN** the reader returns `unsupported_ion_version`

### Requirement: Values keep every Ion distinction

A value SHALL keep its type, its ordered annotations, integers of any size, decimal coefficient and exponent including negative zero, timestamp precision, typed nulls, and the difference between a symbol and a string.

#### Scenario: Numeric exactness

- **WHEN** the input holds `9007199254740993`, `18446744073709551616000001`, `123.4500` and `-0d0`
- **THEN** the integers keep every digit, `123.4500` keeps its four fraction digits, and `-0d0` stays negative zero

#### Scenario: Ordered annotations

- **WHEN** the input holds `source_name::target_name::{}`
- **THEN** the value carries the annotations in that order

#### Scenario: Timestamp precision

- **WHEN** the input holds `2026-10-01T01:02:03.004-00:00` and `2026-10-01T01:02:03-00:00`
- **THEN** the first keeps three fraction digits, the second keeps none, and the values are not equal

#### Scenario: Symbol and string stay distinct

- **WHEN** the input holds `abc` and `"abc"`
- **THEN** the first is a symbol, the second is a string, and the values are not equal

### Requirement: Structs keep order and repeated fields

A struct value SHALL keep its fields in document order and SHALL keep repeated field names. Formats built on this reader decide whether a repeat is an error.

#### Scenario: Repeated field

- **WHEN** the input holds `{duplicate:1,duplicate:2}`
- **THEN** the struct has two fields named `duplicate`, in that order

### Requirement: Reading is bounded

The reader SHALL apply a decoded-byte limit before parsing starts, and depth and node limits during parsing. The root counts as depth one. Containers and scalars count as nodes. Annotations and field names do not.

#### Scenario: Byte limit

- **WHEN** the input is longer than the byte limit
- **THEN** the reader returns `decoded_limit_exceeded` without parsing any of it

#### Scenario: Depth limit

- **WHEN** the input nests 5000 lists and the depth limit is 512
- **THEN** the reader returns `depth_limit_exceeded` and does not overflow the stack

#### Scenario: Node limit

- **WHEN** the input holds more values than the node limit
- **THEN** the reader returns `node_limit_exceeded`

### Requirement: Malformed input returns a typed error

The reader SHALL return a typed error for malformed input and SHALL NOT raise an exception to the caller. A value read before the failure SHALL NOT be returned as a partial result.

#### Scenario: Truncated text

- **WHEN** the input is `morphir::{ ionVersion: "x"`
- **THEN** the reader returns `malformed_ion` and no value

#### Scenario: Truncated binary

- **WHEN** the input is the first 100 bytes of `rich-model.ionb`
- **THEN** the reader returns `malformed_ion` and no value

#### Scenario: Trailing bytes

- **WHEN** valid binary is followed by bytes that are not Ion
- **THEN** the reader returns `malformed_ion` and no value

#### Scenario: List items without commas

- **WHEN** the input is `[a b]`
- **THEN** the reader returns `malformed_ion`, because Ion requires commas between list items

#### Scenario: Invalid UTF-8 in binary

- **WHEN** a binary string holds bytes that are not valid UTF-8
- **THEN** the reader returns `malformed_ion` and does not substitute replacement characters

#### Scenario: Unknown symbol id

- **WHEN** the input holds `$99` and no symbol table defines it
- **THEN** the reader returns `malformed_ion`
