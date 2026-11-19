# diagram-connectors Specification

## Purpose
Draws connections in every graph family with readable endpoint approaches, attachments spread along box sides, a choice of elbow, rounded or curved lines, and optional bridges where unrelated connections cross, without changing what the diagram means.

## Requirements

### Requirement: Connector styles
Render options SHALL offer elbow, rounded-elbow and curved connector styles for every family drawn as a graph. Elbow is the default and keeps existing output unchanged. Styling SHALL NOT change the model or the endpoints.

#### Scenario: Rounded corners
- **WHEN** a flowchart is rendered with the rounded style
- **THEN** every bend in its connectors is a curve and the endpoints are unchanged

#### Scenario: Default style
- **WHEN** a flowchart is rendered without a connector style
- **THEN** its SVG equals the SVG of the elbow style

### Requirement: Attachments spread along a side
When two or more connectors leave or enter the same side of a box-shaped node, their attachment points SHALL be spaced evenly along that side. Diamonds, circles and marker shapes SHALL keep their single point on each side.

#### Scenario: Three edges from one side
- **WHEN** three edges leave the right side of one box for three targets in the next rank
- **THEN** their start points are evenly spaced along that side

### Requirement: Endpoint approach
The last segment before an arrowhead SHALL be straight and at least 12 px long.

#### Scenario: Arrowhead approach
- **WHEN** any flowchart from the pinned corpus is rendered
- **THEN** every connector with an end marker ends with a straight segment of at least 12 px

### Requirement: Crossing bridges
When bridges are enabled, where two connectors that share no endpoint cross, the later one in paint order SHALL hop over the earlier one with a small arc, keeping its colour and dashes. Connectors that share an endpoint, and curved connectors, SHALL NOT get bridges. Bridges are off by default.

#### Scenario: Two crossing links
- **WHEN** a flowchart whose links cross is rendered with bridges enabled
- **THEN** the later link's path has an arc at the crossing and the earlier link's path has none

#### Scenario: Shared endpoint
- **WHEN** two links from the same node cross nothing else
- **THEN** neither path has an arc

### Requirement: Final path geometry
The final styled path of each connector SHALL be the path that the static SVG, playback and raster export use.

#### Scenario: Playback follows the bridge
- **WHEN** a bridged connector is played
- **THEN** the bound connector's path data contains the arc
