# Spec Delta

## Purpose

Writes a file tree to disk so that files cannot escape the output root, cannot silently overwrite each other, and failed output never replaces good output.

## ADDED Requirements

### Requirement: Output stays inside the root

Publication SHALL reject a tree containing a rooted location, a location with a `..` segment, or a location whose resolved path leaves the output root. Empty and `.` segments SHALL be normalized away when a location is built. Publication SHALL validate the whole tree before writing anything. The root tree's path SHALL NOT contribute to output paths.

#### Scenario: Dot segments

- **WHEN** a tree contains the file path `./src/./a.fs`
- **THEN** the file is published at `src/a.fs`

#### Scenario: Parent traversal

- **WHEN** a tree contains the location `../evil.fs`
- **THEN** publication returns an error naming that location and writes nothing

#### Scenario: Absolute path

- **WHEN** a tree contains a rooted file path
- **THEN** publication returns an error naming that location and writes nothing

#### Scenario: Absolute tree root

- **WHEN** a tree whose own path is absolute contains the file `a.fs`
- **THEN** the file is published directly under the output root

### Requirement: Symbolic links cannot redirect output

Publication SHALL reject a location when the output root or any directory on the way to it is a symbolic link.

#### Scenario: Symlinked subdirectory

- **WHEN** a directory inside the output root is a symbolic link to somewhere else and a file would be published through it
- **THEN** publication returns an error naming that location and writes nothing

#### Scenario: Symlinked output root

- **WHEN** the output root itself is a symbolic link
- **THEN** publication returns an error and writes nothing

### Requirement: Collisions never lose files

Publication SHALL reject a tree when two files have the same location, comparing case-insensitively on every platform so that output is portable. It SHALL also reject a tree containing a file with no usable path.

#### Scenario: Paths differing only by case

- **WHEN** a tree contains `Domain/Types.fs` and `domain/types.fs`
- **THEN** publication returns an error naming the collision and writes nothing

#### Scenario: File without a path

- **WHEN** a tree contains a file with no path
- **THEN** publication returns an error identifying that file and writes nothing

### Requirement: Every problem is reported

Publication validation SHALL report all problems it finds in a single typed error, and validation SHALL be available without publishing so a caller can check a tree before doing expensive work.

#### Scenario: Several problems

- **WHEN** a tree has a traversal path, a duplicate location and a pathless file
- **THEN** the error lists all three

#### Scenario: Validation alone

- **WHEN** a caller validates a tree without publishing
- **THEN** it receives the same problems and the output root is untouched

### Requirement: Trees with errors are not published

Publication SHALL refuse a tree that reports errors, so failed generation cannot replace previous output.

#### Scenario: Failed generation after a success

- **WHEN** a tree with an error diagnostic is published to a root holding earlier output
- **THEN** publication returns an error and the earlier output is unchanged

### Requirement: Staged publication

Publication SHALL first write all content to a private staging directory inside the output root, and SHALL only begin replacing output after every file has been staged. A staging failure SHALL leave existing output unchanged. The staging directory SHALL be removed whether publication succeeds or fails.

#### Scenario: Content cannot be read

- **WHEN** a file's stream source fails while being staged
- **THEN** publication returns an error holding the original exception, existing output is unchanged and no staging directory remains

#### Scenario: Concurrent publication to one root

- **WHEN** two publications by the same user on one machine target the same output root at once
- **THEN** they do not interleave, and the final output is exactly one of the two complete sets

#### Scenario: Nothing extra is left behind

- **WHEN** a tree is published with no manifest and the output directory is loaded back
- **THEN** the loaded tree contains exactly the published files, with no lock or staging files

### Requirement: Repeat publication does not rewrite identical files

Publication SHALL leave a file untouched when its content is byte-identical to the file already at that location, and SHALL compare content without loading whole files into memory. The result SHALL report which locations were written, unchanged and removed.

#### Scenario: Unchanged repeat

- **WHEN** the same tree is published twice
- **THEN** the second publication reports every file as unchanged and their timestamps are unchanged

### Requirement: Manifest tracks owned files

When enabled, publication SHALL write a manifest of the current locations in the tree's declared order, after output files are in place. It SHALL delete files listed in the previous manifest that are no longer present, and SHALL NOT delete any other file. A manifest entry that fails path validation SHALL be an error.

#### Scenario: Module removed

- **WHEN** a tree omits a location listed in the previous manifest
- **THEN** that file is deleted and the new manifest omits it

#### Scenario: Unowned file

- **WHEN** the output root contains a file no manifest ever listed
- **THEN** publication leaves it in place

#### Scenario: Tampered manifest

- **WHEN** the previous manifest lists `../outside.fs`
- **THEN** publication returns an error and deletes nothing outside the output root

#### Scenario: Directory became a symbolic link

- **WHEN** the previous manifest lists `linked/old.fs` and `linked` is now a symbolic link to somewhere else
- **THEN** publication returns an error naming that entry and deletes nothing through the link

#### Scenario: Manifest name is not a plain file name

- **WHEN** the manifest's name is a rooted path or contains a directory or `..`
- **THEN** publication returns an error naming it and writes nothing

#### Scenario: Declared order

- **WHEN** a tree declares an order that differs from directory order
- **THEN** the manifest lists locations in the declared order

### Requirement: Failures are typed

Publication SHALL return typed errors that distinguish an invalid tree from a failure while writing, and SHALL keep the original exception for write failures. The errors SHALL be describable as text for a person.

#### Scenario: Output root is a file

- **WHEN** the output root is an existing file
- **THEN** publication returns a write failure holding the original exception
