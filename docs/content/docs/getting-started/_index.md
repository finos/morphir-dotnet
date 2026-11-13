---
title: "Getting Started"
linkTitle: "Getting Started"
weight: 10
description: "Get up and running with Morphir .NET"
---

New here? Start with [Your first model with morphirup]({{< relref "/docs/getting-started/first-model" >}}). It installs `morphirup`, sets up the Morphir toolchain, compiles a small F# model, generates F# from it and changes a rule, with no .NET install. It works on Windows today.

With the .NET tool instead, [Compile F# to Morphir IR]({{< relref "/docs/getting-started/compile-fsharp" >}}) takes five minutes: write a rule in F#, compile it, and generate C# from it.

For executable F# from a model JSON file, start with [Generate F# from Morphir]({{< relref "/docs/getting-started/morphir-to-fsharp" >}}).

## Installation

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) 10.0 or higher
- [Mono](http://www.mono-project.com/) if you're on Linux or macOS

### Install Morphir CLI

The dotnet tool `dotnet-morphir` has every command, including `compile` for F#:

```bash
dotnet tool install --global Morphir.Tool --prerelease
dotnet-morphir --version
```

The native executable `morphir` needs no .NET SDK. Download it from the
[releases page](https://github.com/finos/morphir-dotnet/releases). To compile F# with it, see
[compile F# with the native executable]({{< relref "/docs/guides/compile-with-native-morphir" >}}).

## Your First Project

### 1. Create a New Project

```bash
dotnet new console -n MyMorphirProject
cd MyMorphirProject
```

### 2. Add Morphir.Core Package

```bash
dotnet add package Morphir.Core
```

### 3. Build Your Project

```bash
dotnet build
```

## Next Steps

- Read the [workspace configuration walkthrough]({{< relref "/docs/getting-started/configuration" >}}), including its implementation status
- Learn about [Morphir IR modeling](/docs/guides/ir-modeling/)
- Explore the [API Reference](/docs/api/)
- Check out [examples and guides](/docs/guides/)


For classic project adoption, follow the [configuration walkthrough]({{< relref "/docs/getting-started/configuration" >}}#migrate-the-classic-project).
