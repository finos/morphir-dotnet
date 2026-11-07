# Spec Delta

## ADDED Requirements

### Requirement: The global Morphir home is not a project location

Discovery SHALL NOT treat the global Morphir home (nonempty MORPHIR_HOME, otherwise the home directory's .morphir) as a project or workspace primary layout while walking upward. Its files SHALL contribute only as global sources. A directory with no other primary SHALL leave the host at its start location.

#### Scenario: Project below the home directory

- **WHEN** `~/.morphir/morphir.toml` exists and discovery starts in `~/work/app`, which has no primary
- **THEN** the home directory is not reported as a project or workspace and `~/.morphir/morphir.toml` is consulted only as a global source

#### Scenario: Relocated Morphir home

- **WHEN** MORPHIR_HOME is `/opt/morphir-home`, `/opt/morphir-home/morphir.toml` exists, and discovery starts in `/opt/morphir-home/extensions/tool`, which has no primary
- **THEN** `/opt/morphir-home` is not reported as a project or workspace and `/opt/morphir-home/morphir.toml` is consulted only as a global source

#### Scenario: Real project .morphir layout still works

- **WHEN** a project directory that is not the Morphir home contains `.morphir/morphir.toml`
- **THEN** discovery reports that directory as a project location

## MODIFIED Requirements

### Requirement: Machine sources use platform conventions
Discovery SHALL inspect system and global Morphir locations using platform config directories and the alternate Morphir home. Nonempty MORPHIR_HOME SHALL replace only the home alternative. An absolute MORPHIR_HOME, including Windows drive and UNC paths written with either separator, SHALL be used as given; a relative one SHALL resolve against the start directory. Relative XDG_CONFIG_HOME SHALL be ignored. Same-format alternatives SHALL be ambiguous; cross-format sources SHALL layer.

#### Scenario: Invalid XDG path
- **WHEN** Linux XDG_CONFIG_HOME is relative
- **THEN** global discovery uses the platform home fallback

#### Scenario: Split global ambiguity
- **WHEN** a platform config directory and Morphir home both contain ordinary TOML global files
- **THEN** loading fails with both paths instead of choosing a directory

#### Scenario: Windows native Morphir home
- **WHEN** on Windows MORPHIR_HOME is `C:\Users\me\.morphir`
- **THEN** global sources are read from `C:/Users/me/.morphir` and not from a path joined onto the start directory

#### Scenario: Relative Morphir home
- **WHEN** MORPHIR_HOME is `.morphir-home` and discovery starts in `/repo/app`
- **THEN** global sources are read from `/repo/app/.morphir-home`
