---
title: "Getting Started"
linkTitle: "Getting Started"
weight: 10
description: "Get up and running with Morphir .NET"
---

## Installation

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) 10.0 or higher
- [Mono](http://www.mono-project.com/) if you're on Linux or macOS

### Install Morphir CLI

```bash
dotnet tool install -g Morphir
```

### Verify Installation

```bash
morphir --version
```

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

- Learn about [Morphir IR modeling](/guides/ir-modeling/)
- Explore the [API Reference](/api/)
- Check out [examples and guides](/guides/)

