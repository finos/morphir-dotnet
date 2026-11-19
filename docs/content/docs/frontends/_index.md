---
title: "Frontends"
linkTitle: "Frontends"
weight: 28
description: "Frontends compile source code in a programming language into a Morphir IR model."
---

A frontend reads source documents in one programming language and writes a
Morphir IR model. A backend does the opposite: it reads a model and generates
code.

| Language | Frontend | Status |
| --- | --- | --- |
| C# | [C# frontend](csharp/) | In process in every offering, the Native AOT `morphir` included. See [compile]({{< relref "/docs/cli/compile" >}}). |
| F# | [F# frontend](fsharp/) | In process in `dotnet-morphir compile`, and as the MEP guest `morphir-fsharp-frontend` for `morphir compile --extension`. See [compile]({{< relref "/docs/cli/compile" >}}). |
