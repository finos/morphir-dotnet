# Spec Delta

## Purpose

Gives every new diagram family the same knowledge features as flowcharts and sequences: stable element references, selections, comments, links and RDF statements carried in the SVG, colour families in every built-in theme, and the offline player with its notes panel.

## ADDED Requirements

### Requirement: Element references for every family
Each element of every family SHALL have a stable element reference of the form `kind=value`, documented per family (for example `state=Idle`, `transition=Idle/Active/0`, `service=api`, `task=Approve_1`, `rule=list`, `row=/server/port`), and the SVG SHALL mark each element with its reference.

#### Scenario: State reference
- **WHEN** a state diagram is rendered
- **THEN** the group for state `Idle` carries `data-ref="state=Idle"`

### Requirement: Selections and annotations
Diagram selections SHALL accept references of every family, and comments, links and statements on those elements SHALL map to RDF and embed in the SVG metadata as they do for flowcharts.

#### Scenario: Comment on a BPMN task
- **WHEN** a comment targets `task=Approve_1` and the diagram is rendered with metadata
- **THEN** the SVG metadata holds the comment and the player shows a marker on the task

### Requirement: Themes for every family
Every element kind SHALL map to a documented colour family, so every built-in theme and custom theme colours every family, with text contrast of at least 4.5:1 and lines of at least 3:1 in dark and light modes.

#### Scenario: Monokai state diagram
- **WHEN** a state diagram is rendered in Monokai dark
- **THEN** ordinary states use the backend family and all text passes 4.5:1

### Requirement: Stable existing output
Adding the new families SHALL NOT change the SVG of existing flowcharts and sequences, except changes from the shared layout and connector styling that are reviewed through committed snapshot diffs.

#### Scenario: Existing flowchart
- **WHEN** the checkout flowchart is rendered with default options before and after this change
- **THEN** the output equals the committed, reviewed snapshot
