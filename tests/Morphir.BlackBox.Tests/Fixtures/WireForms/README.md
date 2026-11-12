# Wire forms of the daily rate model

One small model (`car/rental`, module `inventory`, value `daily-rate`) in each wire form that `morphir generate`
admits. The scenario `Each admitted wire form generates through the CLI` of `Integration/Generation.feature` uses
them.

| File | Source |
| --- | --- |
| `daily-rate-1.json`, `daily-rate-2.json` | `examples/car-rental/models/car-rental-v1.json` and `car-rental-v2.json`. The project file links them; they are not copies, so they are not in this folder. |
| `daily-rate-3.json` | `tests/Fixtures/MorphirIon/pairs/v3-daily-rate.json`, as a link of the project file. |
| `daily-rate-3.0.0.json`, `daily-rate-3.1.0.json` | The same model with the format version as that text. |
| `daily-rate-4.json` | `tests/Fixtures/MorphirIR/daily-rate-v4.json`, as a link of the project file. |
| `daily-rate-4.0.0.json` | The same model with the format version as that text. |
| `daily-rate-<version>.text.unusual` | `tests/Fixtures/MorphirIon/pairs/v3-daily-rate.ion` with that format version. The extension is not one that names a format. |
| `daily-rate-<version>.binary.unusual` | The same Ion document in the binary encoding. |
| `daily-rate-<version>.tree/` | `tests/Fixtures/MorphirIon/trees/v3-daily-rate` with that format version. |

The files were written once with `GenerationInputFixtures.DailyRate` of `tests/Morphir.Tooling.Tests`, which makes
the same inputs for the test that compares the output of the CLI with the generator
(`CSharpCliGenerationTests.EveryAdmittedWireFormUsesTheRealProviderAndMatchesCoreBytes`). When that helper changes
a wire form, write the files again.
