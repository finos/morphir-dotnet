# Provenance

The fixture contains the byte-exact input and, for evaluation tests, the
expected N-Triples of each active entry in the
[W3C RDF 1.1 XML Syntax manifest](https://github.com/w3c/rdf-tests/blob/56076c0efec4c353efcae2971be8f6005294ac64/rdf/rdf11/rdf-xml/manifest.ttl)
at revision `56076c0efec4c353efcae2971be8f6005294ac64`. The manifest lists 166
active tests: 126 evaluation tests and 40 negative syntax tests. Seven
withdrawn entries that the manifest comments out are not included. The entries
keep the manifest order and are embedded as base64 to preserve line endings and
character references. The fixture records the original path and SHA-256 of
every file.

Each test's document base IRI is the value of `base` in the fixture followed by
the test's `action` path, as the manifest's `mf:assumedTestBase` defines.

## Excluded tests

None. No input at this revision declares a document type, so every test runs
with DTD processing prohibited. The reader refuses document type declarations;
a later revision that adds a test that needs one lists it here by name with
that reason.

| File | SHA-256 |
| --- | --- |
| `rdf11-xml.json` | `0e7b96c043203942b71a49d0f0499d1880fee58de16ab6482a436a9b4b77307d` |

Copyright © World Wide Web Consortium and the RDF test suite contributors.

The upstream tests are distributed under both the
[W3C test suite license](https://www.w3.org/copyright/test-suite-license-2023/)
and the [W3C 3-clause BSD license](https://www.w3.org/copyright/3-clause-bsd-license-2008/).
This repository uses the BSD terms for software development.

## W3C BSD test suite license

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

- Redistributions of works must retain the original copyright notice, this
  list of conditions and the following disclaimer.
- Redistributions in binary form must reproduce the original copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.
- Neither the name of W3C nor the names of its contributors may be used to
  endorse or promote products derived from this work without specific prior
  written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED.

IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
