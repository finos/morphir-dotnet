# Spec Delta

## Purpose

Represents a Morphir model as a document tree, one document per manifest, module, type and value, addressed by Morphir names, and maps that tree to and from files under the parent repository's document-tree specifications for IR 3.1.0 and IR 4.

A tree for package `acme/demo` with module `Billing` holding type `Invoice` has the documents `manifest`, `Billing` (module), `Billing.Invoice` (type) and, for a dependency `morphir/sdk`, `morphir/sdk: Basics` (module). Their files are `manifest.json`, `pkg/acme/demo/billing/module.json`, `pkg/acme/demo/billing/invoice.type.json` and `deps/morphir/sdk/@/basics/module.json`. The names are the model's; the paths are a storage detail.

## ADDED Requirements

### Requirement: Documents are addressed by model identity
Each document of a tree SHALL be addressed by what it is in the model: the manifest; a module, type or value of the own package, by module path and name; or a module, type or value of a dependency, by package name, module path and name. An address SHALL hold no file path, escaped stem, extension or package version.

#### Scenario: Two spellings of one name have one address
- **WHEN** a type named `Invoice line` is read from a tree, and the same model is split from a single file
- **THEN** both give one address for the type, built from its module path and its name, with no file stem in it

#### Scenario: A dependency document is addressed by its package
- **WHEN** a tree holds module `Basics` of dependency `morphir/sdk`
- **THEN** its address names the package `morphir/sdk` and the module `Basics`, and holds no version

### Requirement: A model splits into a tree and joins back unchanged
Splitting a single-file model SHALL give a document tree with one document per module, type and value, plus the manifest. Joining a tree SHALL give the single-file model. For IR 3.1.0 and IR 4, joining the split of a model SHALL give a model equal to the original.

#### Scenario: A v4 model round-trips
- **WHEN** a v4 Library with two modules, a dependency and private and public definitions is split and joined
- **THEN** the joined model equals the original

#### Scenario: A v3 model round-trips
- **WHEN** a classic v3 Library is split and joined
- **THEN** the joined model equals the original, including every attribute slot

### Requirement: Tree files follow the parent document-tree specifications
Writing a tree SHALL produce the logical paths, name escaping, path budget, truncated stems with `fileNames`, and dependency directories that the parent v3 and v4 tree specifications define, with the `.json` extension of the JSON profile. A dependency's version segment SHALL be a bare `@`, because the model carries no package version.

#### Scenario: A long name is truncated and recorded
- **WHEN** a value's escaped stem would make its path exceed the tree's path budget
- **THEN** the file uses the truncated stem the spec defines and the module file's `fileNames` maps it back to the name

#### Scenario: A type and a value of one name share one cut stem
- **WHEN** a module holds a type and a value of one name, and the path of either file would exceed the path budget
- **THEN** both files use one truncated stem, short enough for the longer value file, the module file's `fileNames` maps the name to it once, and reading the tree gives the model that was written

#### Scenario: A dependency is written under a bare version segment
- **WHEN** a tree with dependency `morphir/sdk` is written
- **THEN** its files are under `deps/morphir/sdk/@/`

#### Scenario: Kit tree cases are met
- **WHEN** each pinned kit document-tree case is converted
- **THEN** the result has the case's canonical spelling or its named diagnostic

### Requirement: Modules are read and written in sorted logical-path order
A tree SHALL be read with its modules in sorted logical-path order, and written so that reading it back gives the same order, whatever order the storage lists files in.

#### Scenario: Directory order does not change the model
- **WHEN** the same tree is read from two stores that list its files in different orders
- **THEN** both reads give the same model with the same module order

### Requirement: The v3 version label follows the tree specification
Every file of a v3 tree SHALL say `"formatVersion": "3.1.0"`. A v3 `Library` tree SHALL join to a single file with `"formatVersion": 3`. A v3 `Specs` tree SHALL join to a single file with `"formatVersion": "3.1.0"`.

#### Scenario: A v3 Library returns its original label
- **WHEN** a single-file v3 `Library` with `"formatVersion": 3` is split, written, read and joined
- **THEN** every tree file says `"3.1.0"` and the joined file says `3`

#### Scenario: A v3 Specs tree keeps 3.1.0
- **WHEN** a v3 `Specs` tree is joined
- **THEN** the single file says `"formatVersion": "3.1.0"`

### Requirement: Content without a single-file place is reported, never dropped silently
Joining a tree SHALL drop the manifest's `layout` and `pathBudget` members, which describe the tree itself; writing a tree SHALL use a path budget of 4000 unless the caller gives another. It SHALL drop the v4 manifest's `version` and `created` members with a notice that names each one. No other content SHALL be dropped; content the model cannot carry SHALL be refused.

#### Scenario: Version and created are reported
- **WHEN** a v4 tree whose manifest has `version` and `created` is joined
- **THEN** the result holds a notice naming `version` and a notice naming `created`

#### Scenario: A classic member with no place in the model is refused
- **WHEN** a v3 tree file's `def` or `spec`, or a v3 single file, holds a member at any depth that the classic v3 model has no place for
- **THEN** reading is refused with `unknown_member` at that member's JSON location in its file, and nothing is converted

### Requirement: The reserved meta member is ignored
Reading a tree file SHALL ignore a top-level `$meta` member. Writing SHALL never emit one.

#### Scenario: A meta member does not change the model
- **WHEN** a module file has a top-level `$meta` object and the tree is read
- **THEN** the model equals the one read without that member, and a written tree holds no `$meta`

### Requirement: An invalid tree is refused with a named diagnostic
Reading a tree SHALL refuse, with a diagnostic naming the file and using the parent tree specification's code where it names one, both a JSON and a YAML manifest, a v3 manifest whose version is not `"3.1.0"`, an unknown member, a path over the path budget, a missing module or definition file, a file outside `pkg/` and `deps/` that claims to be part of the tree, and a versioned dependency segment. A refused read SHALL give every error it finds and no model.

#### Scenario: Ambiguous profile
- **WHEN** a tree directory holds `manifest.json` and `manifest.yaml`
- **THEN** reading is refused with `tree.ambiguous_profile`, naming both files

#### Scenario: A v3 manifest of another release
- **WHEN** a v3 tree's manifest says `"formatVersion": "3.0.0"`
- **THEN** reading is refused with `version_mismatch` at `manifest.json`'s `formatVersion`, naming the release it says and `"3.1.0"`

#### Scenario: Unknown member
- **WHEN** a v3 tree's manifest has a `version` member, which only a v4 manifest may carry
- **THEN** reading is refused with `unknown_member` at that member of `manifest.json`

#### Scenario: Path over the budget
- **WHEN** a tree file's path is longer than the path budget its manifest says
- **THEN** reading is refused with `tree.over_budget`, naming each such file and the budget

#### Scenario: Missing definition file
- **WHEN** a module file lists type `Invoice` and the tree holds no file for it
- **THEN** reading is refused with `missing_member` at the missing file's path, naming the module file and `Invoice`

#### Scenario: A file outside the tree
- **WHEN** a type file is at the root of the tree, beside `pkg/`
- **THEN** reading is refused with `tree.outside_tree`, naming that file

#### Scenario: Versioned dependency segment
- **WHEN** a tree holds `deps/morphir/sdk/@3.0.0/basics/module.json`
- **THEN** reading is refused with `tree.versioned_dependency`, naming that directory, `deps/morphir/sdk/@3.0.0/`, once

### Requirement: Trees are read and written through virtual file trees
Reading and writing a document tree SHALL work on a virtual file tree, so the same tree can come from or go to the disk, embedded resources, an archive or memory. The model code SHALL make no file-system calls of its own.

#### Scenario: The same tree from two sources
- **WHEN** one tree is read from a directory on disk and from an embedded-resource provider holding the same files, or a v3 tree from a directory on disk and from a file provider over memory
- **THEN** both reads give the same model and the same diagnostics
