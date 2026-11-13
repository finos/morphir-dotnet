---
title: "Getting Started"
linkTitle: "Getting Started"
weight: 10
description: "Install the Morphir toolchain, compile your first model and generate code from it"
---

## Start here

[Your first model with morphirup]({{< relref "/docs/getting-started/first-model" >}}) takes you from nothing to
generated code: install `morphirup`, set up the toolchain, compile a small F# model and generate F# from it. It works
on Windows and on Macs with Apple silicon, and needs no .NET.

[Installation]({{< relref "/docs/getting-started/installation" >}}) lists every way to install `morphirup` and the
Morphir CLI.

## Next

- [Compile F# to Morphir IR]({{< relref "/docs/getting-started/compile-fsharp" >}}): a larger rule, compiled with the
  .NET tool `dotnet-morphir`, and C# generated from it.
- [Generate F# from a Morphir model]({{< relref "/docs/getting-started/generate-fsharp" >}}): turn an IR file into F#.
- [Generate and run C#]({{< relref "/docs/getting-started/morphir-to-csharp" >}}): generate C# and call it from a C#
  program.
- [Generate F# from Morphir]({{< relref "/docs/getting-started/morphir-to-fsharp" >}}): generate F# as part of an
  ordinary build, with the Myriad plugin.
- [Validating IR files]({{< relref "/docs/getting-started/validating-ir" >}}): check an IR file against its schema.
- [Configure a workspace]({{< relref "/docs/getting-started/configuration" >}}): `morphir.toml`, settings and
  migrating morphir-elm projects.
- [Python WASM extension]({{< relref "/docs/getting-started/python-wasm" >}}) (experimental): compile and generate
  Python.

When something goes wrong, see [Troubleshooting]({{< relref "/docs/getting-started/troubleshooting" >}}).
