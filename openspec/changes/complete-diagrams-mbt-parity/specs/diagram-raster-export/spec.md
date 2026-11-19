# Spec Delta

## Purpose

Converts the library's own SVG output to PNG or JPEG and a playback plan to an animated GIF in managed code, with deterministic bytes and no browser, native library or external process, so diagrams can be saved where SVG is not accepted.

## ADDED Requirements

### Requirement: Still raster export
The library SHALL convert SVG produced by `Morphir.Diagrams` to PNG (the default, with alpha) or JPEG (onto an explicit opaque background with a validated quality), keeping labels, symbols, colours, curves, arrowheads, clipping and crossing masks. Adaptive SVG SHALL be resolved to an explicit export theme.

#### Scenario: PNG of a themed flowchart
- **WHEN** a flowchart rendered in Monokai is exported as PNG with the dark theme
- **THEN** the result is PNG bytes with the requested dimensions and the dark palette

### Requirement: Sizing and options
Callers SHALL choose width, height or scale with the aspect ratio preserved, the background colour and the export theme. Results SHALL report the media type and actual dimensions and SHALL NOT crop.

#### Scenario: Scale
- **WHEN** an 800 by 400 diagram is exported at scale 2
- **THEN** the PNG is 1600 by 800

### Requirement: Animated GIF export
The library SHALL encode a multi-frame GIF from a validated playback plan with a bounded interval, dimensions, frame rate, background and loop setting, sampling by logical time and keeping repeated and concurrent occurrences.

#### Scenario: Two-second loop
- **WHEN** a sequence playback is exported over 2 seconds at 10 frames per second
- **THEN** the GIF has 20 frames

### Requirement: Fonts and glyphs
Text SHALL be drawn with embedded Noto Sans Mono and Noto Sans Symbols 2 fonts under the SIL Open Font License, shipped with their licence. A required glyph that no font has SHALL produce a diagnostic and no image.

#### Scenario: Missing glyph
- **WHEN** a label contains a character none of the fonts covers
- **THEN** export fails with a missing-glyph diagnostic

### Requirement: Deterministic and bounded
Equal SVG, fonts, options and samples SHALL give identical bytes. Dimensions, scale, quality, interval, frame rate and loop SHALL be validated before allocation, and limits on pixels, frames, cumulative work and encoded size SHALL produce diagnostics and no payload.

#### Scenario: Too many pixels
- **WHEN** an export asks for 100,000 by 100,000 pixels
- **THEN** it fails with a pixel-limit diagnostic before allocating

### Requirement: Player downloads
The player SHALL offer PNG, JPEG and GIF downloads beside SVG through the same conversion, excluding zoom, pan and transient highlights from stills, without changing playback position, speed or route.

#### Scenario: Download while zoomed
- **WHEN** the reader is zoomed to 300% and downloads PNG
- **THEN** the image shows the whole diagram at its natural size
