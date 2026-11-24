# Spec Delta

## Purpose

Turns diagrams into Google Slides shapes without the library touching the network or credentials. The renderer writes the request body for the Slides API `presentations.batchUpdate` method, and the caller sends it with their own authorised client.

For example, the flowchart node `A[Start]` becomes a `createShape` request for a rounded rectangle, followed by requests that insert `Start` and set its colours.

## ADDED Requirements

### Requirement: The renderer writes batch update requests
The Google Slides renderer SHALL write one JSON object, UTF-8 without a byte order mark, whose `requests` array is a valid body for the Slides API `presentations.batchUpdate` method. It SHALL use the extension `.gslides.json` and the media type `application/json`. It SHALL make no network requests and hold no credentials.

#### Scenario: A flowchart becomes requests
- **WHEN** a flowchart is rendered with the Google Slides renderer
- **THEN** the output is an object with a `requests` array holding shape creation requests for its nodes and line creation requests for its links

### Requirement: Callers describe the target presentation
The renderer's options SHALL hold the following, each a parsed value:
- the target presentation's page size, which placement uses as the slide size
- an object ID namespace that the caller guarantees is unused in that presentation
- optionally, an existing slide's object ID

With an existing slide, exactly one page SHALL be accepted, and it SHALL be placed on that slide. Without one, the requests SHALL create one blank slide for each page, in order.

#### Scenario: An existing slide takes one page
- **WHEN** two pages are rendered with an existing slide's object ID
- **THEN** the render writes nothing and returns an error naming the page count

#### Scenario: New slides for several pages
- **WHEN** two pages are rendered with no existing slide
- **THEN** the requests create two slides, and each page's objects are placed on its own slide

### Requirement: Existing slide IDs are parsed
An existing slide's object ID SHALL be 1 to 50 ASCII letters, digits, underscores, hyphens and colons, parsed from nullable text with a parse, a try-parse and a create.

#### Scenario: An existing slide ID with a space is refused
- **WHEN** a caller parses `slide 1` as an existing slide's object ID
- **THEN** the parse fails and no ID is produced

### Requirement: Object IDs are valid and deterministic
Every created object SHALL have an ID made from the namespace, the page index and the object's position on that page, valid as a Slides object ID of 5 to 50 characters. This covers slides, shapes and lines. The namespace SHALL be 2 to 30 ASCII characters: letters, digits, underscores, hyphens and colons, starting with a letter, digit or underscore. Namespaces SHALL be parsed from nullable text with a parse, a try-parse and a create.

#### Scenario: Object IDs are deterministic and valid
- **WHEN** the same two-page layout is rendered twice with the shortest and with the longest permitted namespace
- **THEN** each pair of outputs is byte-identical, and every object ID is unique, starts with the namespace and has 5 to 50 characters

#### Scenario: An invalid namespace is refused
- **WHEN** a caller parses a one-character namespace, or one with a non-ASCII letter or another character that a Slides object ID does not allow
- **THEN** the parse fails and no namespace is produced

### Requirement: Requests refer only to existing objects
The requests SHALL create every slide and object first, in paint order, then set text and styles, and SHALL set line connections last, so that each request names only objects that earlier requests created or that the caller gave.

#### Scenario: A link painted before its nodes connects to them
- **WHEN** a flowchart, whose links are painted before its nodes, is rendered
- **THEN** every `updateLineProperties` request that sets a connection comes after the `createShape` requests of both shapes it names

### Requirement: Limits are checked before writing
A render SHALL write nothing and return an error naming the limit when a plan has more than 9,999 pages, or a page has more than 9,999,999 objects. These limits keep every ID within 50 characters.

#### Scenario: Too many pages are refused
- **WHEN** a plan with 10,000 pages is rendered
- **THEN** nothing is written and the result holds an error naming the page limit

### Requirement: The renderer declares its features
The Google Slides renderer SHALL declare these features: rounded rectangles, ellipses, diamonds, cylinders, hexagons, attached connectors, open and circle heads, wrapped text, interleaved painting, fill opacity, links and pages. It SHALL write no groups, so that page-wide paint order is kept. Paths SHALL therefore reach it as rectangles, bound lines as straight segments, message heads as arrows, and dash patterns as the default dash.

#### Scenario: Connectors are attached at connection sites
- **WHEN** the link `A --> B` is rendered
- **THEN** its line's start and end connections name the shapes of `A` and `B`

### Requirement: Placement and styles are in EMU and points
Shape sizes and positions SHALL be in whole EMU, placed by the slide placement on the whole-EMU grid. Font sizes SHALL be in points. Colours SHALL be the paint's colours, with fill opacity as the fill's alpha. Filled arrows, open arrows and circles SHALL be the `FILL_ARROW`, `OPEN_ARROW` and `FILL_CIRCLE` arrow styles.

#### Scenario: A node is placed in EMU
- **WHEN** a node is rendered with the default content area
- **THEN** its creation request gives its size and translation in whole EMU inside the content area
