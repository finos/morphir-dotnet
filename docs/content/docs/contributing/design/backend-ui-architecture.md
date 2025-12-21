# Backend and UI Architecture Plan

## Overview

This document outlines the architecture for the Morphir backend API and Fun.Blazor UI projects, supporting both Server-Side Rendering (SSR) and WebAssembly (WASM) modes.

## Project Structure

```
src/
├── Morphir.Server/           # ASP.NET Core Minimal API + SignalR server
│   ├── Program.cs            # Minimal API setup with SignalR + Blazor hosting
│   ├── Hubs/                 # SignalR hubs
│   ├── Endpoints/            # Minimal API endpoints
│   └── Services/             # Business logic services
│
├── Morphir.Web/              # Fun.Blazor UI (SSR + WASM)
│   ├── Program.fs            # Blazor app entry point
│   ├── App.fs                # Root component
│   ├── Components/           # Fun.Blazor components
│   ├── Pages/                # Page components
│   ├── Shared/               # Shared components
│   └── wwwroot/              # Static assets
│
└── Morphir/                  # CLI (references Server and Web)
    └── Program.cs            # Includes "server" command

tests/
├── Morphir.Tests/            # CLI command tests
├── Morphir.Server.Tests/     # Server endpoint and hub tests
└── Morphir.Web.Tests/         # Web UI component tests
```

## Technology Stack

### Server (Morphir.Server)
- **ASP.NET Core 10** - Minimal API framework
- **SignalR** - Real-time communication
- **Foundatio.Mediator** - Message handling (already in use)
- **Blazor Hosting** - Hosts Morphir.Web Blazor app
- **C# 14** - Primary language

### Web UI (Morphir.Web)
- **Fun.Blazor** - F# DSL for Blazor
- **MudBlazor** - Material Design component library
- **Fun.Blazor.MudBlazor** - Integration package
- **Blazor** - SSR and WASM rendering modes
- **F#** - Primary language

## Architecture Decisions

### 1. Server Project (Morphir.Server)

**Structure:**
- ASP.NET Core Web Application
- Minimal API for REST endpoints
- SignalR for real-time features
- Foundatio.Mediator for handler pattern (consistent with CLI)
- CORS enabled for UI communication

**Key Features:**
- RESTful API endpoints
- SignalR hubs for real-time updates
- Integration with Morphir.Tooling for business logic
- Hosts Morphir.Web Blazor application
- Health check endpoints
- Swagger/OpenAPI documentation

### 2. Web UI Project (Morphir.Web)

**Structure:**
- Blazor Web App (supports both SSR and WASM)
- Fun.Blazor for F# DSL
- MudBlazor for UI components
- Adaptive rendering mode (SSR by default, WASM on demand)

**Key Features:**
- Server-side rendering for fast initial load
- WebAssembly for client-side interactivity
- MudBlazor Material Design components
- SignalR client integration
- Responsive design

### 3. CLI Integration

**New Command:**
- `morphir server` - Launches the backend server
- Options:
  - `--port` - Port number (default: 5000)
  - `--urls` - URLs to bind to
  - `--environment` - Development/Production

## Implementation Plan

### Phase 1: Server Project Setup ✅ COMPLETED
1. ✅ Create Morphir.Server project (renamed from Morphir.Backend)
2. ✅ Configure ASP.NET Core Minimal API
3. ✅ Add SignalR support
4. ✅ Integrate Foundatio.Mediator
5. ✅ Add health check endpoint
6. ✅ Configure CORS
7. ✅ Add server info endpoint
8. ✅ Configure Blazor hosting for Morphir.Web

### Phase 2: CLI Integration ✅ COMPLETED
1. ✅ Add "server" command to Morphir CLI
2. ✅ Launch server from CLI
3. ✅ Add configuration options (--port, --urls, --environment)
4. ✅ Update references to Morphir.Server and Morphir.Web

### Phase 3: Web UI Project Setup ✅ COMPLETED
1. ✅ Create Morphir.Web project (Blazor Web App)
2. ✅ Install Fun.Blazor and MudBlazor packages
3. ✅ Configure SSR and WASM rendering modes
4. ✅ Set up basic layout and components
5. ✅ Create initial Index page

### Phase 4: Integration ✅ COMPLETED
1. ✅ Configure Morphir.Server to host Morphir.Web
2. ✅ Set up routing (API endpoints before Blazor fallback)
3. ✅ Configure static files serving
4. ⏳ Test end-to-end communication (pending verification)

### Phase 5: Testing Projects ✅ COMPLETED
1. ✅ Create Morphir.Tests project for CLI testing
2. ✅ Create Morphir.Server.Tests project for server testing
3. ✅ Create Morphir.Web.Tests project for web UI testing
4. ✅ Add test helpers and initial test cases

## Dependencies

### Backend
- `Microsoft.AspNetCore.App` (implicit)
- `Microsoft.AspNetCore.SignalR`
- `Foundatio.Mediator` (already in solution)
- `Swashbuckle.AspNetCore` (for Swagger)

### UI
- `Fun.Blazor` (latest)
- `Fun.Blazor.MudBlazor` (latest)
- `MudBlazor` (latest)
- `Microsoft.AspNetCore.SignalR.Client`

## File Structure Details

### Morphir.Server/Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddCors();
builder.Services.AddMediator(); // Foundatio.Mediator
builder.Services.AddSingleton<SchemaLoader>();
builder.Services.AddSingleton<SchemaValidator>();

var app = builder.Build();

// Configure pipeline
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

// Map endpoints
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));
app.MapHub<MorphirHub>("/morphirHub");

app.Run();
```

### Morphir.Web/Program.fs
```fsharp
open Fun.Blazor
open MudBlazor

let builder = WebApplication.CreateBuilder(args)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()  // SSR
    .AddInteractiveWebAssemblyComponents()  // WASM

builder.Services.AddMudServices()

let app = builder.Build()
// Configure app...
```

## Test Projects

### Morphir.Tests
- **Purpose**: Unit and integration tests for CLI commands
- **Framework**: TUnit, Reqnroll (BDD)
- **Location**: `tests/Morphir.Tests/`
- **Coverage**: Command parsing, options validation, command execution

### Morphir.Server.Tests
- **Purpose**: Unit and integration tests for server endpoints and hubs
- **Framework**: TUnit, Reqnroll (BDD), WebApplicationFactory
- **Location**: `tests/Morphir.Server.Tests/`
- **Coverage**: API endpoints, SignalR hubs, Blazor hosting integration

### Morphir.Web.Tests
- **Purpose**: Unit and integration tests for web UI components
- **Framework**: Expecto, WebApplicationFactory
- **Location**: `tests/Morphir.Web.Tests/`
- **Coverage**: Component rendering, routing, page functionality

## Next Steps

1. ✅ Create server project structure
2. ✅ Create web UI project structure
3. ✅ Add CLI command for launching server
4. ✅ Implement basic health check
5. ✅ Create initial UI page
6. ✅ Create test projects
7. ⏳ Verify UI accessibility when launching via server command
8. ⏳ Add integration tests for UI accessibility

