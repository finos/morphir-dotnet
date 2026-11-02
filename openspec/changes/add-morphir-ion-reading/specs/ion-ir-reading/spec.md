# Spec Delta

## Purpose

Reads a single-file Morphir distribution written in the Ion IR format into the typed IR for its version, producing the same value the JSON reader produces for the corresponding document.

## ADDED Requirements

### Requirement: A reader accepts both single-file layouts

The reader SHALL accept a datagram, whose first value is the `morphir::` header and whose last is `morphir_footer::{}`, and a collapsed `morphir::` record. It SHALL accept any struct member order and any Ion whitespace.

#### Scenario: Canonical empty library

- **WHEN** the canonical empty v3 library datagram from the draft is read
- **THEN** the result is a classic `Library` with package `example` and no modules

#### Scenario: Collapsed record

- **WHEN** a v3 library and a v4 library are each read once as a datagram and once as the equivalent `morphir::` record, whose `dependencies` and `modules` lists hold the datagram's elements
- **THEN** the two results for each library are equal

#### Scenario: Missing footer

- **WHEN** a datagram has more than one top-level value and does not end with `morphir_footer::{}`
- **THEN** the reader returns `invalid_document`

#### Scenario: Header not first

- **WHEN** the first top-level value is not annotated `morphir`
- **THEN** the reader returns `invalid_document`

### Requirement: Versions select the reader exactly

`formatVersion` SHALL be required and SHALL be `3.0.0`, `3.1.0` or `4.0.0`. `ionVersion` SHALL match `0.1.0-draft.1` exactly, and a missing `ionVersion` SHALL mean that version.

#### Scenario: Missing format version

- **WHEN** a header has no `formatVersion`
- **THEN** the reader returns `missing_format_version`

#### Scenario: Other Ion draft

- **WHEN** a header says `ionVersion: "0.1.0-draft.2"`
- **THEN** the reader returns `unsupported_ion_version`

#### Scenario: Missing Ion version

- **WHEN** a header has `formatVersion` but no `ionVersion`
- **THEN** the reader reads the document as `0.1.0-draft.1`

#### Scenario: Unsupported format version

- **WHEN** a header says `formatVersion: "4.1.0"`
- **THEN** the reader returns `unsupported_format_version_minor`

### Requirement: Unknown header members follow the critical rule

The reader SHALL ignore a header member it does not understand unless the header's `critical` list names it.

#### Scenario: Unknown optional member

- **WHEN** a header has an unknown member `futureHint`
- **THEN** the reader ignores it and reads the document

#### Scenario: Unknown critical member

- **WHEN** a header has `futureHint` and `critical: ["futureHint"]`
- **THEN** the reader returns `unknown_critical_member` naming `futureHint`

### Requirement: The result is typed by IR version

A `3.x` document SHALL produce a classic distribution. A `4.0.0` document SHALL produce a v4 distribution. The result SHALL tell the caller which one it holds and SHALL keep the declared `formatVersion`.

#### Scenario: Distribution kinds

- **WHEN** documents of each kind the draft allows are read
- **THEN** v3 yields `Library` or `Specs`, and v4 yields `Library`, `Specs` or `Application` with its entry points in document order

#### Scenario: Specs below 3.1

- **WHEN** a header says `kind: specs` and `formatVersion: "3.0.0"`
- **THEN** the reader returns `specs_before_3_1`

### Requirement: The result matches the JSON reader

For a document and its corresponding JSON distribution, the Ion reader and the JSON reader SHALL build equal IR values.

#### Scenario: Reference pairs

- **WHEN** each vendored pair of JSON and Ion files produced by the pinned reference CLI is read
- **THEN** the two results are equal

#### Scenario: Compatibility kit spellings

- **WHEN** each Ion value in the vendored kit's `values.feature` is read
- **THEN** the result equals the value the case states

### Requirement: Type and value spellings decode to their IR nodes

The reader SHALL decode every annotation, type spelling and S-expression head in the draft. Bare strings in type position and bare scalars in value position SHALL follow the draft's shorthand rules. A string in type position that is neither a canonical FQName nor a canonical name SHALL be refused.

#### Scenario: Type shorthand

- **WHEN** a type position holds `"a"`, `"morphir/SDK:basics#int"` and `"not a name"`
- **THEN** the first is a variable, the second is a reference with no arguments, and the third returns `invalid_name`

#### Scenario: Value shorthand

- **WHEN** a v4 body holds `42`, `x`, `true` and `()`
- **THEN** these decode to an integer literal, a variable, a bool literal and a unit value

#### Scenario: Lambda shorthand

- **WHEN** a body holds `(lambda [score] score)`
- **THEN** its pattern is an `as` pattern over a wildcard binding `score`

#### Scenario: Binary apply

- **WHEN** a body holds `(apply f x y)`
- **THEN** the reader returns `invalid_node`, because apply takes exactly two operands

#### Scenario: Attributes force the expanded form

- **WHEN** a v4 body holds `(variable { source: { startLine: 1, startColumn: 1, endLine: 1, endColumn: 2 } } x)`
- **THEN** the variable `x` carries that source location

### Requirement: Literals keep their exact form

v4 literals SHALL keep arbitrary-size integers, exact decimal lexemes, retained float lexemes and document payload number lexemes. A v3 literal that the classic model cannot hold SHALL be refused and SHALL NOT be rounded.

#### Scenario: Large integer

- **WHEN** a v4 body holds `(int "99999999999999999999999999")`
- **THEN** the integer literal keeps every digit

#### Scenario: Decimal scale

- **WHEN** a v4 body holds `(decimal "10.50")`
- **THEN** the literal's lexeme is `10.50`

#### Scenario: Float lexeme

- **WHEN** a v4 body holds `(float "4e+0")`
- **THEN** the literal's value is 4 and its retained lexeme is `4e+0`

#### Scenario: Document number

- **WHEN** a document payload holds `number::"1.5e+2"`
- **THEN** the payload number's lexeme is `1.5e+2`

#### Scenario: Non-JSON Ion in a document

- **WHEN** a document payload holds an Ion timestamp, symbol or typed null
- **THEN** the reader returns `invalid_document_payload`

#### Scenario: v3 whole number out of range

- **WHEN** a v3 body holds a whole number that does not fit the classic literal
- **THEN** the reader returns `literal_out_of_range`

### Requirement: Fragments merge by the draft's rules

Dependency fragments SHALL merge in document order. A distribution's own modules SHALL NOT merge. A name defined twice after merging SHALL be refused.

#### Scenario: Dependency fragments

- **WHEN** two `package::spec` values name one package and hold different modules
- **THEN** the result has one dependency holding both modules in document order

#### Scenario: Repeated own module

- **WHEN** a `specs` document holds two `module::spec` values with the same name
- **THEN** the reader returns `duplicate_name`

#### Scenario: Repeated member after merging

- **WHEN** an inline type and a top-level type in one module have the same name
- **THEN** the reader returns `duplicate_name`

### Requirement: Nodes outside the document's version or kind are refused

The reader SHALL refuse a node that the document's version or kind does not allow, and SHALL name the node's location in the error.

#### Scenario: v4 node in a v3 document

- **WHEN** a v3 document holds `public::def::native::value`
- **THEN** the reader returns `unsupported_node` naming the node

#### Scenario: Definition in specs

- **WHEN** a v3 `specs` document holds `public::def::module`
- **THEN** the reader returns `definition_in_specs`

#### Scenario: Collapsed v3 specs record

- **WHEN** a v3 `specs` distribution is collapsed into one `morphir::` record
- **THEN** the reader returns `unsupported_node`

#### Scenario: Header package as a dependency

- **WHEN** a v3 `package::spec` names the header's own package
- **THEN** the reader returns `invalid_document`

#### Scenario: Document in a pattern

- **WHEN** a pattern in a v4 document holds `(document 1)`
- **THEN** the reader returns `invalid_node`

#### Scenario: Annotations on a definition

- **WHEN** a definition in a v4 document has an `annotations` member
- **THEN** the reader returns `invalid_node`
