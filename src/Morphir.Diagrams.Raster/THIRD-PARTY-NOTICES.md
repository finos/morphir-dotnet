# Third-party notices

## Noto fonts

`Fonts/regular.ttf`, `Fonts/bold.ttf` and `Fonts/symbols.ttf` are subsets of Noto Sans Mono
Regular, Noto Sans Mono Bold and Noto Sans Symbols 2, Copyright 2018 The Noto Project Authors,
licensed under the SIL Open Font License, Version 1.1. The licence is in `licenses/fonts/OFL.txt`
and the sources and digests are in `licenses/fonts/PROVENANCE.md`. Noto is a trademark of Google
LLC.

## JPEG tables

The JPEG encoder's quantisation, zigzag and Huffman tables and its codec structure follow
`mizchi/image@0.4.3` (https://github.com/mizchi/image-mbt, published archive SHA-256
`324e9fac91790655d9ce67e6edc76f7831ace4ab3b197c8657aab23ae7f0cdcb`) by mizchi, as diagrams.mbt
adapts them, under the Apache License 2.0. The tables are those of ITU-T T.81 Annex K. This
library replaces the floating-point DCT and colour conversion with integer arithmetic.
