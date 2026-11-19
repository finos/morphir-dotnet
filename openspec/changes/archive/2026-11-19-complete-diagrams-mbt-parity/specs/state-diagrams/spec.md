# Spec Delta

## Purpose

Represents state diagrams from Mermaid and PlantUML as one format-independent model, with hierarchy, concurrent regions, typed pseudostates and transition identity, and renders them as themed standalone SVG. Behaviour text such as events, guards and actions is kept as data and never evaluated.

## ADDED Requirements

### Requirement: Immutable state model
The library SHALL represent states, regions, transitions, descriptions, notes, direction and accessibility text as immutable ordered values. Each state SHALL belong to exactly one region; composite states own one or more regions.

#### Scenario: Concurrent regions
- **WHEN** a composite state has two regions separated by `--`
- **THEN** the model has one composite with two ordered regions, each owning its states

### Requirement: Typed pseudostates
The model SHALL distinguish initial, final, choice, fork, join, shallow history, deep history, entry point and exit point. Implicit initial and final states SHALL get stable IDs scoped to their region.

#### Scenario: Choice
- **WHEN** a state is declared `state Check <<choice>>`
- **THEN** the model marks `Check` as a choice pseudostate

### Requirement: Transition identity
Every transition SHALL keep its ID, endpoints, order, label, line style and markers. Parallel transitions and self-transitions SHALL be distinct. Event, guard and action text SHALL be ordered data.

#### Scenario: Parallel transitions
- **WHEN** two transitions go from `A` to `B` with labels `retry` and `skip`
- **THEN** the model has two transitions with different IDs

### Requirement: Structural validation
Validation SHALL report duplicate IDs, missing endpoints and note targets, ownership cycles and invalid pseudostate references with stable codes, and SHALL NOT repair them.

#### Scenario: Missing endpoint in a constructed model
- **WHEN** a caller builds a transition to a state that does not exist
- **THEN** validation reports the missing endpoint

### Requirement: Hierarchical state layout and painting
The renderer SHALL lay out states in all four directions, with nested and empty composites, region separators, cycles, self-loops and parallel transitions, and SHALL paint each pseudostate role with its own symbol. Composite boxes SHALL contain exactly their descendants.

#### Scenario: Fork bar
- **WHEN** a diagram has a fork pseudostate
- **THEN** the SVG draws it as a bar, not as a state box

### Requirement: Collapsed composites
A caller SHALL be able to collapse a composite state. Its descendants are hidden, transitions that cross its boundary attach to its outline, and the model is unchanged.

#### Scenario: Collapse Active
- **WHEN** `Active` is collapsed and a transition goes from `Idle` to a child of `Active`
- **THEN** the drawn transition ends on `Active`'s outline and its semantic endpoint is still the child
