# Generation conformance tests

`GenerationCases` owns behavioral expectations. `GenerationConformance` applies
them to representation adapters and checks the required matrix.
`GenerationCompiler` compiles and executes every successful result with FSI.
Existing generator tests call the same compiler helper.

To extend a case:

1. Specify the consumer and expected output independently of generated source.
2. Declare required version/format/layout rows, then provide an adapter for each.
3. Use `Supported`, `Refused` with a diagnostic code, or `Unrepresentable` with
   an explanation. Missing rows and duplicate rows fail; there are no ignored
   tests for unrepresentable encodings.
4. Run each row through `GenerationConformance.run`. A CLI adapter can return
   the published, ordered file contents to the same runner.

`rental.total` currently runs classic JSON versions 1, 2 and 3 against the same
expected total of 15000 cents. Add v4 and Ion adapters to this case as those
paths become available. Do not copy its consumer or introduce a version-specific
expected value. Cases unique to a model family belong in this inventory with
explicit applicability, using the same compiler and diagnostic assertions.
