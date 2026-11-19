# Spec Delta

## Purpose

Let SVG documents written by `Morphir.Svg` carry namespaced foreign content, such as RDF/XML, in a `<metadata>` element, and let callers extract that content safely from SVG input.

## ADDED Requirements

### Requirement: Scenes can contain namespaced elements and attributes

A scene SHALL admit element and attribute names qualified by a namespace IRI and a prefix. The writer SHALL declare each namespace once, at the outermost element that needs it, using the caller's prefix. Unqualified names SHALL keep their current meaning. Invalid local names, invalid prefixes and reserved prefixes SHALL be typed failures.

#### Scenario: RDF/XML inside SVG
- **WHEN** a scene contains an `rdf:RDF` element in the RDF namespace, with an `rdf:Description` child that has an `rdf:about` attribute
- **THEN** the written SVG is well-formed XML, declares the RDF namespace once, and existing unqualified SVG elements are unchanged

#### Scenario: Reserved prefix
- **WHEN** a caller qualifies a name with the prefix `xmlns`, or binds the prefix `xml` to a namespace other than the XML namespace
- **THEN** the caller receives a typed failure

#### Scenario: Two prefixes for one namespace
- **WHEN** a scene binds two different prefixes to the same namespace at the same element
- **THEN** the caller receives a typed failure that names both prefixes

### Requirement: A document can carry one metadata element

An SVG document SHALL be able to carry one `<metadata>` element, written as the first child of the root element. It SHALL contain namespaced XML content, text content, or both. Text content SHALL be written so that reading it back yields the same characters.

#### Scenario: Text payload with markup characters
- **WHEN** a document's metadata holds N-Quads text containing `<`, `&` and `]]>`
- **THEN** the written SVG is well-formed, and extraction returns the exact original text

#### Scenario: Metadata is first
- **WHEN** a document with a style sheet, definitions and metadata is written
- **THEN** the metadata element is the first child of the root `svg` element

### Requirement: Metadata can be extracted from SVG input safely

A caller SHALL be able to read SVG input and get the content of its root metadata element, as XML and text, by namespace. The reader SHALL refuse document type declarations and SHALL never resolve external entities or resources. Malformed input SHALL return a typed error with a line and column.

#### Scenario: Extract embedded RDF/XML
- **WHEN** a caller reads an SVG whose metadata holds an `rdf:RDF` element
- **THEN** the caller receives that element with its namespace bindings, ready for an RDF/XML reader

#### Scenario: Document type declaration
- **WHEN** the input contains a DOCTYPE that declares an external entity
- **THEN** the reader returns a typed error, and makes no file or network access

#### Scenario: No metadata
- **WHEN** a well-formed SVG has no metadata element
- **THEN** the reader reports that there is no metadata, which is distinct from an error

#### Scenario: Malformed input
- **WHEN** the input has an unclosed element on line 4
- **THEN** the reader returns a typed error that names line 4
