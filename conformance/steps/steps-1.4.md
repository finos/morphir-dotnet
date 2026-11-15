# MCK seed step vocabulary 1.4

Version 1.4 is version 1.1 for the C# frontend suite. Nothing changes for the F# frontend suite, which keeps reading
versions 1.0 and 1.1.

`Morphir.Conformance.Corpus.read` reads vocabulary 1.4 for the suite `frontend-csharp`. The suite lives under
`suites/frontend-csharp/`, declares `@suite:frontend-csharp`, and uses stable `frontend-csharp-NNNN` scenario ids. The
rules of [vocabulary 1.1](steps-1.md) apply, with these differences:

| Step text | Difference |
| --- | --- |
| `the C# package "<package>" exposing "<modules>"` | First step; it replaces `the F# package ...`. The suite `frontend-csharp` refuses a package step of another language, and the suite `frontend-fsharp` refuses this one. The module is a dotted module name such as `Rates.Pricing`. |
| `the source "<uri>":` | The docstring has the content type `csharp`, not `fsharp`. |

The matrix of the suite is the three input kinds and the current IR only: every Examples row has the version `4`. The
C# frontend writes the current IR, and the middle end of the package upgrades older models, so the suite has no case for
IR versions 1 to 3.

Positions in a diagnostic range are zero-based lines and UTF-16 characters, as in the F# suite. A refusal case holds the
code and the range of its one diagnostic. An analyzer diagnostic has the range that Roslyn gives its location: the
construct, the identifier of a declaration, or the accessor keyword, as the case says.
