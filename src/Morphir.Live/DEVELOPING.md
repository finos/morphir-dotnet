# Morphir.Live - Development Guide

Complete guide for developing Morphir.Live, including setup, workflows, HMR configuration, and troubleshooting.

## Table of Contents

- [Development Environment](#development-environment)
- [Getting Started](#getting-started)
- [Hot Module Reload (HMR)](#hot-module-reload-hmr)
- [Development Workflow](#development-workflow)
- [Testing](#testing)
- [Debugging](#debugging)
- [Build & Deploy](#build--deploy)
- [Troubleshooting](#troubleshooting)
- [Tips & Tricks](#tips--tricks)
- [FAQ](#faq)

## Development Environment

### Required Tools

| Tool | Version | Purpose |
|------|---------|---------|
| **.NET SDK** | 10.0.101+ | Build and run |
| **Node.js** | 18+ (optional) | For npm-based tooling |
| **Git** | Latest | Version control |

### Recommended IDE Setup

**Visual Studio Code:**
```bash
# Install recommended extensions
code --install-extension Ionide.Ionide-fsharp
code --install-extension ms-dotnettools.csharp
code --install-extension editorconfig.editorconfig
```

**Visual Studio 2022:**
- Install **.NET desktop development** workload
- Install **ASP.NET and web development** workload

**JetBrains Rider:**
- Built-in F# support
- Excellent Blazor debugging

### Environment Variables

Create `.env` file (optional):
```bash
# Development settings
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=https://localhost:5001;http://localhost:5000

# HMR settings (if using Fun.Blazor CLI)
FUN_BLAZOR_HMR_ENABLED=true
FUN_BLAZOR_HMR_PORT=8091
```

## Getting Started

### Clone & Build

```bash
# Clone repository
git clone https://github.com/finos/morphir-dotnet.git
cd morphir-dotnet/src/Morphir.Live

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
dotnet run
```

### Project Structure

```
Morphir.Live/
├── Components/              # Reusable UI components
│   └── Layout.fs           # Main layout with AppBar
├── Pages/                  # Application pages
│   ├── Index.fs            # Home page
│   ├── Counter.fs          # Example page
│   └── NotFound.fs         # 404 page
├── wwwroot/                # Static files
│   ├── index.html          # HTML host
│   ├── favicon.svg         # Icon
│   └── css/                # Custom styles (if any)
├── App.fs                  # Router configuration
├── Program.fs              # Entry point
├── Morphir.Live.fsproj     # Project file
├── README.md               # Overview
├── CONTRIBUTING.md         # Contribution guide
└── DEVELOPING.md           # This file
```

### Dependencies

Check `Directory.Packages.props` at repository root for centralized version management:

```xml
<PackageVersion Include="Fun.Blazor" Version="4.1.9" />
<PackageVersion Include="Fun.Blazor.MudBlazor" Version="8.15.0" />
<PackageVersion Include="MudBlazor" Version="8.15.0" />
```

## Hot Module Reload (HMR)

Hot Module Reload allows you to see code changes instantly without full page reloads.

### Built-in .NET Hot Reload

**.NET 10 includes built-in hot reload** that works out of the box:

```bash
# Watch mode with automatic reload
dotnet watch

# Or run with hot reload
dotnet watch run
```

**Supported Changes:**
- Method bodies
- New methods
- New properties
- CSS files
- Static files in wwwroot

**Not Supported:**
- Type definitions changes
- New types
- Namespace changes
- Generic parameter changes

### Fun.Blazor Hot Reload (Advanced)

For more advanced hot reload including type-level changes, use Fun.Blazor's HMR system.

#### Installation

```bash
# Install Fun.Blazor CLI globally
dotnet tool install --global Fun.Blazor.Cli --version 4.1.9

# Verify installation
fun-blazor --version
```

#### Configuration

**1. Enable HMR in Component:**

```fsharp
// In your page component (e.g., Index.fs)
// Add this at the top of the file
// hot-reload

namespace Morphir.Live.Pages

open Microsoft.AspNetCore.Components
open Fun.Blazor

[<Route("/")>]
type Index() =
    inherit FunComponent()

    override _.Render() = fragment {
        h1 { "Morphir Live" }
        p { "This file supports hot-reload!" }
    }
```

**2. Update Program.fs (Development Only):**

```fsharp
namespace Morphir.Live

open System
open Microsoft.AspNetCore.Components.WebAssembly.Hosting
open Fun.Blazor
open MudBlazor.Services

module Program =

    [<EntryPoint>]
    let main args =
        let builder = WebAssemblyHostBuilder.CreateDefault(args)

        // Register MudBlazor services
        builder.Services.AddMudServices() |> ignore

        // Add root component
#if DEBUG
        // Use hot-reload component in development
        builder.RootComponents.Add<Fun.Blazor.HotReloadComponent>("#app")
#else
        // Use normal component in production
        builder.RootComponents.Add<Morphir.Live.Routes>("#app")
#endif

        builder.Build().RunAsync() |> ignore
        0
```

**3. Run HMR Watcher:**

```bash
# Terminal 1: Run the application
dotnet watch

# Terminal 2: Run Fun.Blazor HMR watcher
fun-blazor watch Morphir.Live.fsproj --server http://localhost:5000
```

#### HMR Workflow

1. **Make changes** to files marked with `// hot-reload`
2. **Save the file**
3. **Watch CLI** detects change and sends updates
4. **Browser updates** without full reload
5. **State preserved** (where possible)

#### HMR Limitations

- **Slow for large changes**: JSON serialization can take 5-10 seconds
- **Syntax tree conversion**: Not all F# syntax is supported
- **File annotation required**: Must add `// hot-reload` to each file
- **WebAssembly**: Slower than Blazor Server due to serialization

#### HMR Tips

- **Mark only actively edited files** with `// hot-reload`
- **Remove annotation** when done editing to improve performance
- **Use .NET hot reload** for method-level changes (faster)
- **Use Fun.Blazor HMR** for type-level changes

## Development Workflow

### Daily Development

**Standard Workflow:**
```bash
# Start with watch mode
dotnet watch

# Make changes to code
# Browser reloads automatically

# Run tests in another terminal
dotnet test --watch
```

**With Fun.Blazor HMR:**
```bash
# Terminal 1: Run app
dotnet watch

# Terminal 2: Run HMR watcher
fun-blazor watch Morphir.Live.fsproj --server http://localhost:5000

# Terminal 3: Run tests
dotnet test --watch
```

### Adding a New Page

1. **Create the page file:**
   ```fsharp
   // Pages/MyNewPage.fs
   namespace Morphir.Live.Pages

   open Microsoft.AspNetCore.Components
   open Fun.Blazor
   open MudBlazor

   /// <summary>
   /// Description of what this page does
   /// </summary>
   [<Route("/my-new-page")>]
   type MyNewPage() =
       inherit FunComponent()

       override _.Render() = fragment {
           MudText'() {
               Typo Typo.h3
               "My New Page"
           }
       }
   ```

2. **Add to project file** (`Morphir.Live.fsproj`):
   ```xml
   <Compile Include="Pages/MyNewPage.fs" />
   ```

3. **Add navigation** (in Layout.fs or nav component):
   ```fsharp
   MudNavLink'() {
       Href "/my-new-page"
       "My New Page"
   }
   ```

### Adding a Component

1. **Create component:**
   ```fsharp
   // Components/MyComponent.fs
   namespace Morphir.Live.Components

   open Fun.Blazor
   open MudBlazor

   /// <summary>
   /// Reusable card component
   /// </summary>
   module MyComponent =

       let create (title: string) (content: NodeRenderFragment list) =
           MudCard'() {
               MudCardHeader'() {
                   MudText'() {
                       Typo Typo.h6
                       title
                   }
               }
               MudCardContent'() {
                   childContent content
               }
           }
   ```

2. **Use in pages:**
   ```fsharp
   open Morphir.Live.Components

   override _.Render() = fragment {
       MyComponent.create "Title" [
           MudText'() { "Content here" }
       ]
   }
   ```

## Testing

### Running Tests

```bash
# Run all tests
dotnet test

# Run tests with watch mode
dotnet test --watch

# Run specific test
dotnet test --filter "FullyQualifiedName~MyTestMethod"

# Run with coverage
dotnet test /p:CollectCoverage=true
```

### Writing Component Tests

```fsharp
namespace Morphir.Live.Tests

open TUnit.Core
open FluentAssertions
open Bunit
open MudBlazor.Services
open Microsoft.Extensions.DependencyInjection

type MyComponentTests() =

    // Helper to setup test context with MudBlazor
    let createContext() =
        let ctx = new TestContext()
        ctx.Services.AddMudServices() |> ignore
        // Mock MudBlazor's JavaScript interop
        ctx.JSInterop.SetupVoid("watchDarkThemeMedia", fun _ -> true) |> ignore
        ctx

    [<Test>]
    member _.MyComponent_Should_Render_Title() =
        use ctx = createContext()

        // Arrange & Act
        let cut = ctx.RenderComponent<MyComponent>()

        // Assert
        let markup = cut.Markup
        markup.Should().Contain("Expected Title") |> ignore

    [<Test>]
    member _.MyComponent_Should_Handle_Click() =
        use ctx = createContext()
        let cut = ctx.RenderComponent<MyComponent>()

        // Find button and click it
        let button = cut.Find("button")
        button.Click()

        // Assert state changed
        let markup = cut.Markup
        markup.Should().Contain("Clicked!") |> ignore
```

### Test Best Practices

- **Arrange-Act-Assert** pattern
- **One assertion per test** (generally)
- **Descriptive test names**: `Method_Scenario_ExpectedResult`
- **Mock external dependencies**
- **Test edge cases** and error conditions

## Debugging

### Browser DevTools

**F12 Developer Tools:**
- **Console**: View logs and errors
- **Network**: Monitor API calls
- **Application > Storage**: Inspect LocalStorage
- **Sources**: Set breakpoints (with source maps)

### Visual Studio / Rider

**Debugging Blazor WASM:**
1. Set breakpoint in code
2. Press F5 to start debugging
3. Browser opens with debugger attached
4. Breakpoints hit in IDE

**Debug Configuration:**
```json
// .vscode/launch.json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": ".NET Core Launch (Blazor)",
            "type": "blazorwasm",
            "request": "launch",
            "cwd": "${workspaceFolder}/src/Morphir.Live",
            "browser": "chrome"
        }
    ]
}
```

### Logging

```fsharp
open Microsoft.Extensions.Logging

type MyComponent(logger: ILogger<MyComponent>) =
    inherit FunComponent()

    override _.Render() = fragment {
        button {
            onclick (fun _ ->
                logger.LogInformation("Button clicked!")
                logger.LogDebug("Debug info: {Value}", someValue)
            )
            "Click Me"
        }
    }
```

**View logs in browser console:**
- F12 > Console
- Filter by log level

## Build & Deploy

### Development Build

```bash
dotnet build
```

### Release Build

```bash
dotnet publish -c Release
```

**Output:** `bin/Release/net10.0/publish/wwwroot/`

### Deployment

**Static Hosting (GitHub Pages, Netlify, Vercel):**

1. **Build for production:**
   ```bash
   dotnet publish -c Release -o out
   ```

2. **Deploy wwwroot folder:**
   ```bash
   # The deployable files are in
   out/wwwroot/
   ```

3. **Configure base path** (if needed):
   ```xml
   <!-- In Morphir.Live.fsproj -->
   <PropertyGroup>
     <StaticWebAssetBasePath>/my-app</StaticWebAssetBasePath>
   </PropertyGroup>
   ```

**Docker:**
```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Morphir.Live/Morphir.Live.fsproj -c Release -o /app/publish

FROM nginx:alpine
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY nginx.conf /etc/nginx/nginx.conf
```

## Troubleshooting

### Common Issues

#### Build Errors

**Error: Package version conflicts**
```bash
# Solution: Restore with locked mode disabled
dotnet restore --no-lock
dotnet build
```

**Error: F# compilation fails**
```bash
# Solution: Clean and rebuild
dotnet clean
dotnet build
```

#### Runtime Errors

**Error: MudBlazor components not styled**
```html
<!-- Solution: Verify CSS is loaded in index.html -->
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
```

**Error: JavaScript interop fails**
```html
<!-- Solution: Verify JS is loaded -->
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

#### HMR Issues

**HMR not working:**
1. Check `// hot-reload` annotation at top of file
2. Verify Fun.Blazor CLI is running
3. Check browser console for errors
4. Restart both app and CLI watcher

**Slow HMR updates:**
- Mark fewer files with `// hot-reload`
- Use .NET hot reload for simple changes
- Consider Blazor Server for development (faster HMR)

#### Test Failures

**bUnit tests fail with JSInterop errors:**
```fsharp
// Solution: Mock JavaScript interop
ctx.JSInterop.SetupVoid("watchDarkThemeMedia", fun _ -> true) |> ignore
```

**Tests fail with service not registered:**
```fsharp
// Solution: Register MudBlazor services
ctx.Services.AddMudServices() |> ignore
```

### Performance Issues

**Slow initial load:**
- Enable Blazor WebAssembly compression
- Use lazy loading for large features
- Optimize images and assets

**Large bundle size:**
```xml
<!-- Enable trimming and AOT -->
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>link</TrimMode>
</PropertyGroup>
```

## Tips & Tricks

### Fun.Blazor Shortcuts

```fsharp
// Use childContent for cleaner nesting
MudCard'() {
    childContent [
        MudCardHeader'() { ... }
        MudCardContent'() { ... }
    ]
}

// Use fragments for multiple root elements
fragment {
    div { "First" }
    div { "Second" }
}

// Conditional rendering
if condition then
    MudAlert'() { ... }
```

### MudBlazor Tips

```fsharp
// Icons
Icons.Material.Filled.Menu
Icons.Material.Outlined.Info
Icons.Custom.Brands.GitHub

// Colors
Color.Primary
Color.Secondary
Color.Success
Color.Error
Color.Warning
Color.Info

// Sizes
Size.Small
Size.Medium
Size.Large

// Variants
Variant.Filled
Variant.Outlined
Variant.Text
```

### Keyboard Shortcuts

**VS Code:**
- `Ctrl+Shift+P`: Command palette
- `F5`: Start debugging
- `Ctrl+K Ctrl+C`: Comment
- `Ctrl+K Ctrl+U`: Uncomment

**Rider:**
- `Alt+Enter`: Quick fixes
- `Ctrl+Shift+A`: Find action
- `F5`: Debug
- `Shift+F10`: Run

## FAQ

### Q: Should I use .NET hot reload or Fun.Blazor HMR?

**A:** Use **.NET hot reload** (`dotnet watch`) for:
- Method-level changes
- Quick iterations
- Most day-to-day development

Use **Fun.Blazor HMR** for:
- Type-level changes
- Adding new types
- Experimenting with structure

### Q: Why are my tests failing in CI but passing locally?

**A:** Common causes:
- Different .NET SDK versions
- Missing service registrations
- JavaScript interop not mocked
- Time-dependent tests

### Q: How do I debug Blazor WASM in production?

**A:** Production debugging is limited. Best practices:
- Use logging (`ILogger`)
- Enable source maps in Release (for staging)
- Use browser DevTools console
- Implement error boundaries

### Q: Can I use C# libraries with Fun.Blazor?

**A:** Yes! F# interoperates seamlessly with C#:
```fsharp
open MyCompany.CSharpLibrary

let result = CSharpClass.Method(arg)
```

### Q: How do I optimize bundle size?

**A:**
1. Enable trimming and AOT
2. Use lazy loading
3. Remove unused dependencies
4. Compress with Brotli
5. Use CDN for large libraries

### Q: What's the difference between Blazor Server and WASM?

**Blazor Server:**
- ✅ Smaller initial download
- ✅ Faster startup
- ❌ Requires server connection
- ❌ Higher latency

**Blazor WASM (our choice):**
- ✅ No server required
- ✅ Offline capable
- ✅ Better scalability
- ❌ Larger initial download
- ❌ Slower startup

## Resources

### Documentation
- [Fun.Blazor Docs](https://slaveoftime.github.io/Fun.Blazor.Docs/)
- [MudBlazor Docs](https://mudblazor.com/)
- [Blazor Docs](https://learn.microsoft.com/en-us/aspnet/core/blazor/)
- [F# Docs](https://learn.microsoft.com/en-us/dotnet/fsharp/)

### Community
- [FINOS Slack #morphir](https://finos-lf.slack.com/archives/C01L4TPQTKH)
- [GitHub Discussions](https://github.com/finos/morphir-dotnet/discussions)
- [Fun.Blazor GitHub](https://github.com/slaveOftime/Fun.Blazor)

### Tools
- [Fun.Blazor CLI](https://www.nuget.org/packages/Fun.Blazor.Cli/)
- [Ionide (VS Code F#)](https://ionide.io/)
- [FSharpLint](https://fsprojects.github.io/FSharpLint/)

---

**Happy Developing! 🚀**

For questions or issues, open a [GitHub Issue](https://github.com/finos/morphir-dotnet/issues) or join us on [FINOS Slack](https://finos-lf.slack.com/).
