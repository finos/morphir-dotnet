# Embedded fonts

The three fonts are copied unchanged from diagrams.mbt at commit
`60a8dd19f386b6fc65e9ddf5ddccec213c622d07`, `pkgs/diagrams-raster/fonts/default/`. diagrams.mbt
made them with `mizchi/font@0.7.4 subset_font` from the Noto fonts of
[notofonts/noto-fonts](https://github.com/notofonts/noto-fonts) at commit
`ffebf8c1ee449e544955a7e813c54f9b73848eac`, keeping these code points (inclusive): U+0020–U+052F,
U+2000–U+206F, U+2190–U+21FF, U+2400–U+243F, U+2500–U+25FF and U+2610–U+2612. A character outside
them, or absent from the source font, has no glyph and stops an export with `raster/missing-glyph`.

| File | Source | SHA-256 |
| --- | --- | --- |
| `regular.ttf` | `hinted/ttf/NotoSansMono/NotoSansMono-Regular.ttf` | `496b3e880274c721bc89a5a7c0ad6d70449568179b201b4445fd280b950ec439` |
| `bold.ttf` | `hinted/ttf/NotoSansMono/NotoSansMono-Bold.ttf` | `907cac2a1419ce8e465ffbdbeb11f56db1a6b89a72e33f1529cda3f10d1d1acd` |
| `symbols.ttf` | `hinted/ttf/NotoSansSymbols2/NotoSansSymbols2-Regular.ttf` | `5e88ba96ece392769936bf5091e34d065185096a011b5cdd388d0c14abb3f952` |

The fonts are licensed under the SIL Open Font License 1.1, whose text is `OFL.txt` beside this file.
Noto is a trademark of Google LLC.
