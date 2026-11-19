# bpmn-diagrams Specification

## Purpose
Converts a selected BPMN process or collaboration view into a diagram and renders it with BPMN notation in the library's themes, using supplied diagram interchange geometry or an automatic layout.

## Requirements

### Requirement: View selection
The caller SHALL be able to select a process, collaboration or DI plane by identity. Automatic selection SHALL prefer planes, then collaborations, then processes, and SHALL report an ambiguity diagnostic when a tier has several candidates. Choreography and conversation views SHALL produce an unsupported-view diagnostic.

#### Scenario: Two processes, no collaboration
- **WHEN** a document has two processes and no plane or collaboration
- **THEN** automatic selection reports an ambiguity and renders nothing

### Requirement: Element coverage
Rendering SHALL draw every BPMN 2.0.2 process and collaboration graphical element: activities and their markers, events and event definitions, gateways, subprocesses, call activities, pools including black-box pools, nested lanes, sequence and message flows, associations, data elements, groups and text annotations. An element it cannot draw SHALL produce an error, never a different symbol.

#### Scenario: Boundary event
- **WHEN** a task has a non-interrupting timer boundary event
- **THEN** the SVG draws a dashed double circle with a timer on the task's border

### Requirement: Supplied geometry policy
Rendering SHALL support prefer-supplied, require-supplied and automatic geometry. Complete DI SHALL be kept as given. Prefer-supplied with missing DI SHALL use a full automatic layout with a warning, never a mix. Require-supplied with missing DI SHALL fail.

#### Scenario: Missing DI
- **WHEN** a process has no DI and the policy is require-supplied
- **THEN** rendering fails with a geometry diagnostic

### Requirement: Automatic layout
Automatic layout SHALL keep pools, lanes, subprocesses and attached events contained, route flows to their real endpoints, and handle cycles, branches, joins and disconnected parts deterministically.

#### Scenario: Lanes
- **WHEN** a pool has lanes `Sales` and `Billing`
- **THEN** each task is drawn inside its lane and the lanes stack inside the pool
