# Provenance

The fixture contains the byte-exact input and expected result of each entry in
the [W3C RDF Dataset Canonicalization (RDFC-1.0) test manifest](https://github.com/w3c/rdf-canon/blob/5ffaaa13c0e19a2a2f9e60d0ecd9efaf3edab633/tests/manifest.jsonld)
at revision `5ffaaa13c0e19a2a2f9e60d0ecd9efaf3edab633`. The W3C maintains the
RDFC-1.0 tests in the `w3c/rdf-canon` repository; `w3c/rdf-tests` does not
contain them.

The manifest has 86 tests: 64 evaluation tests with an expected canonical
N-Quads result, 21 map tests with an expected issued-identifier map, and one
negative evaluation test that must fail on its computational complexity. One
evaluation test and its map test use SHA-384; the others use SHA-256. The entries keep the
manifest order and are embedded as base64. The fixture records the original
path and SHA-256 of every file.

| File | SHA-256 |
| --- | --- |
| `rdfc10.json` | `8de10283843668593cea24874775ed1639e3438fc6020e626d5fdbd3c91059fd` |

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
