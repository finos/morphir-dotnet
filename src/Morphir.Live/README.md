# Morphir.Live

**Interactive web-based playground for exploring and experimenting with Morphir models.**

Morphir.Live is a Blazor WebAssembly application built with F#, Fun.Blazor, and MudBlazor that provides an interactive environment for working with Morphir IR (Intermediate Representation).

## What is Morphir.Live?

Morphir.Live serves as:

- **Interactive Playground**: Explore Morphir models directly in your browser
- **Learning Tool**: Understand Morphir IR structure and transformations
- **Development Environment**: Test and validate Morphir models in real-time
- **Visualization Platform**: View and navigate Morphir IR visually

## Features

- ✅ **Modern UI**: Material Design components via MudBlazor
- ✅ **F#-First**: Functional, type-safe development with Fun.Blazor
- ✅ **WebAssembly**: Runs entirely in the browser, no server required
- ✅ **Interactive**: Real-time feedback and exploration
- ✅ **Try-Morphir**: Interactive code editor with mock transformation (UI only, real IR pipeline coming soon)
- 🚧 **IR Visualization**: (Coming Soon) Visual representation of Morphir IR
- 🚧 **Model Validation**: (Coming Soon) Validate Morphir models

## Technology Stack

| Technology | Purpose | Version |
|------------|---------|---------|
| **F#** | Primary language | 10.0 |
| **Blazor WebAssembly** | UI framework | 10.0 |
| **Fun.Blazor** | F# DSL for Blazor | 4.1.9 |
| **Fun.Blazor.MudBlazor** | Material Design wrappers | 8.15.0 |
| **MudBlazor** | Material Design components | 8.15.0 |
| **.NET** | Runtime | 10.0 |

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Any modern browser (Chrome, Firefox, Edge, Safari)

### Run Locally

```bash
# Navigate to the Morphir.Live directory
cd src/Morphir.Live

# Run the application
dotnet run

# Or use watch mode for development
dotnet watch
```

The application will be available at `https://localhost:5001` (or the port shown in the console).

### Build for Production

```bash
# Publish as a static WebAssembly site
dotnet publish -c Release

# Output will be in bin/Release/net10.0/publish/wwwroot
```

## Project Structure

```
src/Morphir.Live/
├── Components/           # Reusable UI components
│   └── Layout.fs        # Main layout with AppBar
├── Pages/               # Application pages/routes
│   ├── Index.fs         # Home page
│   ├── TryMorphir.fs    # Try-Morphir interactive editor (mock)
│   ├── Counter.fs       # Example page
│   └── NotFound.fs      # 404 page
├── wwwroot/             # Static assets
│   ├── index.html       # HTML host
│   └── favicon.svg      # Application icon
├── App.fs               # Routing configuration
├── Program.fs           # Application entry point
└── README.md            # This file
```

## Development

See [DEVELOPING.md](./DEVELOPING.md) for detailed development setup, including:
- Hot Module Reload (HMR) configuration
- Development workflow
- Troubleshooting tips
- Testing guidelines

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md) for:
- Contribution guidelines
- Code style requirements
- Pull request process
- Testing requirements

## Architecture

### Component Model

Morphir.Live uses Fun.Blazor's functional component model:

```fsharp
type MyComponent() =
    inherit FunComponent()

    override _.Render() = fragment {
        MudCard'() {
            MudCardContent'() {
                MudText'() {
                    Typo Typo.h5
                    "Hello, Morphir!"
                }
            }
        }
    }
```

### Routing

Routes are defined using F# attributes:

```fsharp
[<Route("/my-page")>]
type MyPage() =
    inherit FunComponent()

    override _.Render() = fragment {
        h1 { "My Page" }
    }
```

### State Management

Components use F# mutable variables for local state:

```fsharp
let mutable count = 0

button {
    onclick (fun _ -> count <- count + 1)
    $"Count: {count}"
}
```

## Testing

Tests are written using TUnit and bUnit:

```bash
# Run all tests
dotnet test

# Run tests from the test project directly
cd ../../tests/Morphir.Live.Tests
dotnet test
```

## Key Concepts

### Fun.Blazor DSL

Fun.Blazor provides an F#-friendly DSL for Blazor:

- **Computation Expressions**: `fragment { }`, `div { }`, etc.
- **Type Safety**: Full F# type checking
- **Functional**: Immutable by default, explicit mutation
- **Concise**: Less boilerplate than traditional Blazor

### MudBlazor Integration

MudBlazor components are accessed via Fun.Blazor.MudBlazor wrappers:

```fsharp
// Single-apostrophe syntax for components
MudButton'() {
    Variant Variant.Filled
    Color Color.Primary
    "Click Me"
}
```

## Try-Morphir Feature

The Try-Morphir page (`/try-morphir`) provides an interactive code editor for experimenting with Morphir transformations:

### Current Implementation (Mock)

- ✅ **Language Selection**: Choose between F# and Elm source languages
- ✅ **Code Editor**: Textarea-based editor for writing source code
- ✅ **Mock Transformation**: Simulated transformation output
- ✅ **Responsive Layout**: Side-by-side editor and output panels
- ✅ **Clear Disclaimers**: UI clearly indicates mock implementation

### Future Enhancements

- [ ] **Real Morphir IR Pipeline**: Actual transformation from source to Morphir IR
- [ ] **Monaco Editor Integration**: Rich code editor with syntax highlighting and IntelliSense
- [ ] **Type Inference**: Display inferred types for expressions
- [ ] **Error Reporting**: Show compilation and transformation errors
- [ ] **IR Visualization**: Visual representation of the generated IR
- [ ] **Export/Share**: Save and share code snippets

**Note**: The current implementation is a UI mockup. The transformation logic returns placeholder text. Real Morphir IR transformation will be integrated in future releases.

## Roadmap

- [ ] **Try-Morphir**: Complete real IR transformation pipeline (UI exists, backend pending)
- [ ] **IR Viewer**: Display and navigate Morphir IR structure
- [ ] **Type Explorer**: Explore Morphir types interactively
- [ ] **Model Validator**: Validate Morphir models with error reporting
- [ ] **Code Generator**: Generate code from Morphir models
- [ ] **Transformation Visualizer**: See IR transformations step-by-step
- [ ] **Example Library**: Pre-loaded Morphir examples
- [ ] **Export/Import**: Save and load Morphir models

## FAQ

### Why Fun.Blazor instead of traditional Blazor?

Fun.Blazor provides:
- **Better F# integration**: No need for awkward C# interop
- **Functional composition**: Natural F# development style
- **Type safety**: Full compiler support
- **Less boilerplate**: Computation expression syntax

### Why WebAssembly instead of Server?

WebAssembly provides:
- **No server required**: Static hosting (GitHub Pages, CDN)
- **Offline capability**: Works without network connection
- **Better performance**: No network latency
- **Scalability**: Computation runs on client

### How does Hot Module Reload work?

See [DEVELOPING.md](./DEVELOPING.md#hot-module-reload-hmr) for HMR setup and configuration.

### Can I use this in production?

Morphir.Live is currently in **early development**. The UI framework (Fun.Blazor + MudBlazor) is production-ready, but Morphir-specific features are still being built.

## Resources

### Morphir

- [Morphir Homepage](https://morphir.finos.org/)
- [Morphir GitHub](https://github.com/finos/morphir)
- [Morphir-Elm](https://github.com/finos/morphir-elm)

### Fun.Blazor

- [Fun.Blazor Docs](https://slaveoftime.github.io/Fun.Blazor.Docs/)
- [Fun.Blazor GitHub](https://github.com/slaveOftime/Fun.Blazor)
- [Fun.Blazor Samples](https://github.com/slaveOftime/Fun.Blazor.Samples)

### MudBlazor

- [MudBlazor Docs](https://mudblazor.com/)
- [MudBlazor GitHub](https://github.com/MudBlazor/MudBlazor)
- [MudBlazor Components](https://mudblazor.com/components/list)

### Blazor

- [Blazor Docs](https://learn.microsoft.com/en-us/aspnet/core/blazor/)
- [Blazor WebAssembly](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models?view=aspnetcore-8.0#blazor-webassembly)

## License

Copyright © 2024 FINOS - The Fintech Open Source Foundation

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

## Contact

- [FINOS Morphir](https://github.com/finos/morphir)
- [Issue Tracker](https://github.com/finos/morphir-dotnet/issues)
- [Discussions](https://github.com/finos/morphir-dotnet/discussions)
