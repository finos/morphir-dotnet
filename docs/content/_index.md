---
title: "Morphir .NET"
linkTitle: "Home"
---

{{< blocks/cover title="Morphir .NET" image_anchor="top" height="full" >}}
<p class="lead mt-4">
<strong>Your business logic, liberated.</strong><br>
.NET bindings, libraries, and tooling for the Morphir ecosystem—bringing portable, technology-agnostic domain models to .NET developers.
</p>
<a class="btn btn-lg btn-primary me-3 mb-4" href="{{< relref "/docs" >}}">
Get Started <i class="fas fa-arrow-alt-circle-right ms-2"></i>
</a>
<a class="btn btn-lg btn-secondary me-3 mb-4" href="https://github.com/finos/morphir-dotnet">
GitHub <i class="fab fa-github ms-2 "></i>
</a>
<p class="lead mt-2">
A <a href="https://www.finos.org/">FINOS</a> hosted project | Apache 2.0 License
</p>
{{< blocks/link-down color="info" >}}
{{< /blocks/cover >}}


{{< blocks/lead color="primary" >}}

## What is Morphir?

**Morphir** is a multi-language system that captures your application's domain model and business logic in a technology-agnostic format. Write once, deploy anywhere—from databases to APIs to documentation.

**Morphir .NET** brings this power to the .NET ecosystem with native C# and F# libraries, CLI tools, and seamless integration with your existing workflows.

{{< /blocks/lead >}}


{{< blocks/section color="dark" type="row" >}}

{{% blocks/feature icon="fa-solid fa-shield-halved" title="Eliminate Technical Debt" %}}
Standardized IR (Intermediate Representation) enables safe, automated refactoring. Your business logic stays clean and maintainable as technology evolves.
{{% /blocks/feature %}}

{{% blocks/feature icon="fa-solid fa-bolt" title="Increase Agility" %}}
Adapt quickly to changing requirements. Generate code for multiple targets from a single source of truth—databases, APIs, documentation, and more.
{{% /blocks/feature %}}

{{% blocks/feature icon="fa-solid fa-check-double" title="Ensure Correctness" %}}
Immutability-first design with algebraic data types that make illegal states unrepresentable. Catch errors at compile time, not runtime.
{{% /blocks/feature %}}

{{< /blocks/section >}}


{{< blocks/section color="white" type="row" >}}

{{% blocks/feature icon="fa-solid fa-code" title="Pure Domain Models" %}}
Design with immutable records and discriminated unions. Express complex business rules clearly while keeping side effects at the edges.

[Learn more]({{< relref "/docs/guides" >}})
{{% /blocks/feature %}}

{{% blocks/feature icon="fa-solid fa-arrows-rotate" title="IR Compatibility" %}}
Full compatibility with Morphir IR and JSON formats. Interoperate seamlessly with morphir-elm, morphir-jvm, and the broader Morphir ecosystem.

[Explore the spec]({{< relref "/docs/spec" >}})
{{% /blocks/feature %}}

{{% blocks/feature icon="fa-solid fa-terminal" title="Powerful CLI" %}}
The `dotnet-morphir` CLI validates, transforms, and generates code from Morphir IR. Integrate into your build pipelines with ease.

[CLI reference]({{< relref "/docs/cli" >}})
{{% /blocks/feature %}}

{{< /blocks/section >}}


{{< blocks/section color="info" >}}

<div class="col-12">
<h2 class="text-center pb-3">Quick Start</h2>
</div>

<div class="col-lg-6 mx-auto">

```bash
# Install the Morphir CLI
dotnet tool install -g Morphir

# Get information about your workspace
morphir info

# Validate Morphir IR
morphir verify model.json

# Run a Morphir plugin
morphir run <plugin-path>
```

</div>

<div class="col-12 text-center mt-4">
<a class="btn btn-lg btn-light" href="{{< relref "/docs/getting-started" >}}">
Full installation guide <i class="fas fa-arrow-right ms-2"></i>
</a>
</div>

{{< /blocks/section >}}


{{< blocks/section color="dark" type="row" >}}

{{% blocks/feature icon="fa-brands fa-github" title="Contributions Welcome!" url="https://github.com/finos/morphir-dotnet" url_text="Contribute on GitHub" %}}
We welcome contributions from the community! Check out our [good first issues](https://github.com/finos/morphir-dotnet/labels/good%20first%20issue) to get started.
{{% /blocks/feature %}}

{{% blocks/feature icon="fa-solid fa-comments" title="Join the Community" url="https://github.com/finos/morphir-dotnet/discussions" url_text="Join discussions" %}}
Ask questions, share ideas, and connect with other Morphir users and contributors in our GitHub Discussions.
{{% /blocks/feature %}}

{{% blocks/feature icon="fa-solid fa-book" title="Learn More" url="https://morphir.finos.org/" url_text="Visit morphir.finos.org" %}}
Explore the full Morphir ecosystem including morphir-elm, morphir-jvm, and comprehensive documentation.
{{% /blocks/feature %}}

{{< /blocks/section >}}


{{< blocks/section color="primary" >}}

<div class="col-12 text-center">
<h2>Part of the FINOS Ecosystem</h2>
<p class="lead mt-3">
Morphir is a project of the <a href="https://www.finos.org/" class="text-white"><strong>Fintech Open Source Foundation (FINOS)</strong></a>,
dedicated to building open source solutions for financial services.
</p>
<a href="https://www.finos.org/" class="mt-4 d-inline-block">
<img src="https://www.finos.org/hubfs/FINOS/finos-logo/FINOS_Icon_Wordmark_Name_White.svg" alt="FINOS" style="max-width: 200px;">
</a>
</div>

{{< /blocks/section >}}









