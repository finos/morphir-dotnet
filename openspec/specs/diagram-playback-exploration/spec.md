# diagram-playback-exploration Specification

## Purpose
Extends the offline player beyond authored-order playback: readers explore directed routes and reachability between elements, play explicit scenarios and node visits, and inspect railroad and data-tree structure. Every result is structural; nothing evaluates conditions, guards, timers or matches.

## Requirements

### Requirement: Route and reachability exploration
The player and the F# `DiagramExploration` API SHALL let a reader pick two elements and see one directed shortest route, and pick one element and see upstream or downstream reachability, with the same answers. Results SHALL distinguish route, reachable, unreachable, invalid (an element outside the diagram) and limit-exhausted, and SHALL terminate on cycles.

#### Scenario: Unreachable
- **WHEN** a reader asks for a route from `Done` back to `Start` in an acyclic flowchart
- **THEN** the player reports that `Start` is unreachable and draws no route

### Requirement: Direction semantics
Exploration SHALL follow arrowheads. Bidirectional connections SHALL be traversable both ways. Connections without heads SHALL be followed only when the reader opts into undirected exploration.

#### Scenario: Unheaded link
- **WHEN** two nodes are joined by `A --- B` and the reader asks for a route from A to B
- **THEN** the route is unreachable unless undirected exploration is on

### Requirement: Explicit scenarios and node visits
Callers SHALL be able to supply finite scenarios of connector occurrences, which may repeat and overlap, and timed node visits. A visit to an element outside the diagram SHALL produce a diagnostic, and an empty or negative duration SHALL be refused when the step or visit is created.

#### Scenario: Loop taken twice
- **WHEN** a scenario names a loop-return edge twice
- **THEN** playback animates that edge twice in order

### Requirement: Every family plays
The player SHALL play state, architecture, swimlane, ZenUML, BPMN, railroad and data-tree diagrams with the existing controls, keyboard operation, reduced-motion behaviour, pause while hidden, print and offline rules.

#### Scenario: Data tree visits
- **WHEN** a JSON diagram is played
- **THEN** rows are visited in member order without fake connector paths

### Requirement: Structural results only
Exploration and playback SHALL NOT evaluate guards, gateway conditions, timers, regex matches or grammar acceptance, and SHALL label results as structural or as a supplied scenario.

#### Scenario: Gateway
- **WHEN** a route passes an exclusive gateway with conditions
- **THEN** the route includes every structurally possible branch the reader selects and states it is structural
