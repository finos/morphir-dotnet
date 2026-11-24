# Spec Delta

## Purpose

Writes diagrams as a reveal.js deck that shows each page exactly as the SVG renderer draws it, so web presentations use the same diagram that documentation does.

For example, two diagrams become one HTML deck with two slides, each holding its diagram's SVG inline.

## ADDED Requirements

### Requirement: The renderer writes reveal.js decks
The reveal.js renderer SHALL write one HTML document, UTF-8 without a byte order mark, with the extension `.reveal.html` and the media type `text/html`. It SHALL hold one section for each page, in the order given. Each section SHALL contain that page's original SVG inline, scaled to fit the slide.

#### Scenario: Two pages give two sections
- **WHEN** two layouts are rendered as one deck
- **THEN** the deck holds two sections, each with its layout's SVG

#### Scenario: A large diagram fits the slide
- **WHEN** a diagram larger than the reveal.js slide is rendered
- **THEN** its section scales the SVG to fit within the slide

### Requirement: SVG identifiers are unique in the deck
Each section's SVG SHALL have every identifier, and every reference to one, prefixed with the section's number. This includes element IDs, `href` and `url(#…)` references, and the style sheet's ID selectors. Every identifier in the deck is then unique, even when the same diagram appears twice.

#### Scenario: The same diagram twice
- **WHEN** a layout of a flowchart with icons is rendered twice in one deck
- **THEN** no identifier appears twice in the deck, and each section's markers, symbols and styles resolve within that section

### Requirement: reveal.js is referenced, not bundled
The deck SHALL load reveal.js's script and style sheet from a base address. The default base address SHALL name one pinned reveal.js version, and callers SHALL be able to give another, such as a local copy for offline use.

#### Scenario: A local copy can be used
- **WHEN** a caller renders with the base address `./reveal/`
- **THEN** the deck loads `./reveal/dist/reveal.js` and its style sheet from that address

### Requirement: The renderer draws the original SVG
The reveal.js renderer SHALL use the original-SVG mode, declare no features, and draw each page's SVG rather than its drawables. Its assessment SHALL therefore be native for any pages. The deck SHALL claim nothing beyond the SVG: it has no editable connectors, and metadata links appear only where the SVG itself has them.

#### Scenario: Any diagram is native
- **WHEN** any pinned corpus diagram is assessed for the reveal.js renderer
- **THEN** the assessment is native, and the renderer receives the page unlowered

### Requirement: Decks are deterministic and safe
Equal plans and options SHALL give byte-identical decks. A deck title, if given, SHALL be escaped as HTML text. The deck SHALL contain no script beyond reveal.js and its initialisation.

#### Scenario: A title with markup stays text
- **WHEN** a caller gives the title `<script>x</script>`
- **THEN** the deck shows that title as text and contains no added script element
