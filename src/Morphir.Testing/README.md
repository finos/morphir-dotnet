# Generation conformance tests

`GenerationCases` owns behavioral expectations. `GenerationConformance` applies
them to representation adapters and checks the required matrix.
`GenerationCompiler` compiles and executes every successful result with FSI.
Existing generator tests call the same compiler helper.

To extend a case:

1. Specify the consumer and expected output independently of generated source.
2. Declare required version/format/layout/optimization rows, then provide an adapter for each.
3. Use `Supported`, `Refused` with a diagnostic code, or `Unrepresentable` with
   an explanation. Missing rows and duplicate rows fail; there are no ignored
   tests for unrepresentable encodings.
4. Run each row through `GenerationConformance.run`. A CLI adapter can return
   the published, ordered file contents to the same runner.

`rental.total` runs classic JSON versions 1, 2 and 3 and classic/v4 typed models
against the same expected total of 15000 cents, with optimization on and off.
The wire inventory also runs daily-rate and scalar cases through JSON and Ion
text, binary and trees. Do not copy a consumer or introduce a version-specific
expected value. Cases unique to a model family belong in this inventory with
explicit applicability, using the same compiler and diagnostic assertions.

To exercise a published CLI, set `MORPHIR_GENERATION_CLI` to its absolute
executable path and run `Morphir.Tooling.Tests`. The existing generation BDD,
command and wire tests use that executable. The wire adapter reads its JSON
report and ordered source files, then passes them to the same
`GenerationConformance.run` and FSI runner. This is also the trimmed/Native AOT
gate; it does not have separate version or runtime expectations. Unset the
variable to use the ordinary development adapters.
