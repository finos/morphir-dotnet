# Spec Delta

## Purpose

Draws connections in every diagram family with readable endpoint approaches, shape-aware attachments, a choice of elbow, rounded or curved lines, and small bridges where unrelated connections cross, without changing what the diagram means.

## ADDED Requirements

### Requirement: Connector styles
Render options SHALL offer elbow, rounded-elbow and smooth-curve styles, and crossing bridges on by default with an option for plain crossings. Omitting the style SHALL keep each family's natural style. Styling SHALL NOT change the model.

#### Scenario: Rounded corners
- **WHEN** a flowchart is rendered with the rounded-elbow style
- **THEN** every bend in its connectors is an arc and the endpoints are unchanged

### Requirement: Shape-aware attachments
Automatic attachments SHALL use the actual outline: diamonds prefer their four points, boxes spread several attachments evenly over a side and consider other clear sides before crowding one. Explicit ports and authored sides SHALL win.

#### Scenario: Three edges from one side
- **WHEN** three edges leave the right side of one box
- **THEN** their start points are evenly spaced along that side

### Requirement: Endpoint approach
Each marked endpoint SHALL have a straight approach long enough for its marker and stroke, with no turn inside it. A documented minimum-approach option SHALL be validated; a negative or non-finite value SHALL produce an invalid-option diagnostic and no output.

#### Scenario: Short final segment
- **WHEN** a route would turn 2 px before an arrowhead
- **THEN** the router moves the bend so the last segment fits the arrowhead

### Requirement: Crossing bridges
Where two unrelated connections cross, the later one in stable order SHALL hop over the other with a small arch and a clear underpass, keeping colours and dashes. Junctions, shared endpoints, lifelines and outlines SHALL NOT get bridges.

#### Scenario: Message crosses a lifeline
- **WHEN** a sequence message crosses a lifeline
- **THEN** no bridge is drawn

### Requirement: Final path geometry
The final styled path of each connector SHALL be available with a stable identity, and the original route points SHALL remain available. Static SVG, playback overlays and raster exports SHALL use the same final path.

#### Scenario: Playback follows the bridge
- **WHEN** a bridged connector is played
- **THEN** the motion follows the arch
