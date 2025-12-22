# Contributing to Morphir.Live

Thank you for your interest in contributing to Morphir.Live! This document provides guidelines and instructions for contributing to this project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Testing Requirements](#testing-requirements)
- [Commit Guidelines](#commit-guidelines)
- [Pull Request Process](#pull-request-process)
- [Architecture Guidelines](#architecture-guidelines)

## Code of Conduct

This project follows the [FINOS Code of Conduct](https://github.com/finos/.github/blob/main/CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code.

## Getting Started

### Prerequisites

- **.NET 10 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Git** - Version control
- **IDE** - VS Code, Visual Studio, or Rider recommended
- **F# knowledge** - Familiarity with functional programming

### Initial Setup

1. **Fork and clone the repository:**
   ```bash
   git clone https://github.com/YOUR_USERNAME/morphir-dotnet.git
   cd morphir-dotnet
   ```

2. **Navigate to Morphir.Live:**
   ```bash
   cd src/Morphir.Live
   ```

3. **Restore dependencies:**
   ```bash
   dotnet restore
   ```

4. **Build the project:**
   ```bash
   dotnet build
   ```

5. **Run tests:**
   ```bash
   cd ../../tests/Morphir.Live.Tests
   dotnet test
   ```

6. **Run the application:**
   ```bash
   cd ../../src/Morphir.Live
   dotnet run
   ```

See [DEVELOPING.md](./DEVELOPING.md) for detailed development setup.

## Development Workflow

### Branching Strategy

- **`main`** - Stable, production-ready code
- **`feature/*`** - New features
- **`fix/*`** - Bug fixes
- **`docs/*`** - Documentation updates

### Workflow Steps

1. **Create a feature branch:**
   ```bash
   git checkout -b feature/my-new-feature
   ```

2. **Make your changes** following coding standards

3. **Write tests** for your changes

4. **Run tests locally:**
   ```bash
   dotnet test
   ```

5. **Format code:**
   ```bash
   dotnet format
   ```

6. **Commit with conventional commit format** (see below)

7. **Push and create a pull request**

## Coding Standards

### F# Style Guide

Follow the project's [F# Coding Guide](../../docs/contributing/fsharp-coding-guide.md). Key points:

**Naming Conventions:**
- **PascalCase**: Types, modules, DUs, properties
- **camelCase**: Functions, values, parameters
- **UPPER_CASE**: Constants

**F# Best Practices:**
```fsharp
// ✅ GOOD: Immutable by default
let name = "Morphir"

// ✅ GOOD: Explicit mutation
let mutable count = 0
count <- count + 1

// ✅ GOOD: Pattern matching
match result with
| Ok value -> printfn "Success: %A" value
| Error err -> printfn "Error: %s" err

// ❌ BAD: Nullable types
let name: string = null  // Avoid nulls

// ❌ BAD: Exceptions for control flow
try
    doSomething()
with ex ->
    handleError()  // Use Result<'T, 'Error> instead
```

### Fun.Blazor Component Style

**Component Structure:**
```fsharp
namespace Morphir.Live.Pages

open Microsoft.AspNetCore.Components
open Fun.Blazor
open MudBlazor

/// <summary>
/// Page description here
/// </summary>
[<Route("/my-page")>]
type MyPage() =
    inherit FunComponent()

    // Component state
    let mutable count = 0

    override _.Render() = fragment {
        MudCard'() {
            MudCardContent'() {
                MudText'() {
                    Typo Typo.h5
                    $"Count: {count}"
                }
                MudButton'() {
                    Variant Variant.Filled
                    Color Color.Primary
                    OnClick (fun _ -> count <- count + 1)
                    "Increment"
                }
            }
        }
    }
```

**Key Patterns:**
- Use `fragment { }` for root render
- Single-apostrophe for MudBlazor: `MudButton'()`
- Properties before children in CE blocks
- Use `childContent [...]` for nested components

### Code Organization

```
Components/       # Reusable UI components
  ├── Layout.fs   # Layouts
  └── Shared.fs   # Shared components

Pages/            # Route-able pages
  ├── Index.fs    # Home page
  └── *.fs        # Other pages

Models/           # Domain models (if needed)
Utils/            # Helper functions
```

### XML Documentation

All public types and functions must have XML documentation:

```fsharp
/// <summary>
/// Validates a Morphir IR type expression
/// </summary>
/// <param name="typeExpr">The type expression to validate</param>
/// <returns>Validation result with errors if any</returns>
let validateType (typeExpr: TypeExpr) : Result<unit, string list> =
    // Implementation
```

## Testing Requirements

### Test Framework

- **TUnit** - Test framework
- **bUnit** - Blazor component testing
- **FluentAssertions** - Assertion library

### Test Structure

```fsharp
namespace Morphir.Live.Tests

open TUnit.Core
open FluentAssertions
open Bunit
open MudBlazor.Services
open Microsoft.Extensions.DependencyInjection

type MyComponentTests() =

    [<Test>]
    member _.Component_Should_Render_Correctly() =
        use ctx = new TestContext()

        // Setup
        ctx.Services.AddMudServices() |> ignore
        ctx.JSInterop.SetupVoid("watchDarkThemeMedia", fun _ -> true) |> ignore

        // Act
        let cut = ctx.RenderComponent<MyComponent>()

        // Assert
        let markup = cut.Markup
        markup.Should().NotBeNullOrEmpty() |> ignore
        (markup.Contains("expected text")).Should().BeTrue() |> ignore
```

### Test Guidelines

1. **Unit tests** for all business logic
2. **Component tests** for UI components
3. **Integration tests** for page flows
4. **Test naming**: `Method_Scenario_ExpectedBehavior`
5. **Arrange-Act-Assert** pattern
6. **Mock JSInterop** for MudBlazor components

### Coverage Requirements

- **Minimum**: 80% code coverage
- **Target**: 90%+ for new code
- **Required**: All public APIs must be tested

## Commit Guidelines

### Conventional Commits

Use [Conventional Commits](https://www.conventionalcommits.org/) format:

```
<type>(<scope>): <description>

[optional body]

[optional footer(s)]
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation only
- `style`: Code style (formatting, etc.)
- `refactor`: Code refactoring
- `test`: Adding or updating tests
- `chore`: Build process, dependencies, etc.

**Examples:**
```
feat(ui): add IR visualization component

- Implemented tree view for Morphir IR
- Added expand/collapse functionality
- Tests for component interaction

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

```
fix(routing): correct NotFound page navigation

Fixed issue where 404 page wouldn't display for invalid routes

Closes #123

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

**IMPORTANT - CLA Compliance:**

⚠️ **NEVER include `Co-Authored-By: Claude <noreply@anthropic.com>` in commits**

Our Contributor License Agreement (CLA) does NOT support AI assistants as co-authors. Use the footer format instead:

✅ **Correct Attribution:**
```
feat: add new feature

Description of feature

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

❌ **Incorrect (DO NOT USE):**
```
feat: add new feature

Co-Authored-By: Claude <noreply@anthropic.com>
```

## Pull Request Process

### Before Submitting

1. ✅ **Tests pass**: `dotnet test`
2. ✅ **Code formatted**: `dotnet format`
3. ✅ **Builds successfully**: `dotnet build`
4. ✅ **Coverage maintained**: Check coverage report
5. ✅ **Docs updated**: Update README if needed
6. ✅ **Changelog updated**: Add entry to CHANGELOG.md (if applicable)

### PR Template

```markdown
## Description
Brief description of changes

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Testing
- [ ] Unit tests added/updated
- [ ] Component tests added/updated
- [ ] Manual testing completed

## Checklist
- [ ] Code follows F# style guide
- [ ] Tests pass locally
- [ ] Code formatted with dotnet format
- [ ] Documentation updated
- [ ] Commits follow conventional commit format
```

### Review Process

1. **Automated checks** must pass
2. **At least one approval** from maintainer
3. **No unresolved comments**
4. **Up-to-date with main branch**

### Merging

- **Squash and merge** for multiple commits
- **Rebase and merge** for single, clean commits
- **Merge commit** for feature branches (rare)

## Architecture Guidelines

### Component Design

**Stateless Components:**
```fsharp
// Pure, reusable components
let createCard (title: string) (content: NodeRenderFragment list) =
    MudCard'() {
        MudCardHeader'() {
            MudText'() { Typo Typo.h6; title }
        }
        MudCardContent'() {
            childContent content
        }
    }
```

**Stateful Components:**
```fsharp
// Components with local state
type Counter() =
    inherit FunComponent()

    let mutable count = 0

    override _.Render() = fragment {
        MudButton'() {
            OnClick (fun _ -> count <- count + 1)
            $"Count: {count}"
        }
    }
```

### State Management

For now, use local component state. Future considerations:
- **Elmish** - For complex state management
- **SignalR** - For real-time updates
- **LocalStorage** - For persistence

### Performance

- **Lazy loading** - Load features on demand
- **Virtual scrolling** - For large lists
- **Memoization** - Cache expensive computations
- **Code splitting** - Separate bundles for pages

### Accessibility

- Use **semantic HTML** where appropriate
- Provide **ARIA labels** for interactive elements
- Support **keyboard navigation**
- Test with **screen readers**

### Security

- **No secrets in code** - Use environment variables
- **Validate inputs** - Client and server-side
- **XSS protection** - Sanitize user content
- **CORS configuration** - Restrict origins

## Getting Help

### Resources

- **Documentation**: [README.md](./README.md), [DEVELOPING.md](./DEVELOPING.md)
- **Issues**: [GitHub Issues](https://github.com/finos/morphir-dotnet/issues)
- **Discussions**: [GitHub Discussions](https://github.com/finos/morphir-dotnet/discussions)
- **FINOS Slack**: [Join #morphir channel](https://finos-lf.slack.com/archives/C01L4TPQTKH)

### Questions?

- Open a **discussion** for questions
- Create an **issue** for bugs
- Join **FINOS Slack** for real-time chat

## Recognition

Contributors will be recognized in:
- **CONTRIBUTORS.md** file
- **Release notes**
- **Project documentation**

Thank you for contributing to Morphir.Live! 🎉
