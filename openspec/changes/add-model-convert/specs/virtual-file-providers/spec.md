# Spec Delta

## Purpose

Makes the virtual file tree a library that every Morphir assembly can use, including the `netstandard2.0` assemblies that a compiler or MSBuild host loads, and lets it exchange files with any Microsoft `IFileProvider`, so model code works on files without touching the disk itself.

## ADDED Requirements

### Requirement: The virtual file library is available on every Morphir target
Virtual files, virtual file trees and typed property bags SHALL be published in one library that targets the repository's library frameworks and `netstandard2.0`. Their observable behavior, as defined by `vfile-content-sources` and `typed-property-bags`, SHALL be the same on every target.

#### Scenario: A netstandard2.0 model library uses virtual file trees
- **WHEN** a library that targets `netstandard2.0` references the virtual file library and builds a tree from files
- **THEN** it builds with zero warnings and reads the same files, content and properties that a `net11.0` caller reads

#### Scenario: Existing pipeline behavior is unchanged
- **WHEN** a pipeline that ran before the move runs on the same input
- **THEN** it produces the same output tree, files, messages and properties

### Requirement: A virtual file tree is readable as a file provider
A virtual file tree SHALL be usable wherever a Microsoft `IFileProvider` is accepted. Looking up a path SHALL return the file at that location, or a non-existent result for a missing or directory path. Listing a directory SHALL return its files and subdirectories. Watching SHALL return a change token that never fires.

#### Scenario: A file is found by its location
- **WHEN** a tree holds `pkg/acme/demo/module.json` and a caller asks the provider for that path
- **THEN** the result exists, is not a directory, has the file's length, and its stream yields the file's bytes

#### Scenario: A missing file is reported as not existing
- **WHEN** a caller asks the provider for a path the tree does not hold
- **THEN** the result does not exist and opening it is not offered

#### Scenario: A directory lists its entries
- **WHEN** a caller lists the contents of `pkg/acme/demo`
- **THEN** the listing holds each file and subdirectory directly under it, and nothing deeper

#### Scenario: Watching never signals a change
- **WHEN** a caller watches any filter on a tree provider
- **THEN** the change token reports no change and accepts callbacks without invoking them

### Requirement: A virtual file tree can be built from any file provider
The system SHALL build a virtual file tree from any `IFileProvider`, including physical, embedded-resource and composite providers. Each file SHALL keep its location relative to the provider root, with forward slashes. Content SHALL be opened from the provider on demand, as for files loaded from disk.

#### Scenario: Embedded fixtures become a tree
- **WHEN** a test builds a tree from an embedded-resource provider holding a document tree
- **THEN** the tree has one file per embedded file at the same relative location, and reading each yields its bytes

#### Scenario: Content is not read while building
- **WHEN** a tree is built from a physical provider over a large directory
- **THEN** no file content is read until a caller reads that file
