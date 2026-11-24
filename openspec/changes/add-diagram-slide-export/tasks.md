# Tasks

Each task writes its scenarios as failing tests first and confirms they fail for the stated reason, then implements until they pass, as AGENTS.md requires.

## 1. Tracking, package and placement

- [x] 1.1 File the GitHub issue for this change and link it under Tracking in `proposal.md`. Confirm that `add-diagram-canvas-export` has merged. Verify the link resolves. (The two changes land together in one pull request, the canvas change's commits first.)
- [x] 1.2 Create `src/Morphir.Diagrams.Slides`, with the project settings from interop rule 1, a README, the licence and repository metadata, referencing Morphir.Diagrams and Morphir.Diagrams.Raster. Create `tests/Morphir.Diagrams.Slides.Tests` (TUnit), with the Open XML SDK as a test-only dependency. Add both to `Morphir.slnx`, the suite to `build/Helpers/TestPlan.cs`, and the package to `LibraryPackages` after Morphir.Diagrams.Raster. Verify `./build.sh --target Pack` produces the package with no warnings and the build tests pass.
- [x] 1.3 Write the `slide-placement` scenarios as tests: the default size, the refused area, an area empty after rounding, the negative BPMN extent, the zero-size extent, rounding on each grid, the wide diagram, the maximum scale, and strokes kept. Add a property test that every placed edge lies on the grid and within the rounded area, across corpus layouts and fractional 4:3 areas, for each grid. Then implement `SlidePlacement` with slide sizes, areas, the fitted extent, the grid and the rational fit (design PLC-1).
- [x] 1.4 Write tests for the shared ZIP writer and PNG helper: equal bytes for seekable and forward-only streams, the density step from two to one, a forced raster failure that writes nothing, the pixel limit and the work limit separately, cancellation between images, and the `slides/png-approximated` warning for an authored named colour and a gradient. Then make the raster painter's supported-paint check visible to the package and implement them (design PKG-1, PPT-1), returning `slides/raster-failed` through `Diagnostic.Error`.

## 2. PowerPoint

- [x] 2.1 Write the two-slides and equal-bytes scenarios, a cross-runtime comparison of entry contents and PNG pixels, and validator tests for Microsoft 365 and Office 2010 with an SVG image. Confirm whether the Office 2010 validator accepts the SVG blip in the extension list, and if it does not, add the `mc:Ignorable` declaration and update the spec and design. Then write the minimal PresentationML package: content types, relationships, presentation, master, blank layout, theme, and one slide per page (design PPT-1). (The Office 2010 validator accepts the SVG blip inside the extension list, so no `mc:Ignorable` declaration is needed. The cross-runtime comparison is `SlideDigestTests`, which digests entry contents and runs on each operating system; PNG pixels are pinned across runtimes by the raster baselines.)
- [x] 2.2 Write the diamond, BPMN icon, quadratic-curve, link and paint-order scenarios, and a validator test over every pinned corpus. Then implement preset shapes, custom geometry with `quadBezTo`, text bodies with wrapping and rotation, paints with `noFill`, opacity on fills, strokes and text, dashes, the first web link and flattened elements and groups. (Inline `style` statements are not drawn by the SVG renderer either, so the opacity scenario uses the theme's translucent fill.)
- [x] 2.3 Write the exact-route, exact-connectors assessment, cross-head, half-head, picture-fallback, lower-density and raster-failure scenarios. Then implement exact connectors, the line-end mapping, drawn heads grouped with their line, and images with the SVG extension and a PNG fallback. Open the package in a reader that ignores the SVG extension and confirm it shows the PNG. Move a grouped line with a drawn head in PowerPoint and confirm the head moves with it. (PowerPoint is not available here. Exact lines are freeform shapes, not connectors, because LibreOffice re-routes a connector between the corners of its box. LibreOffice 26.8 reads the SVG blip rather than the PNG, and drew black until the stored SVG got the light palette in place of its CSS variables; it now draws each page as the SVG does. The head-follows-line check in PowerPoint is left to 6.2.)
- [x] 2.4 Before recording goldens, check the connection site indices of each preset against a file saved by PowerPoint. Then write the glued scenario and a test of the features each option declares, and implement the glued connector option. Record a digest of entry contents for one diagram of each family. (PowerPoint is not available here, so the site indices were checked against ECMA-376 `presetShapeDefinitions.xml`, as LibreOffice ships it: an ellipse has eight sites, a can has the lid first and a hexagon starts at the right. The digests are in `tests/Fixtures/DiagramExport/slide-digests.json`.)

## 3. OpenDocument presentation

- [ ] 3.1 Write the raw-header, equal-bytes and cross-runtime scenarios. Then write the ODF package, with `mimetype` first, uncompressed and with no extra field, then the manifest, content and styles with the page layout (design ODP-1).
- [ ] 3.2 Write the cylinder, BPMN arc icon, subgraph assessment and image scenarios. Then implement custom shapes with their own enhanced geometry and view boxes, the enhanced-path mapping, text, automatic styles with fill opacity, links, flattened groups and image frames with a PNG fallback.
- [ ] 3.3 Write the glued-connector and cross-head scenarios, and a test that every head is a closed marker outline matching the SVG's head. Then implement connectors glued at the nearest glue points, with `svg:d` routes and markers. Before recording goldens, open the output in LibreOffice Impress, move a shape and save. Check that the glue holds, the route is kept, and the heads follow and sit at the line ends as in the SVG. If Impress drops or re-routes the route, stop declaring polylines and exact routes, and update the spec and design. Then record a digest of entry contents for one diagram of each family.

## 4. Google Slides

- [ ] 4.1 Write the request, existing-slide, new-slides, ID, invalid-namespace, page-limit and request-order scenarios, and tests for `SlideObjectNamespace` and `SlideObjectId` `Parse`, `TryParse` and `Create` with each `Create` literal. Then implement the options and the request writer: `createSlide` for new slides, the ID scheme, the limits, shapes, text, styles in whole EMU and points with fill alpha, and the first web link, with no groups (design GSL-1).
- [ ] 4.2 Before recording goldens, check the connection site order for each shape type by sending requests to a test presentation through the API. Then write the connection and arrow-style scenarios, and implement lines with `updateLineProperties` connections and arrow styles. Record a digest for one diagram of each family.

## 5. reveal.js

- [ ] 5.1 Confirm that the pinned reveal.js release exists, is at least two weeks old, and that its template loads `dist/reveal.js` and its style sheet. Then write the two-sections, same-diagram-twice, large-diagram, local-copy, native-and-unlowered and escaped-title scenarios, and implement the deck writer in the original-SVG mode: sections with section-prefixed SVG identifiers, `r-stretch`, the base address option, the escaped title and the initialisation script (design REV-1).

## 6. Integration

- [ ] 6.1 Document each renderer in the package README with C# examples compiled by a README test. Include the Google Slides send step with the caller's own client, an offline copy of reveal.js, the connector options and what each keeps, and the font notes. Verify the README test passes, and run the `fsharp-csharp-interop-reviewer` agent and fix its findings.
- [ ] 6.2 Render one diagram of each family to every format, then check each by hand:
  - open the `.pptx` in PowerPoint and LibreOffice
  - open the `.odp` in Impress
  - open the reveal.js deck in a browser
  - send the Google requests to a test presentation

  Verify each opens without repair, text is editable, glued connectors follow moved shapes, and screenshots are attached to the pull request.
- [ ] 6.3 Add one export to each slide target to `tests/Morphir.Diagrams.Smoke`, asserting that each succeeds. Verify that the existing `diagrams-smoke` scenarios pass, managed in `LibraryHosts.feature` and under Native AOT in `NativeAot.feature`, with no new warnings beyond the allowed FSharp.Core ones.
- [ ] 6.4 Update the diagrams documentation page and the changelog. Run `dotnet format`, `mise run openspec:verify` and `./build.sh --target Test`, and run the `test-manager-reviewer` agent and fix its findings. Verify every suite passes on Linux, Windows and macOS in CI.

## Workflow follow-up

- Follow AGENTS.md for review and explicit approval before merge.
- Archive the change after merge.
