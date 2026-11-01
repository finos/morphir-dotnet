# Spec Delta

## Purpose

Lets a virtual file represent text, bytes or a stream that can be opened on demand, so large files do not have to be held in memory while they move through a pipeline.

## ADDED Requirements

### Requirement: Typed file content

A file's content SHALL be absent, text, bytes or a re-openable stream source. A file SHALL be constructible from each of these, and C# callers SHALL have a way to construct each that does not rely on F# inline functions.

#### Scenario: Text content

- **WHEN** a file is created with a string
- **THEN** its content is text

#### Scenario: Unsupported content type

- **WHEN** F# code creates a file from a value that is not a string, a byte buffer or a stream source
- **THEN** the code does not compile

### Requirement: Stream sources can be opened repeatedly

A stream source SHALL open a new independent stream each time it is read, so that one consumer reading the content does not use it up for the next. The caller that opens a stream SHALL own and dispose of it.

#### Scenario: Failed parser followed by another parser

- **WHEN** a parser reads a stream source and fails, and the next parser reads the same file
- **THEN** the second parser reads the content from the beginning

#### Scenario: Source is not opened until read

- **WHEN** a file with a stream source passes through stages that do not read its content
- **THEN** the source is never opened

### Requirement: Content reading reports failures

Reading content as a stream or as text SHALL return a typed error when the source cannot be read, and SHALL NOT raise an exception to the caller. Text SHALL be encoded as UTF-8 without a byte order mark when converted to bytes. Reading text SHALL honour a byte order mark in bytes or stream content.

#### Scenario: Source has been deleted

- **WHEN** a file's stream source opens a path that no longer exists
- **THEN** reading its content returns an error holding the original exception

#### Scenario: Reading absent content

- **WHEN** a file with no content is read as text
- **THEN** the read returns empty text

### Requirement: Loading from disk does not read content

Loading a directory into a tree SHALL record each file's location and a stream source for its content, without reading file content. Directory enumeration failures SHALL return a typed load error.

#### Scenario: Large file not read

- **WHEN** a directory is loaded into a tree and no content is requested
- **THEN** no file in the directory has been opened

#### Scenario: Unreadable file surfaces on read

- **WHEN** a directory containing an unreadable file is loaded and that file's content is later read
- **THEN** loading succeeds and the read returns a typed error

### Requirement: Binary content is preserved

Bytes and stream content SHALL reach disk byte for byte. Text content SHALL be written as UTF-8 without a byte order mark.

#### Scenario: Binary round trip

- **WHEN** a file containing arbitrary non-UTF-8 bytes is loaded from disk and published
- **THEN** the published file has identical bytes

#### Scenario: Text round trip

- **WHEN** a text file containing CRLF line endings and non-ASCII characters is published and loaded back
- **THEN** reading it as text returns the original string

### Requirement: Flat string maps read content explicitly

Converting a tree to a map of location to string SHALL read each file's content and SHALL return a typed error if any file cannot be read, rather than raising an exception or storing the name of a stream type.

#### Scenario: Stream content in a string map

- **WHEN** a tree containing a stream source is converted to a string map
- **THEN** the map holds the stream's text

#### Scenario: Unreadable content in a string map

- **WHEN** a tree contains a stream source that cannot be opened and is converted to a string map
- **THEN** the conversion returns a typed error
