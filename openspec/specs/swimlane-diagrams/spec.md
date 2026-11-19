# swimlane-diagrams Specification

## Purpose
Represents flows whose steps belong to lanes of responsibility, as read from Mermaid swimlanes and PlantUML activity diagrams, and renders them with lane bands, typed control roles and readable connections as themed standalone SVG.

## Requirements

### Requirement: Swimlane model
The library SHALL represent lanes in order, nodes with at most one lane, groups, notes and connections with direction, labels, markers and line styles. Nodes created implicitly by an edge SHALL have no lane.

#### Scenario: Implicit endpoint
- **WHEN** a swimlane edge names a node that no lane declares
- **THEN** that node has no lane

### Requirement: Typed control roles
Nodes converted from PlantUML activity diagrams SHALL carry a control role: action, decision, merge, fork, join, start, activity final, flow final or connector. `stop` and `end` SHALL stay distinct finals. Mermaid shapes SHALL NOT gain a control role from their shape alone.

#### Scenario: Stop and end
- **WHEN** an activity diagram has one branch ending in `stop` and another in `end`
- **THEN** the model has an activity final and a flow final

### Requirement: Lane layout
The renderer SHALL draw one band per lane in declaration order, in the diagram's direction, with each node inside its lane's band and empty lanes still visible. Ordinary groups SHALL NOT be drawn as lanes.

#### Scenario: Left-to-right lanes
- **WHEN** a swimlane diagram uses `LR` with lanes `Customer` and `Shop`
- **THEN** the SVG has two horizontal bands in that order, each holding its nodes

### Requirement: Structured activity topology
Conversion SHALL produce explicit edges for sequences, branches, loop returns and exits, forks, joins, merges and splits, with deterministic implicit control nodes. Guard and join text SHALL be kept and not evaluated.

#### Scenario: While loop
- **WHEN** an activity has `while (more?) is (yes)` … `endwhile (no)`
- **THEN** the flowchart has a decision with a `yes` edge into the body, a loop-return edge, and a `no` exit edge
