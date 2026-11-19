# Icon provenance

`icons.json.gz` is written by `scripts/build-diagram-icons.fsx` from the icon catalogue of
[diagrams.mbt at 60a8dd1](https://github.com/moonrockz/diagrams.mbt/tree/60a8dd19f386b6fc65e9ddf5ddccec213c622d07/pkgs/diagrams-svg/icons),
which takes them from ZenUML core 3.47.9. The script checks the SHA-256 of every raw source asset against the
catalogue before it copies the normalized vector trees, unchanged in shape and colour.

| File | Keys | Assets | SHA-256 |
| --- | --- | --- | --- |
| `icons.json.gz` | 493 | 473 | `eb0097ee152cbb25e8c231c0e991d8d80e8c0aa5c4553f3a7cdf234f1584d895` |

| Family | Keys | Source and terms |
| --- | --- | --- |
| Amazon Web Services | 461 | AWS Architecture Icons, <https://aws.amazon.com/architecture/icons/> |
| Microsoft Azure | 10 | Azure architecture icons, <https://learn.microsoft.com/en-us/azure/architecture/icons/> |
| Google Cloud | 17 | Google Cloud icons, <https://cloud.google.com/icons> |
| UML (actor, boundary, control, database, entity) | 5 | ZenUML, MIT, © 2022 Peng Xiao |

Drawn for this library in `Icons.fs`, under the repository's Apache-2.0 licence:

- The 29 technology keys (such as `client`, `server`, `kubernetes`, `redis`): diagrams.mbt records no per-icon
  licence for their SVG Repo art, so they are not copied. Product keys are drawn as neutral badges with initials,
  which do not imitate the product's logo.
- `externalsystem`: the upstream art is CC-BY-SA-4.0, so it is not copied and diagrams never carry share-alike terms.
- The architecture defaults `cloud`, `disk` and `internet`, and the fallback for unknown keys.
