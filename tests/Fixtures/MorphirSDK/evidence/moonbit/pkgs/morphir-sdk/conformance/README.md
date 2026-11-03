# SDK conformance corpus

The [binding inventory](bindings.json) comes from `finos/morphir-elm` commit `bc99af69a8b24d391311fae3822a87eafef3c334`. It maps the 248 declared values in Basics, String, List, Maybe, Result, Dict, Set, Char, Int, and Decimal to typed MoonBit functions and Scheme adapters.

## Coverage

| Classification | Values | Cases | Evidence |
| --- | ---: | ---: | --- |
| Executed Elm oracle | 245 | 310 | [Elm fixtures](elm_oracle.json), [MoonBit tests](../../morphir-scheme/backend/sdk_oracle_wbtest.mbt) |
| Exact integer extension | 2 Int64 values, plus other integer operations | 14 | [Math fixtures](exact_integer_extension.json), [MoonBit tests](../../morphir-scheme/backend/sdk_exact_integer_wbtest.mbt) |
| Uninhabited | 1 `Basics.never` | 0 | Explicit adapter error and inventory status |

The 310 Elm cases include one normal case for each executable value and 65 additional boundary cases. The MoonBit test compares the corresponding Scheme SDK result to the pinned Elm result. This is example coverage, not a proof for every value in each function's domain. The typed SDK module tests also check collection immutability, stable sorting, arithmetic errors, and text behavior.

The oracle stores each Elm result as `Debug.toString` text inside JSON. This keeps `NaN` and infinities explicit instead of passing them through JSON numbers. Decimal results are projected through `Morphir.SDK.Decimal.toString`; Dict and Set results through ordered `toList`; fixed-width integer results through their `fromInt` conversions. Those projections avoid making opaque Elm internals part of the compatibility contract. The matching Scheme test renderer applies the same projections.

`Basics.never` has no input value, so the oracle cannot call it. The pinned `Morphir.SDK.Int.toInt64` compiles its lower bound into `--9223372036854775808`, which is invalid JavaScript; calling either Int64 conversion links that expression. The fixture marks both unexecuted in Elm. The mathematical extension corpus checks signed 64-bit boundaries and overflow on all four MoonBit targets.

General Integer arithmetic beyond JavaScript's exact range is an approved Morphir extension. The extension fixture uses Python arbitrary-precision arithmetic as its oracle and labels it separately from Elm output. Integer zero divisors raise `SdkError.DivisionByZero` by design; Elm's JavaScript behavior is not used as the oracle for those cases. The SDK also treats nonfinite Float-to-Integer and Float-to-Decimal conversion as errors. Its Unicode case tables are pinned and locale independent.

## Regenerate

Use a checkout containing the pinned commit and the cached Elm packages `elm/core` 1.0.5, `elm/json` 1.1.3, `elm/regex` 1.0.0, and `chain-partners/elm-bignum` 1.0.1. `run_elm_oracle.py` reads the exact commit with `git show`, compiles a temporary Elm 0.19.1 worker, executes it with Node, and removes the temporary files.

```sh
python3 pkgs/morphir-sdk/conformance/run_elm_oracle.py --elm-checkout /path/to/morphir-elm
python3 pkgs/morphir-sdk/conformance/generate_exact_integer_extension.py
python3 pkgs/morphir-sdk/conformance/generate_inventory.py --elm-checkout /path/to/morphir-elm
python3 pkgs/morphir-sdk/conformance/generate_scheme_registry.py
python3 pkgs/morphir-sdk/conformance/generate_moonbit_oracle_tests.py
moon fmt
moon test --target native pkgs/morphir-scheme/backend
moon test --target js pkgs/morphir-scheme/backend
moon test --target wasm pkgs/morphir-scheme/backend
moon test --target wasm-gc pkgs/morphir-scheme/backend
```

`oracle_cases.py`, `oracle_boundary_cases.py`, and `oracle_scheme_cases.py` hold the reproducible Elm and Scheme input expressions. The generated MoonBit tests live in `pkgs/morphir-scheme/backend`. They require no file reads at test runtime.
