# Spec Delta

## Purpose

Represents JSON and YAML values as ordered data trees and draws them as readable containers, rows and hierarchy connections, with optional highlights that never change the data.

## ADDED Requirements

### Requirement: Ordered data model
The library SHALL represent objects with ordered member occurrences (duplicates allowed), arrays, strings, exact number lexemes, booleans and null, with explicit empty containers and scalar roots. `"true"` and `true` SHALL stay distinct.

#### Scenario: Duplicate members
- **WHEN** a JSON object has two members named `id`
- **THEN** the model keeps both in source order with distinct identities

### Requirement: Data-tree layout
The renderer SHALL keep member order and array positions, measure keys and values, show empty containers and scalar roots, and wrap long labels rather than truncate them.

#### Scenario: Nested array
- **WHEN** a value has an array of three objects
- **THEN** the SVG shows the array container with three child containers connected in order

### Requirement: Safe highlights
Resolved highlights SHALL restyle only the selected occurrences within the theme's contrast rules. Author styles SHALL NOT be injected as CSS, and values, types and layout SHALL NOT change.

#### Scenario: Highlighted row
- **WHEN** a YAML block highlights `server / port`
- **THEN** only that row's style changes and the SVG geometry is identical to the unhighlighted render
