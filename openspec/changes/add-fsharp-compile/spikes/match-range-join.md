# Match-range join spike, 2026-11-07

Status: the original FSF-2 assumption is disproved. This failing probe is retained
as historical evidence. Revised task 3.1 is completed by the
[syntax-led reconstruction proof](syntax-led-reconstruction.md); no production
lowering is implemented from the disproved rule.

## Reproduction

```console
dotnet run --project spikes/FSharpMatchJoin -- --sequenced
```

This is a deliberately failing design probe outside the solution and production
test target. It tests the exact FSF-2 claim, rather than silently replacing it
with another algorithm. The probe has no child processes or Morphir configuration
access. Source files live in disposable `Morphir.TestSupport.TestSandbox` trees.
Reflection is used only by the spike's syntax visitor.

The probe uses FSharp.Compiler.Service 43.12.403, .NET SDK 10.0.403, the repository's
FSharp.Core 11.0.100 and `--optimize-`. All eleven input programs type-check. The
exact whole-match/decision-tree join passes only the `function` case and fails
the other ten cases.

## Observations

| Source case | Typed representation observed | Exact FSF-2 join |
| --- | --- | --- |
| Option constructor with wildcard fallback | No `DecisionTree` exposed | Fails |
| Variable pattern | No `DecisionTree` exposed | Fails |
| Integer literal and wildcard | No `DecisionTree` exposed | Fails |
| Tuple destructuring | No `DecisionTree` exposed | Fails |
| Ordered union constructors and literal subpattern | Tree range equals the subject range | Fails |
| Empty list and cons | Tree range equals the subject range | Fails |
| Cons with bound tail | Tree range equals the subject range | Fails |
| Constructor with `as` binding | Tree range equals the subject range | Fails |
| Unit pattern | No `DecisionTree` exposed | Fails |
| `function` expression | Whole range and clause target ranges match | Passes |
| Nested matches | Outer tree uses its subject range; its first target range differs from the whole nested clause body | Fails |

For the ordered-union example, source coordinates are one-based lines and
zero-based, end-exclusive UTF-16 columns, as reported by FCS:

```text
source match:   (4, 21)–(4, 92)
typed tree:    (4, 27)–(4, 33)  // the subject, "status"
clause bodies: (4, 53)–(4, 54), (4, 69)–(4, 74), (4, 90)–(4, 92)
typed targets: (4, 53)–(4, 54), (4, 69)–(4, 74), (4, 90)–(4, 92)
```

Matching the subject range repairs that example, but cannot repair cases with
no exposed decision tree, or the nested-body range mismatch. The absence of
guards and OR patterns is therefore insufficient to prove the planned join.
This is observable without enabling optimization.

## Compiler-service baseline

The repository currently pins FSharp.Compiler.Service 34.1.1. The initial
standalone probe loaded that exact assembly and attempted to check a union and
three-clause match against the hosting runtime's trusted platform assemblies.
It failed before source analysis because it could not read the bundled modern
FSharp.Core metadata:

```text
FSharp.Core.optdata not found alongside FSharp.Core
BuildFrameworkTcImports: no successful import of .../FSharp.Core.dll
```

The newer compiler service checks the sources successfully, but its package
declares an exact FSharp.Core 10.1.403 dependency. Using the repository's
FSharp.Core 11.0.100 produces NU1608. The spike records that warning; it does not
suppress it or change either central package pin. A compatible, supported
compiler-service/Core pairing must be chosen before production integration.

## Replacement investigation

Preserve the supported pattern set, clause order, source locations and native
builder architecture. Investigate syntax-led match reconstruction with typed
symbol and expression information, including reduced-away matches and nested
clause bodies. Prove it against these eleven cases before replacing FSF-2.

Do not narrow the supported subset or emit flattened matches as an implicit
workaround. If the proposed reconstruction cannot retain inferred types and
evaluation structure, return to design review with that evidence.

The authorized replacement is now recorded in [FSF-2](../design.html#fsf-2) and
validated by the [syntax-led proof](syntax-led-reconstruction.md).
