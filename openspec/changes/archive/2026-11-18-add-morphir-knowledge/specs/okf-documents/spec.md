# Spec Delta

## Purpose

Expose the complete structure and original text of an OKF Markdown file so validation, navigation and annotation can address the same source.

## ADDED Requirements

### Requirement: A document has a unified syntax and semantic root

Reading a UTF-8 document SHALL expose one CST and one AST with a shared source. Both roots SHALL encompass YAML frontmatter when present and the Markdown body. The AST SHALL expose YAML collections, keys and values and the Markdown block and inline structure, including CommonMark, GFM tables and footnotes. Unknown frontmatter fields SHALL remain inspectable at every level.

#### Scenario: YAML and Markdown are in one document
- **WHEN** a document has nested YAML extension metadata, a Markdown table and a footnote
- **THEN** a caller can inspect the nested fields, table cells and footnote labels beneath the same document root in the AST

### Requirement: Concrete syntax preserves the source

CST leaves SHALL cover the original source in order, including delimiters, punctuation, whitespace, comments, reference definitions and malformed regions. Reconstructing an unedited document from the CST SHALL preserve its original UTF-8 bytes.

#### Scenario: Source-preserving traversal
- **WHEN** an input file has quoted YAML, comments, mixed CRLF and LF, Unicode, an escaped table pipe, an alignment row and an unused footnote
- **THEN** visitors can reach each construct in source order and writing the untouched CST yields identical bytes

#### Scenario: A table ends at EOF
- **WHEN** a header-only GFM table ends with its alignment row and has no final newline
- **THEN** the alignment row is traversable and emission preserves the missing final newline

### Requirement: AST and CST share cursor operations

Each tree SHALL support a cursor with its current node, source range where available, parent, first child and adjacent siblings. A depth-first pre-order visitor SHALL stay within the starting subtree and honor requests to skip children or stop. Parsed ranges SHALL use ordered half-open UTF-16 offsets in the original document.

#### Scenario: Walk frontmatter and body
- **WHEN** a visitor starts at the document root and skips a nested YAML mapping
- **THEN** it sees the mapping, omits its children and continues into the Markdown body with whole-document offsets

#### Scenario: Stop a visit
- **WHEN** a visitor stops on a heading inside a subtree
- **THEN** it receives no further callbacks, including for siblings outside that subtree

### Requirement: Parsing retains recoverable documents

Parsing SHALL return source-located diagnostics for syntax errors when a location exists. Malformed YAML inside recognized fences SHALL remain as an error region in the CST and an invalid frontmatter state in the AST. Parsing SHALL still expose the Markdown body. Undecodable UTF-8 SHALL yield a typed failure.

#### Scenario: Recover inside a fenced header
- **WHEN** a fenced YAML block contains an unclosed sequence and the Markdown body starts with a heading
- **THEN** the result exposes the YAML fault, retains the original header and lets a visitor reach the heading

#### Scenario: Reject invalid UTF-8
- **WHEN** an input contains an invalid UTF-8 byte sequence
- **THEN** the parser returns an encoding diagnostic and no decoded document
