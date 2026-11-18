# RFC 8785 test vectors

The JSON Canonicalization Scheme (JCS) writer of `Morphir.Models` (`Morphir.Json.CanonicalJson`) is tested against the examples of RFC 8785 itself, so a bug in the writer cannot change what the tests expect. Do not edit the file by hand. `.gitattributes` keeps its bytes exact, and tests check its hash.

`rfc8785.txt` is the plain-text RFC as the RFC Editor publishes it, downloaded unchanged on 2026-11-17 from <https://www.rfc-editor.org/rfc/rfc8785.txt> (UTF-8 with a byte-order mark, LF line ends). The IETF Trust's legal provisions allow the RFC to be copied whole and unmodified.

| File | Source | SHA-256 |
| --- | --- | --- |
| `rfc8785.txt` | <https://www.rfc-editor.org/rfc/rfc8785.txt> | `63d52294eb0e3f0014174288186d388b4ddbf2c67d1ce8af1d9726eb0c3ab240` |

`FixtureCatalog.Rfc8785` in `tests/Morphir.TestSupport` reads three sets of vectors from the text, and raises when the text does not have the form it expects:

| Vectors | Section of the RFC |
| --- | --- |
| Each row of Table 1: an IEEE 754 double in hexadecimal and its JSON form, empty for NaN and Infinity | Appendix B, "Number Serialization Samples" |
| The parsed sample object and the UTF-8 bytes of its canonical form | Sections 3.2.2 and 3.2.4 |
| The sorting sample object and the order of its values after sorting | Section 3.2.3 |
