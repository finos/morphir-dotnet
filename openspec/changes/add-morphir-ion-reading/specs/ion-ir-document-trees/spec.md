# Spec Delta

## Purpose

Reads a Morphir distribution stored as an Ion document tree, where each file's path supplies its package, module and name, into the same IR a single file with those elements produces.

## ADDED Requirements

### Requirement: The manifest identifies the tree

A tree SHALL be identified by `manifest.ion` at its root. A second manifest (`manifest.json`, `manifest.yaml` or `manifest.yml`) SHALL make the root ambiguous. The reader SHALL read files from a `VFileTree` and SHALL NOT access the file system itself.

#### Scenario: Tree root

- **WHEN** a tree has `manifest.ion` at its root
- **THEN** the reader reads it as an Ion tree

#### Scenario: Missing manifest

- **WHEN** a tree has no manifest at its root
- **THEN** the reader returns `missing_manifest`

#### Scenario: Two manifests

- **WHEN** a tree has both `manifest.ion` and `manifest.json`
- **THEN** the reader returns `ambiguous_manifest` naming both files

### Requirement: A tree reads as the equivalent datagram

A tree SHALL read as the datagram holding the same elements: the header, each dependency in manifest order, then each module in path order. Within a module directory, the module file comes first, then type files, then value files, each in path order.

#### Scenario: Reference tree

- **WHEN** a tree written by the pinned reference CLI is read beside the single file for the same distribution
- **THEN** the results are equal, ignoring module and member order

#### Scenario: Dependency with no modules

- **WHEN** the manifest names a dependency that has no files under `deps/`
- **THEN** the result holds that dependency with no modules, in manifest order

#### Scenario: Footer in a tree

- **WHEN** a tree file holds `morphir_footer::{}`
- **THEN** the reader returns `invalid_document` naming the file

### Requirement: The path supplies names

A file MAY omit `package`, `module` and `name` when its path supplies them. A stated name SHALL match the path. A file stem cut by the path budget SHALL be checked by its prefix and SHA-256 digest suffix.

#### Scenario: Name omitted

- **WHEN** `pkg/example/finance/eligibility/score.type.ion` holds a type with no `name`
- **THEN** the type is `score` in module `eligibility` of package `example/finance`

#### Scenario: Name conflict

- **WHEN** `score.type.ion` states `name: "other"`
- **THEN** the reader returns `path_name_mismatch` naming the file

#### Scenario: Cut stem

- **WHEN** a file's stem ends in `__` and eight hex digits, and its stated name has that prefix and digest
- **THEN** the reader uses the stated name

#### Scenario: Cut stem without a name

- **WHEN** a file with a cut stem states no `name`
- **THEN** the reader returns `path_name_mismatch`

#### Scenario: Cut stem with the wrong digest

- **WHEN** a file with a cut stem states a name whose digest differs
- **THEN** the reader returns `path_name_mismatch`

#### Scenario: Names that look like tree files

- **WHEN** a tree holds module `manifest`, type `manifest`, type `module` and an escaped type `con_`
- **THEN** each reads as the node its path names, and `con_` reads as the name `con`

#### Scenario: Module file directly under the package

- **WHEN** `module.ion` sits directly under the package directory
- **THEN** the reader returns `invalid_tree_path` naming the file

### Requirement: Module files follow their directory's rules

A `pkg/` module directory SHALL hold exactly one module element. A `deps/` directory SHALL hold `module::spec` fragments that merge, or for an application one `public::def::module`. A member stated twice SHALL be refused.

#### Scenario: Second own module

- **WHEN** a `pkg/` `module.ion` holds two definition modules
- **THEN** the reader returns `duplicate_name`

#### Scenario: Dependency fragments merge

- **WHEN** a `deps/` `module.ion` holds two `module::spec` fragments with different members
- **THEN** the dependency module holds both sets of members

#### Scenario: Member in two files

- **WHEN** a type appears in `module.ion` and in its own `.type.ion` file
- **THEN** the reader returns `duplicate_name`

### Requirement: Errors name the failing file

An error from a tree file SHALL carry that file's location in the tree, and limits SHALL apply to the whole tree.

#### Scenario: Malformed file

- **WHEN** one `.value.ion` file is malformed
- **THEN** the error's path is that file's location, and no partial distribution is returned

#### Scenario: Limit across files

- **WHEN** each file is under the node limit but the tree's total is over it
- **THEN** the reader returns `node_limit_exceeded`

#### Scenario: Lazy sources

- **WHEN** a tree's stream source fails to open
- **THEN** the reader returns a typed error holding the original cause and that file's path
