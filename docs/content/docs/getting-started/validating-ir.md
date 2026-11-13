---
title: "Validating IR Files"
linkTitle: "Validating IR"
weight: 20
description: "Check a Morphir IR file with morphir validate"
---

`morphir validate` reads a Morphir IR file and checks it. Use it on IR that `compile` wrote, or on IR from another
tool. The commands below run the Morphir CLI through `morphirup`, as in
[Your first model with morphirup]({{< relref "/docs/getting-started/first-model" >}}). With the .NET tool, write
`dotnet-morphir` in place of `morphirup run --`.

## Check a file

In the walkthrough's project, after `morphirup run -- compile`:

```shell
morphirup run -- validate --input .morphir/out/compile.dest/morphir-ir.json
```

```text
0 errors, 0 warnings, 0 info, 0 hints. Analyzers: 1 ran, 0 not run, 0 failed.
```

The exit code is 0 when there are no errors, so you can use the command in a script or a CI job. The checks find
type mismatches, such as a string where the model declares an int.

For a report that a script can read, add `--json`:

```shell
morphirup run -- validate --input .morphir/out/compile.dest/morphir-ir.json --json
```

```json
{
  "host": "morphir",
  "success": true,
  "exitCode": 0,
  ...
  "analyzers": [
    {
      "id": "morphir.type-validator",
      "status": "ran"
    }
  ],
  "diagnostics": []
}
```

## When the file can't be read

If the file is not IR that Morphir can read, `validate` stops with exit code 2 and says where the problem is. Here
`formatVersion` is the string `"3"` instead of the number `3`:

```text
Error: unsupported-version: ...\bad-ir.json at $.formatVersion: Expected a classic IR version: integer 1, 2 or 3, or release 3.0.x or 3.1.x.
```

[Troubleshooting]({{< relref "/docs/getting-started/troubleshooting#validating-ir" >}}) lists the usual causes.

## Check against the JSON schema

`morphir ir verify <file>` checks a file against the JSON schema of IR formats 1, 2 and 3. Today it also reports
errors for IR that `compile` writes, in format 4 and in format 3, so use `validate` for those files. See the
[ir verify reference]({{< relref "/docs/cli/ir-verify" >}}).

## Next steps

- [`validate` reference]({{< relref "/docs/cli/validate" >}}): options, analyzers and exit codes.
- [Generate F# from a Morphir model]({{< relref "/docs/getting-started/generate-fsharp" >}}).
