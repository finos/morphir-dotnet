# Spec Delta

## Purpose

Lets any Morphir file, a single-file model or one file of a document tree, carry a file-local naming table that interns long names, under the parent repository's `ir/name-compaction` contract, and reads and writes such files exactly, so a compacted model is smaller and still means the same model.

A compacted file wraps its document in an envelope identified by a schema URL:

```json
{
  "$schema": "https://morphir.finos.org/schemas/ir-context/0.1.0-draft.1/document.schema.json",
  "context": { "fqNames": { "a": "my-org/my-project:crm/contact-details#postal-address" } },
  "document": { "formatVersion": 4, "billingAddress": { "$name": "a" } }
}
```

The URL above is illustrative; the parent contract fixes it. This capability covers whole-file reading and writing. The parent's bounded-reading requirements (resolving one name without building the document, a context placed after the document in bounded memory, byte pass-through of unknown optional members) belong to bounded model access.

## ADDED Requirements

### Requirement: A compacted file is identified by its schema URL
A file SHALL be read as compacted only when its top-level `$schema` is a schema URL that this system declares it reads. A file without `$schema` SHALL be read as plain. A file with an undeclared schema URL SHALL be refused with a diagnostic naming the URL.

#### Scenario: An unknown envelope is refused
- **WHEN** a file's `$schema` names a context schema version this system does not declare
- **THEN** reading is refused with a diagnostic that names the file and the URL

### Requirement: Every Morphir file kind can be compacted
The envelope SHALL be accepted for single-file models and for every tree file kind (manifest, module, type and value), for IR 3.1.0 and IR 4. The enclosed document SHALL keep its own `formatVersion` and kind; expansion SHALL NOT relabel a v3 document as v4 or change its version.

#### Scenario: A compacted v3 single file expands to v3
- **WHEN** a compacted single-file v3 `Library` is expanded
- **THEN** the result is a plain v3 single file with `"formatVersion": 3` and every attribute slot preserved

#### Scenario: A compacted tree value file expands in place
- **WHEN** a v4 tree's value file is compacted and the tree is read
- **THEN** the model equals the one read from the plain tree

### Requirement: Aliases resolve only at typed name positions
A `{"$name": "<id>"}` reference SHALL be resolved only at positions the IR schema defines as names, paths or fully qualified names. The same text inside a string literal, documentation or any other value SHALL stay as written. A classic v3 FQName slot SHALL expand to classic package, module and name arrays; a v4 slot SHALL expand to the canonical FQName string.

#### Scenario: A literal that looks like an alias stays a literal
- **WHEN** a compacted value holds the string literal `{"$name":"a"}` and alias `a` is defined
- **THEN** the expanded value holds the same string literal, unchanged

#### Scenario: Classic and v4 slots expand differently
- **WHEN** one alias is used in a classic v3 FQName slot and in a v4 FQName slot of two files
- **THEN** the v3 file expands to arrays and the v4 file to the canonical string

### Requirement: An invalid context is refused
Reading SHALL refuse, with a diagnostic naming the file and the rule, an unknown alias, a duplicate table entry, an unknown `critical` path, a table with more entries than the limit, an expanded name longer than the limit, and a context placed after the document. Context bytes SHALL count toward the input's size limits. A refused file SHALL give no document.

#### Scenario: Unknown alias
- **WHEN** a document references `{"$name":"zz"}` and no table defines `zz`
- **THEN** reading is refused with a diagnostic naming the file and `zz`

#### Scenario: Context after the document
- **WHEN** a compacted file's `document` member comes before its `context` member
- **THEN** reading is refused with a diagnostic that names the file and says the context must come first

### Requirement: Compaction is lossless
For every file the system writes compacted, expanding it SHALL give a document equal to the plain document it was written from.

#### Scenario: Expand after compact
- **WHEN** each fixture model, single file and tree, is compacted and then expanded
- **THEN** each expanded document equals the original

### Requirement: Compacted files are written under a named policy
Writing a compacted file SHALL follow one named writer policy that the parent contract defines, and the result SHALL name the policy. A name SHALL get an alias only when that makes the file smaller after the table's own size, so a name used once stays spelled out. Alias ids, table order and tie breaks SHALL follow the policy.

#### Scenario: A name used once stays spelled out
- **WHEN** a v4 file uses a fully qualified name once, at a string position, and the policy is applied
- **THEN** the written file spells that name in place and its table has no entry for it

#### Scenario: Compaction that would be a loss is not applied
- **WHEN** no name in a file saves more than its table entry costs
- **THEN** the written file is the plain document in canonical JSON, with no envelope, and the result says no alias was worth its cost

### Requirement: Compacted output has one canonical byte form
A compacted file SHALL be written in RFC 8785 canonical JSON. Every size comparison of the policy SHALL use the UTF-8 byte length of that form. Writing the same document twice SHALL give identical bytes, and the bytes SHALL equal the parent contract's fixed expected bytes for each of its cases.

#### Scenario: Non-ASCII content is measured as UTF-8
- **WHEN** a file holds a documentation string with `é` and the policy weighs the file's size
- **THEN** `é` is counted as two bytes, and the written file holds `é` unescaped

#### Scenario: The parent's fixed cases match byte for byte
- **WHEN** each fixed case of the parent policy is written
- **THEN** the output bytes equal the case's expected bytes
