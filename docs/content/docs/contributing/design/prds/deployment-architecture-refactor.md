---
title: "PRD: Deployment Architecture Refactor"
linkTitle: "Deployment Refactor"
description: "Refactor deployment architecture to fix packaging issues and establish changelog-driven versioning"
weight: 30
date: 2025-12-18
status: "Approved"
priority: "High"
epic: true
---

# PRD: Morphir .NET Deployment Architecture Refactor

## Executive Summary

Refactor the morphir-dotnet deployment architecture to fix critical packaging issues, separate tool distribution from executable distribution, implement comprehensive build testing, and establish changelog-driven versioning as the single source of truth.

**Problem**: The current deployment failed due to package naming mismatches (lowercase "morphir" vs "Morphir"), inconsistent tool command naming, and lack of automated testing to catch these issues before CI deployment.

**Solution**: Separate concerns into distinct projects (`Morphir.Tool` for dotnet tool, `Morphir` for executables), reorganize build system following vertical slice architecture, implement Ionide.KeepAChangelog for version management, and add comprehensive build testing infrastructure.

**Impact**: Eliminates deployment failures, provides clear distribution strategy for different user personas, enables confident releases with automated validation, and establishes maintainable build architecture.

---

## Table of Contents

1. [Background](#background)
2. [Problem Statement](#problem-statement)
3. [Goals & Non-Goals](#goals--non-goals)
4. [User Personas](#user-personas)
5. [Design Decisions](#design-decisions)
6. [Architecture](#architecture)
7. [Implementation Plan](#implementation-plan)
8. [BDD Acceptance Criteria](#bdd-acceptance-criteria)
9. [Testing Strategy](#testing-strategy)
10. [Risks & Mitigation](#risks--mitigation)
11. [Success Metrics](#success-metrics)
12. [Timeline](#timeline)
13. [References](#references)

---

## Background

### Current State

The morphir-dotnet project currently:
- Uses a single `Morphir` project for both tool and executable
- Has AssemblyName "morphir" (lowercase) causing glob pattern mismatches
- Sets version via `RELEASE_VERSION` environment variable
- Has no automated tests for packaging or deployment
- Suffers from configuration inconsistency (tool command as "morphir" vs "dotnet-morphir")

### Recent Failure

Deployment to main (run #20330271677) failed with:
```
System.Exception: Morphir tool package not found in /artifacts/packages
  at Build.<get_PublishTool>b__71_1() in Build.cs:line 462
```

**Root cause**: Build.cs searches for `Morphir.*.nupkg` (capital M) but package is named `morphir.*.nupkg` (lowercase m) due to AssemblyName mismatch.

### Research Conducted

Analyzed industry patterns including:
- Nuke build system's own packaging strategy
- Other .NET CLI tools (dotnet-format, dotnet-ef, GitVersion)
- Ionide.KeepAChangelog for changelog-driven versioning
- TestContainers for local NuGet server testing
- Keep a Changelog specification for pre-release versioning

---

## Problem Statement

### Critical Issues

1. **Package Naming Mismatch** ⚠️ BLOCKER
   - Build.cs expects `Morphir.*.nupkg`
   - Actual package: `morphir.*.nupkg`
   - Deployment fails at PublishTool step

2. **Tool Command Inconsistency**
   - Build.cs: `ToolCommandName=morphir`
   - Deprecated scripts: `ToolCommandName=dotnet-morphir`
   - Install scripts reference inconsistent command names

3. **No Build Testing**
   - No validation of package structure
   - No test of tool installation
   - Issues only discovered in CI deployment
   - Manual verification required

4. **Architectural Confusion**
   - Single project serves both tool and executable
   - Mixed concerns (dotnet tool + AOT compilation)
   - Difficult to optimize for each use case
   - Complex build configuration

5. **Version Management Fragility**
   - Manual `RELEASE_VERSION` in workflow file
   - No validation or enforcement
   - Risk of version drift between packages
   - CHANGELOG.md not connected to versions

### User Impact

**Before fix**:
- Users confused about tool command name
- Documentation doesn't match reality
- Deployment failures block releases
- Manual verification slows development

**After fix**:
- Clear persona-based installation paths
- Automated validation prevents failures
- Confident, fast releases
- Maintainable architecture

---

## Goals & Non-Goals

### Goals

✅ **Fix immediate deployment failure**
- Resolve package naming mismatch
- Successful deployment to NuGet.org and GitHub Releases

✅ **Separate concerns**
- Distinct `Morphir.Tool` project for dotnet tool
- `Morphir` project for standalone executables
- Clear boundaries and responsibilities

✅ **Implement comprehensive testing**
- Package structure validation
- Metadata correctness verification
- Local installation smoke tests
- Catch issues before CI deployment

✅ **Establish changelog-driven versioning**
- CHANGELOG.md as single source of truth
- Ionide.KeepAChangelog integration
- Support pre-release versions (alpha, beta, rc)
- Automated release preparation

✅ **Dual distribution strategy**
- NuGet tool package for .NET developers
- GitHub releases with executables for non-SDK users
- Persona-based documentation

✅ **Organize build system**
- Split Build.cs by domain (vertical slices)
- Extract helper classes for testability
- Align with Morphir.Tooling architecture
- Maintainable and scalable structure

### Non-Goals

❌ **Automated pre-release version bumping** (Phase 2, future work)
❌ **TestContainers integration** (Phase 3 of testing, when needed)
❌ **Package rename/migration** (Keeping current names for backward compatibility)
❌ **Breaking changes to public APIs** (Maintain compatibility)

---

## User Personas

### Persona 1: .NET Developer

**Profile**:
- Has .NET SDK installed (development machine)
- Uses dotnet CLI regularly
- Works with Morphir in .NET projects
- Expects standard dotnet tooling experience

**Needs**:
- `dotnet tool install -g Morphir.Tool`
- Automatic updates via `dotnet tool update`
- Integration with IDEs and build tools
- Familiar dotnet conventions

**Distribution**: NuGet.org package

**Command**: `morphir` (after tool install)

---

### Persona 2: Shell Script / Container User

**Profile**:
- Minimal environment (Alpine Linux, slim containers)
- Cannot install .NET SDK (size constraints)
- Uses Morphir as CLI utility in scripts
- Needs fast startup, small binary

**Needs**:
- Standalone executable (no SDK required)
- AOT-compiled for fast startup
- Small binary size (trimmed)
- Install via curl/wget script

**Distribution**: GitHub Releases with platform-specific executables

**Command**: `morphir` (or `./morphir-linux-x64`)

---

### Persona 3: CI/CD Pipeline

**Profile**:
- GitHub Actions, GitLab CI, Jenkins
- May or may not have .NET SDK pre-installed
- Speed and caching are priorities
- Reliability is critical

**Needs**:
- Flexible installation (either method works)
- Fast downloads and caching
- Consistent behavior across runs
- Clear error messages

**Distribution**: Either NuGet tool or GitHub release executable

**Command**: `morphir`

---

## Design Decisions

All design decisions were made interactively with stakeholders. See [Design Decision Rationale](#design-decision-rationale) for full context.

### Decision 1: Project Structure

**Decision**: Separate projects (Option B)

Create new `src/Morphir.Tool/` project for dotnet tool, keep `src/Morphir/` for standalone executable.

**Rationale**:
- Clear separation of concerns
- Industry pattern (matches Nuke, GitVersion)
- Easier to test independently
- Optimized for each use case

**Alternatives considered**:
- Keep single project with renamed package (doesn't fix architecture)
- Keep current, fix naming only (addresses symptom, not cause)

---

### Decision 2: Testing Strategy

**Decision**: Hybrid approach (Option C)

- Phase 1: Package structure + metadata validation (no Docker)
- Phase 2: Local folder smoke tests
- Phase 3: TestContainers + BaGet (future, when needed)

**Rationale**:
- Pragmatic - start simple, add complexity when needed
- Fast feedback loop (no Docker startup)
- Covers 80% of issues immediately
- Extensible for future enhancements

**Alternatives considered**:
- Folder-based only (insufficient validation)
- Full TestContainers immediately (overkill, slower)

---

### Decision 3: Build Organization

**Decision**: Split by domain + extract helpers (Option B + D hybrid)

Split Build.cs into:
- `Build.cs` - Entry point, core configuration
- `Build.Packaging.cs` - Pack targets
- `Build.Publishing.cs` - Publish targets
- `Build.Testing.cs` - Test targets
- `Helpers/` - PackageValidator, ChangelogHelper, etc.

**Rationale**:
- Aligns with vertical slice architecture (matches Morphir.Tooling)
- Clear feature boundaries
- Testable helper classes
- Scales well as features grow

**Alternatives considered**:
- Keep single file (will become unwieldy)
- Split by technical concern (doesn't match domain boundaries)

---

### Decision 4: Distribution Strategy

**Decision**: Dual distribution (Option B)

- NuGet.org: `Morphir.Tool` package (dotnet tool)
- GitHub Releases: Platform executables (linux-x64, win-x64, osx-arm64, etc.)

**Rationale**:
- Serves all user personas
- Industry standard pattern
- Optimal for each use case
- Flexible deployment options

**Alternatives considered**:
- Tool package only (excludes non-SDK users)
- Executable only (not idiomatic for .NET developers)

---

### Decision 5: Version Management

**Decision**: CHANGELOG.md as single source of truth via Ionide.KeepAChangelog (Option D)

- Use Ionide.KeepAChangelog to extract version from CHANGELOG.md
- Support pre-release versions (alpha, beta, rc, preview)
- PrepareRelease target automates [Unreleased] → [X.Y.Z] promotion
- Auto-bump pre-release based on prior pre-release type
- Git tags use `v` prefix (e.g., `v0.2.1`)

**Rationale**:
- Respects Keep a Changelog workflow
- Enforces changelog updates before release
- Supports pre-release versioning fully
- Single source of truth (no version.json needed)
- Automates tedious changelog formatting

**Alternatives considered**:
- Environment variable only (error-prone, no validation)
- version.json (duplication with CHANGELOG.md)
- GitVersion only (doesn't fit changelog-driven workflow)

---

## Architecture

### Project Structure

```
morphir-dotnet/
├── src/
│   ├── Morphir/                      # Standalone executable (AOT)
│   │   ├── Morphir.csproj
│   │   ├── Program.cs
│   │   └── (Output: morphir-{rid} executables)
│   │
│   ├── Morphir.Tool/                 # NEW - Dotnet tool (managed DLLs)
│   │   ├── Morphir.Tool.csproj
│   │   ├── Program.cs                # Thin wrapper
│   │   └── (Output: Morphir.Tool.nupkg)
│   │
│   ├── Morphir.Core/                 # Core domain
│   └── Morphir.Tooling/              # Tooling services
│
├── tests/
│   ├── Morphir.Build.Tests/          # NEW - Build system tests
│   │   ├── PackageStructureTests.cs
│   │   ├── PackageMetadataTests.cs
│   │   └── LocalInstallationTests.cs
│   ├── Morphir.Core.Tests/
│   ├── Morphir.Tooling.Tests/
│   └── Morphir.E2E.Tests/
│
├── build/
│   ├── Build.cs                      # Entry point
│   ├── Build.Packaging.cs            # NEW - Pack targets
│   ├── Build.Publishing.cs           # NEW - Publish targets
│   ├── Build.Testing.cs              # NEW - Test targets
│   └── Helpers/                      # NEW
│       ├── PackageValidator.cs
│       ├── ChangelogHelper.cs
│       └── PathHelper.cs
│
├── CHANGELOG.md                      # Single source of truth for versions
└── .github/workflows/
    └── deployment.yml                # Updated for new architecture
```

### Package Relationships

```
Morphir.Tool (NuGet package)
  └── depends on Morphir.Core
  └── depends on Morphir.Tooling

Morphir.Core (NuGet package)
  └── standalone library

Morphir.Tooling (NuGet package)
  └── depends on Morphir.Core

Morphir executables (GitHub releases)
  └── self-contained, no dependencies
  └── AOT-compiled, trimmed
```

### Build Flow

```
┌─────────────────────────────────────────────────────────────┐
│ Developer: Update CHANGELOG.md [Unreleased]                 │
│            Add changes to feature branch                     │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│ Release Prep: ./build.sh PrepareRelease --version 0.2.1     │
│               Moves [Unreleased] → [0.2.1] - YYYY-MM-DD     │
│               Creates release/0.2.1 branch                   │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│ PR to main: Code review, approval, merge                    │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│ Tag push: git tag -a v0.2.1 -m "Release 0.2.1"             │
│           git push origin v0.2.1                             │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│ CI Deployment:                                               │
│ 1. Extract version from CHANGELOG.md (0.2.1)                │
│ 2. Run build tests (validate packages)                      │
│ 3. Build Morphir.Tool.nupkg → NuGet.org                     │
│ 4. Build executables → GitHub Release v0.2.1                │
│ 5. Upload executables to release                            │
│ 6. Extract release notes from CHANGELOG.md                  │
└──────────────────────────────────────────────────────────────┘
```

### Versioning Flow

```
CHANGELOG.md [Unreleased]
  └── Developer adds changes here during development

PrepareRelease --version 0.2.1
  └── [Unreleased] → [0.2.1] - 2025-12-20
  └── Update comparison links
  └── Stage changes (manual commit)

Ionide.KeepAChangelog
  └── Parses CHANGELOG.md
  └── Extracts latest version: 0.2.1
  └── Extracts release notes for PackageReleaseNotes

Nuke Build
  └── GetVersionFromChangelog() → SemVersion 0.2.1
  └── All Pack targets use this version
  └── All packages have same version
```

### Pre-release Versioning

```
CHANGELOG.md:
## [0.2.1-alpha.1] - 2025-12-18
## [0.2.1-alpha.2] - 2025-12-19  ← Auto-bumped
## [0.2.1-beta.1] - 2025-12-20   ← Explicit release
## [0.2.1-beta.2] - 2025-12-21   ← Auto-bumped
## [0.2.1-rc.1] - 2025-12-22     ← Explicit release
## [0.2.1] - 2025-12-23          ← Final release

Auto-bump logic:
- Detect previous pre-release type (alpha/beta/preview/rc)
- Increment number: alpha.1 → alpha.2
- On new explicit type: beta.1 (resets number)
```

---

## Implementation Plan

### Phase 1: Project Structure & Build Organization (3-4 days)

**Goal**: Separate projects, reorganize build system

#### Tasks

**1.1 Create Morphir.Tool Project**
- [ ] Create `src/Morphir.Tool/` directory
- [ ] Create `Morphir.Tool.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <OutputType>Exe</OutputType>
      <PackAsTool>true</PackAsTool>
      <ToolCommandName>morphir</ToolCommandName>
      <PackageId>Morphir.Tool</PackageId>
      <IsPackable>true</IsPackable>
    </PropertyGroup>

    <ItemGroup>
      <ProjectReference Include="../Morphir.Core/Morphir.Core.csproj" />
      <ProjectReference Include="../Morphir.Tooling/Morphir.Tooling.csproj" />
    </ItemGroup>
  </Project>
  ```
- [ ] Create minimal `Program.cs`:
  ```csharp
  // Delegates to Morphir.Tooling
  return await Morphir.Tooling.CLI.RunAsync(args);
  ```
- [ ] Add to solution file

**1.2 Update Morphir Project**
- [ ] Ensure `Morphir.csproj` has `AssemblyName="morphir"` (lowercase)
- [ ] Verify `IsPackable=false` (not published to NuGet)
- [ ] Ensure AOT and trimming settings remain
- [ ] Keep current `Program.cs` unchanged

**1.3 Split Build.cs**
- [ ] Create `build/Build.Packaging.cs`:
  ```csharp
  partial class Build
  {
      Target PackLibs => _ => _...
      Target PackTool => _ => _...
      Target PackAll => _ => _...
  }
  ```
- [ ] Create `build/Build.Publishing.cs`:
  ```csharp
  partial class Build
  {
      Target PublishLibs => _ => _...
      Target PublishTool => _ => _...
      Target PublishAll => _ => _...
      Target PublishLocalLibs => _ => _...
      Target PublishLocalTool => _ => _...
  }
  ```
- [ ] Create `build/Build.Testing.cs`:
  ```csharp
  partial class Build
  {
      Target Test => _ => _...
      Target TestE2E => _ => _...
      Target TestBuild => _ => _... // NEW
      Target TestAll => _ => _...
  }
  ```
- [ ] Keep `Build.cs` as main entry with:
  - Parameters
  - Core configuration
  - Main targets (Restore, Compile, Clean)
  - CI orchestration targets

**1.4 Create Helper Classes**
- [ ] Create `build/Helpers/` directory
- [ ] Create `PackageValidator.cs`:
  ```csharp
  public static class PackageValidator
  {
      public static void ValidateToolPackage(AbsolutePath packagePath) { }
      public static void ValidateLibraryPackage(AbsolutePath packagePath) { }
  }
  ```
- [ ] Create `ChangelogHelper.cs`:
  ```csharp
  public static class ChangelogHelper
  {
      public static SemVersion GetVersionFromChangelog(AbsolutePath changelogPath) { }
      public static string GetReleaseNotes(AbsolutePath changelogPath) { }
      public static void PrepareRelease(AbsolutePath changelogPath, string version) { }
  }
  ```
- [ ] Create `PathHelper.cs`:
  ```csharp
  public static class PathHelper
  {
      public static AbsolutePath FindLatestPackage(AbsolutePath directory, string pattern) { }
  }
  ```

**1.5 Remove Deprecated Code**
- [ ] Delete `scripts/pack-tool-platform.cs`
- [ ] Delete `scripts/build-tool-dll.cs`
- [ ] Remove references from documentation
- [ ] Update `NUKE_MIGRATION.md`

**1.6 Update Build Targets**
- [ ] Fix `PackTool` to build `Morphir.Tool.csproj`:
  ```csharp
  Target PackTool => _ => _
      .DependsOn(Compile)
      .Executes(() => {
          DotNetPack(s => s
              .SetProject(RootDirectory / "src" / "Morphir.Tool" / "Morphir.Tool.csproj")
              .SetConfiguration(Configuration)
              .SetVersion(Version.ToString())
              .SetOutputDirectory(OutputDir));
      });
  ```
- [ ] Fix `PublishTool` glob pattern:
  ```csharp
  var toolPackage = OutputDir.GlobFiles("Morphir.Tool.*.nupkg")
      .FirstOrDefault();
  ```

**BDD Tests**:
```gherkin
Feature: Project structure refactor
  Scenario: Build Morphir.Tool package
    Given Morphir.Tool project exists
    When I run "./build.sh PackTool"
    Then Morphir.Tool.*.nupkg should be created
    And package should contain tools/net10.0/any/morphir.dll

  Scenario: Build split successfully
    Given Build.cs is split into partial classes
    When I run "./build.sh --help"
    Then all targets should be available
    And no build errors should occur
```

---

### Phase 2: Changelog-Driven Versioning (2-3 days)

**Goal**: Integrate Ionide.KeepAChangelog, implement PrepareRelease

#### Tasks

**2.1 Add Ionide.KeepAChangelog**
- [ ] Add package to `build/_build.csproj`:
  ```bash
  cd build
  dotnet add package Ionide.KeepAChangelog --version 0.2.0
  ```
- [ ] Add using statement to Build.cs:
  ```csharp
  using KeepAChangelogParser;
  using Semver;
  ```

**2.2 Implement Version Extraction**
- [ ] Create `ChangelogHelper.GetVersionFromChangelog()`:
  ```csharp
  public static SemVersion GetVersionFromChangelog(AbsolutePath changelogPath)
  {
      var content = File.ReadAllText(changelogPath);
      var parser = new ChangelogParser();
      var result = parser.Parse(content);

      if (!result.IsSuccess)
          throw new Exception($"Failed to parse CHANGELOG.md: {result.Error}");

      var changelog = result.Value;
      var latest = changelog.SectionCollection.FirstOrDefault()
          ?? throw new Exception("No releases found in CHANGELOG.md");

      if (!SemVersion.TryParse(latest.MarkdownVersion, SemVersionStyles.Any, out var version))
          throw new Exception($"Invalid version: {latest.MarkdownVersion}");

      return version;
  }
  ```

**2.3 Implement Release Notes Extraction**
- [ ] Create `ChangelogHelper.GetReleaseNotes()`:
  ```csharp
  public static string GetReleaseNotes(AbsolutePath changelogPath)
  {
      var content = File.ReadAllText(changelogPath);
      var parser = new ChangelogParser();
      var result = parser.Parse(content);

      if (!result.IsSuccess) return string.Empty;

      var latest = result.Value.SectionCollection.FirstOrDefault();
      if (latest == null) return string.Empty;

      var notes = new StringBuilder();
      AppendSection("Added", latest.SubSections.Added);
      AppendSection("Changed", latest.SubSections.Changed);
      // ... other sections
      return notes.ToString();
  }
  ```

**2.4 Update Build.cs to Use Changelog**
- [ ] Add property:
  ```csharp
  SemVersion Version => ChangelogHelper.GetVersionFromChangelog(ChangelogFile);
  string ReleaseNotes => ChangelogHelper.GetReleaseNotes(ChangelogFile);
  AbsolutePath ChangelogFile => RootDirectory / "CHANGELOG.md";
  ```
- [ ] Update all Pack targets to use `Version` property
- [ ] Update all Pack targets to use `ReleaseNotes` for PackageReleaseNotes

**2.5 Implement PrepareRelease Target**
- [ ] Create target in `Build.Publishing.cs`:
  ```csharp
  [Parameter("Version to release")] readonly string ReleaseVersion;

  Target PrepareRelease => _ => _
      .Description("Prepare a new release: moves [Unreleased] to [X.Y.Z]")
      .Requires(() => ReleaseVersion)
      .Executes(() =>
      {
          // 1. Validate version
          if (!SemVersion.TryParse(ReleaseVersion, out var version))
              throw new Exception($"Invalid version: {ReleaseVersion}");

          // 2. Validate [Unreleased] has content
          if (!ChangelogHelper.HasUnreleasedContent(ChangelogFile))
              throw new Exception("[Unreleased] section is empty");

          // 3. Update CHANGELOG.md
          ChangelogHelper.PrepareRelease(ChangelogFile, ReleaseVersion);

          // 4. Stage changes
          Git("add CHANGELOG.md");

          // 5. Show next steps
          Serilog.Log.Information("✓ Prepared release {0}", ReleaseVersion);
          Serilog.Log.Information("Next steps:");
          Serilog.Log.Information("  1. Review: git diff --staged");
          Serilog.Log.Information("  2. Commit: git commit -m 'chore: prepare release {0}'", ReleaseVersion);
          Serilog.Log.Information("  3. Push: git push origin release/{0}", ReleaseVersion);
          Serilog.Log.Information("  4. Create PR to main");
          Serilog.Log.Information("  5. After merge, tag: git tag -a v{0} -m 'Release {0}'", ReleaseVersion);
          Serilog.Log.Information("  6. Push tag: git push origin v{0}", ReleaseVersion);
      });
  ```

**2.6 Implement Changelog Manipulation**
- [ ] Create `ChangelogHelper.HasUnreleasedContent()`:
  ```csharp
  public static bool HasUnreleasedContent(AbsolutePath changelogPath)
  {
      var content = File.ReadAllText(changelogPath);
      var unreleasedPattern = @"\[Unreleased\][\s\S]*?(?=\[[\d\.]|\z)";
      var match = Regex.Match(content, unreleasedPattern);
      return match.Success && (match.Value.Contains("- ") || match.Value.Contains("* "));
  }
  ```
- [ ] Create `ChangelogHelper.PrepareRelease()`:
  ```csharp
  public static void PrepareRelease(AbsolutePath changelogPath, string version)
  {
      var content = File.ReadAllText(changelogPath);
      var date = DateTime.Now.ToString("yyyy-MM-dd");

      // Extract [Unreleased] content
      var unreleasedPattern = @"## \[Unreleased\](.*?)(?=## \[|$)";
      var match = Regex.Match(content, unreleasedPattern, RegexOptions.Singleline);

      if (!match.Success)
          throw new Exception("Could not find [Unreleased] section");

      var unreleasedContent = match.Groups[1].Value.Trim();

      // Create new sections
      var newUnreleased = "## [Unreleased]\n\n";
      var newRelease = $"## [{version}] - {date}\n\n{unreleasedContent}\n\n";

      // Replace [Unreleased] with both sections
      var updated = Regex.Replace(
          content,
          unreleasedPattern,
          newUnreleased + newRelease,
          RegexOptions.Singleline
      );

      // Update comparison links
      updated = UpdateComparisonLinks(updated, version);

      File.WriteAllText(changelogPath, updated);
  }
  ```

**2.7 Implement Auto Pre-release Bumping**
- [ ] Create `ChangelogHelper.GetNextPreReleaseVersion()`:
  ```csharp
  public static SemVersion GetNextPreReleaseVersion(AbsolutePath changelogPath)
  {
      var currentVersion = GetVersionFromChangelog(changelogPath);

      if (!currentVersion.IsPrerelease)
          throw new Exception("Cannot auto-bump non-prerelease version");

      // Extract pre-release type and number
      // e.g., "alpha.1" → type: "alpha", number: 1
      var prereleaseParts = currentVersion.Prerelease.Split('.');
      var type = prereleaseParts[0]; // alpha, beta, preview, rc
      var number = int.Parse(prereleaseParts.Length > 1 ? prereleaseParts[1] : "0");

      // Increment number
      number++;

      // Create new version
      var newPrerelease = $"{type}.{number}";
      return new SemVersion(
          currentVersion.Major,
          currentVersion.Minor,
          currentVersion.Patch,
          newPrerelease
      );
  }
  ```
- [ ] Create target for auto-bump (used in CI):
  ```csharp
  Target BumpPreRelease => _ => _
      .Description("Auto-bump pre-release version (CI only)")
      .Executes(() =>
      {
          var currentVersion = Version;

          if (!currentVersion.IsPrerelease)
          {
              Serilog.Log.Information("Not a pre-release, skipping auto-bump");
              return;
          }

          var nextVersion = ChangelogHelper.GetNextPreReleaseVersion(ChangelogFile);
          Serilog.Log.Information("Auto-bumping {0} → {1}", currentVersion, nextVersion);

          // Update CHANGELOG.md with empty section for next pre-release
          ChangelogHelper.AddPreReleaseSection(ChangelogFile, nextVersion.ToString());
      });
  ```

**BDD Tests**:
```gherkin
Feature: Changelog-driven versioning
  Scenario: Extract version from CHANGELOG
    Given CHANGELOG.md has [0.2.1] - 2025-12-20
    When I call GetVersionFromChangelog()
    Then version should be 0.2.1

  Scenario: Prepare release
    Given CHANGELOG.md has [Unreleased] with content
    When I run "./build.sh PrepareRelease --version 0.2.1"
    Then CHANGELOG.md should have [0.2.1] - 2025-12-20
    And [Unreleased] should be empty
    And changes should be staged

  Scenario: Block release without content
    Given CHANGELOG.md [Unreleased] is empty
    When I run "./build.sh PrepareRelease --version 0.2.1"
    Then build should fail
    And error should mention "empty"
```

---

### Phase 3: Build Testing Infrastructure (3-4 days)

**Goal**: Create comprehensive build tests

#### Tasks

**3.1 Create Test Project**
- [ ] Create `tests/Morphir.Build.Tests/` directory
- [ ] Create `Morphir.Build.Tests.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <IsPackable>false</IsPackable>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="TUnit" />
      <PackageReference Include="FluentAssertions" />
      <PackageReference Include="System.IO.Compression" />
    </ItemGroup>
  </Project>
  ```
- [ ] Create test infrastructure:
  ```csharp
  public class TestFixture
  {
      public AbsolutePath ArtifactsDir { get; }
      public AbsolutePath FindPackage(string pattern) { }
  }
  ```

**3.2 Package Structure Tests**
- [ ] Create `PackageStructureTests.cs`:
  ```csharp
  [Test]
  public async Task ToolPackage_HasCorrectStructure()
  {
      // Arrange
      var package = FindLatestPackage("Morphir.Tool.*.nupkg");

      // Act
      using var archive = ZipFile.OpenRead(package);
      var entries = archive.Entries.Select(e => e.FullName).ToList();

      // Assert
      entries.Should().Contain("tools/net10.0/any/morphir.dll");
      entries.Should().Contain("tools/net10.0/any/DotnetToolSettings.xml");
      entries.Should().Contain("tools/net10.0/any/Morphir.Core.dll");
      entries.Should().Contain("tools/net10.0/any/Morphir.Tooling.dll");
  }

  [Test]
  public async Task ToolPackage_HasCorrectToolSettings()
  {
      var package = FindLatestPackage("Morphir.Tool.*.nupkg");

      using var archive = ZipFile.OpenRead(package);
      var entry = archive.GetEntry("tools/net10.0/any/DotnetToolSettings.xml");

      using var reader = new StreamReader(entry.Open());
      var xml = await reader.ReadToEndAsync();

      xml.Should().Contain("<Command Name=\"morphir\"");
      xml.Should().Contain("EntryPoint=\"morphir.dll\"");
  }

  [Test]
  public async Task LibraryPackages_HaveCorrectStructure()
  {
      var corePackage = FindLatestPackage("Morphir.Core.*.nupkg");

      using var archive = ZipFile.OpenRead(corePackage);
      var entries = archive.Entries.Select(e => e.FullName).ToList();

      entries.Should().Contain(e => e.Contains("lib/net10.0/Morphir.Core.dll"));
      entries.Should().NotContain(e => e.Contains("tools/"));
  }
  ```

**3.3 Package Metadata Tests**
- [ ] Create `PackageMetadataTests.cs`:
  ```csharp
  [Test]
  public async Task AllPackages_HaveSameVersion()
  {
      var corePackage = FindLatestPackage("Morphir.Core.*.nupkg");
      var toolingPackage = FindLatestPackage("Morphir.Tooling.*.nupkg");
      var toolPackage = FindLatestPackage("Morphir.Tool.*.nupkg");

      var coreVersion = GetPackageVersion(corePackage);
      var toolingVersion = GetPackageVersion(toolingPackage);
      var toolVersion = GetPackageVersion(toolPackage);

      coreVersion.Should().Be(toolingVersion);
      coreVersion.Should().Be(toolVersion);
  }

  [Test]
  public async Task AllPackages_HaveVersionFromChangelog()
  {
      var changelogVersion = GetVersionFromChangelog();
      var toolPackage = FindLatestPackage("Morphir.Tool.*.nupkg");
      var packageVersion = GetPackageVersion(toolPackage);

      packageVersion.Should().Be(changelogVersion);
  }

  [Test]
  public async Task ToolPackage_HasCorrectMetadata()
  {
      var package = FindLatestPackage("Morphir.Tool.*.nupkg");
      var nuspec = GetNuspec(package);

      nuspec.Id.Should().Be("Morphir.Tool");
      nuspec.Authors.Should().Contain("FINOS");
      nuspec.License.Should().NotBeNullOrEmpty();
      nuspec.ProjectUrl.Should().Contain("morphir-dotnet");
      nuspec.PackageType.Should().Be("DotnetTool");
  }

  [Test]
  public async Task ToolPackage_HasReleaseNotes()
  {
      var package = FindLatestPackage("Morphir.Tool.*.nupkg");
      var nuspec = GetNuspec(package);

      nuspec.ReleaseNotes.Should().NotBeNullOrEmpty();
      nuspec.ReleaseNotes.Should().Contain("### "); // Has sections
  }
  ```

**3.4 Local Installation Tests (Phase 2 of testing strategy)**
- [ ] Create `LocalInstallationTests.cs`:
  ```csharp
  [Test]
  public async Task ToolPackage_InstallsFromLocalFolder()
  {
      // Arrange
      var tempDir = CreateTempDirectory();
      var localSource = Path.Combine(tempDir, "feed");
      Directory.CreateDirectory(localSource);

      var toolPackage = FindLatestPackage("Morphir.Tool.*.nupkg");
      File.Copy(toolPackage, Path.Combine(localSource, Path.GetFileName(toolPackage)));

      // Act
      var installResult = await RunDotNet(
          $"tool install --global --add-source {localSource} Morphir.Tool"
      );

      // Assert
      installResult.ExitCode.Should().Be(0);

      var versionResult = await RunDotNet("morphir --version");
      versionResult.ExitCode.Should().Be(0);
      versionResult.Output.Should().MatchRegex(@"\d+\.\d+\.\d+");

      // Cleanup
      await RunDotNet("tool uninstall --global Morphir.Tool");
      Directory.Delete(tempDir, true);
  }

  [Test]
  public async Task ToolCommand_IsAvailableAfterInstall()
  {
      // Assumes tool is installed
      var result = await RunCommand("morphir", "--help");

      result.ExitCode.Should().Be(0);
      result.Output.Should().Contain("morphir");
      result.Output.Should().Contain("Commands:");
  }
  ```

**3.5 Add TestBuild Target**
- [ ] Create target in `Build.Testing.cs`:
  ```csharp
  Target TestBuild => _ => _
      .DependsOn(PackAll)
      .Description("Run build system tests")
      .Executes(() =>
      {
          DotNetTest(s => s
              .SetProjectFile(RootDirectory / "tests" / "Morphir.Build.Tests" / "Morphir.Build.Tests.csproj")
              .SetConfiguration(Configuration)
              .EnableNoRestore()
              .EnableNoBuild());
      });

  Target TestAll => _ => _
      .DependsOn(Test, TestE2E, TestBuild)
      .Description("Run all tests");
  ```

**3.6 Integrate into CI**
- [ ] Update `.github/workflows/development.yml`:
  ```yaml
  - name: Run build tests
    run: ./build.sh TestBuild
  ```

**BDD Tests**:
```gherkin
Feature: Build testing infrastructure
  Scenario: Validate tool package structure
    Given Morphir.Tool package is built
    When I run package structure tests
    Then all required files should be present
    And tool settings should be correct

  Scenario: Validate version consistency
    Given all packages are built
    When I run metadata tests
    Then all packages should have same version
    And version should match CHANGELOG.md

  Scenario: Test local installation
    Given tool package is in local folder
    When I install tool from local source
    Then installation should succeed
    And morphir command should be available
```

---

### Phase 4: Deployment & Distribution (2-3 days)

**Goal**: Update workflows for dual distribution

#### Tasks

**4.1 Update Deployment Workflow**
- [ ] Update `.github/workflows/deployment.yml`:
  ```yaml
  name: Deployment

  on:
    push:
      tags:
        - 'v*'  # Trigger on version tags (e.g., v0.2.1)
    workflow_dispatch:
      inputs:
        release_version:
          description: 'Version to deploy (optional, reads from CHANGELOG if not provided)'
          required: false

  jobs:
    validate-version:
      runs-on: ubuntu-latest
      outputs:
        version: ${{ steps.get-version.outputs.version }}
      steps:
        - uses: actions/checkout@v4

        - name: Get version from CHANGELOG
          id: get-version
          run: |
            # Extract from tag name (v0.2.1 → 0.2.1)
            if [[ "${{ github.ref }}" == refs/tags/* ]]; then
              VERSION=${GITHUB_REF#refs/tags/v}
              echo "version=$VERSION" >> $GITHUB_OUTPUT
            elif [[ -n "${{ github.event.inputs.release_version }}" ]]; then
              echo "version=${{ github.event.inputs.release_version }}" >> $GITHUB_OUTPUT
            else
              echo "No version specified"
              exit 1
            fi

        - name: Validate version in CHANGELOG
          run: |
            VERSION=${{ steps.get-version.outputs.version }}
            if ! grep -q "\[$VERSION\]" CHANGELOG.md; then
              echo "Version $VERSION not found in CHANGELOG.md"
              exit 1
            fi

    build-executables:
      needs: validate-version
      # ... existing build-executables jobs ...

    release:
      needs: [validate-version, build-executables]
      runs-on: ubuntu-latest
      steps:
        - uses: actions/checkout@v4

        - name: Setup .NET SDK
          uses: actions/setup-dotnet@v4
          with:
            global-json-file: global.json

        - name: Restore dependencies
          run: ./build.sh Restore

        - name: Build
          run: ./build.sh Compile

        - name: Run tests
          run: ./build.sh TestAll  # Includes build tests!

        - name: Download executables
          uses: actions/download-artifact@v4

        - name: Pack packages
          run: ./build.sh PackAll

        - name: Run build tests
          run: ./build.sh TestBuild

        - name: Publish to NuGet
          run: ./build.sh PublishAll --api-key ${{ secrets.NUGET_TOKEN }}
          env:
            NUGET_TOKEN: ${{ secrets.NUGET_TOKEN }}

    create-github-release:
      needs: [validate-version, build-executables, release]
      runs-on: ubuntu-latest
      steps:
        - uses: actions/checkout@v4

        - name: Download executables
          uses: actions/download-artifact@v4
          with:
            path: artifacts/executables

        - name: Extract release notes from CHANGELOG
          id: release-notes
          run: |
            VERSION=${{ needs.validate-version.outputs.version }}
            # Extract section for this version from CHANGELOG.md
            awk '/## \['$VERSION'\]/,/## \[/ {print}' CHANGELOG.md | head -n -1 > release-notes.md

        - name: Create GitHub Release
          uses: softprops/action-gh-release@v1
          with:
            tag_name: v${{ needs.validate-version.outputs.version }}
            name: Release v${{ needs.validate-version.outputs.version }}
            body_path: release-notes.md
            files: |
              artifacts/executables/morphir-*
              artifacts/executables/morphir.exe
          env:
            GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
  ```

**4.2 Update Install Scripts**
- [ ] Verify `scripts/install-linux.sh` uses "morphir" command
- [ ] Verify `scripts/install-macos.sh` uses "morphir" command
- [ ] Verify `scripts/install-windows.ps1` uses "morphir" command
- [ ] Update download URLs to point to GitHub releases:
  ```bash
  VERSION="0.2.1"
  URL="https://github.com/finos/morphir-dotnet/releases/download/v${VERSION}/morphir-linux-x64"
  ```
- [ ] Test install scripts locally (manual)

**4.3 Validate PublishTool Target**
- [ ] Update glob pattern in `Build.Publishing.cs`:
  ```csharp
  Target PublishTool => _ => _
      .DependsOn(PackTool)
      .Description("Publish Morphir.Tool to NuGet.org")
      .Executes(() =>
      {
          if (string.IsNullOrEmpty(ApiKey))
              throw new Exception("API_KEY required");

          var toolPackage = OutputDir.GlobFiles("Morphir.Tool.*.nupkg")
              .FirstOrDefault();

          if (toolPackage == null)
              throw new Exception($"Morphir.Tool package not found in {OutputDir}");

          Serilog.Log.Information($"Publishing {toolPackage}");

          DotNetNuGetPush(s => s
              .SetTargetPath(toolPackage)
              .SetSource(NuGetSource)
              .SetApiKey(ApiKey)
              .SetSkipDuplicate(true));
      });
  ```

**BDD Tests**:
```gherkin
Feature: Deployment workflow
  Scenario: Deploy on tag push
    Given tag v0.2.1 is pushed
    When deployment workflow runs
    Then version should be extracted from CHANGELOG.md
    And packages should be built
    And build tests should run
    And packages should be published to NuGet
    And executables should be uploaded to GitHub release

  Scenario: Block deployment if version not in CHANGELOG
    Given tag v0.2.2 is pushed
    But CHANGELOG.md doesn't have [0.2.2]
    When deployment workflow runs
    Then workflow should fail
    And no packages should be published
```

---

### Phase 5: Documentation (1-2 days)

**Goal**: Comprehensive documentation for all stakeholders

#### Tasks

**5.1 Update AGENTS.md**
- [ ] Add section: "Build System Configuration"
  ```markdown
  ## Build System Configuration

  ### Nuke Parameters

  The build system uses Nuke with these parameters:

  - `--configuration`: Build configuration (Debug/Release)
  - `--version`: Version override (reads from CHANGELOG.md by default)
  - `--api-key`: NuGet API key for publishing
  - `--nuget-source`: NuGet source URL
  - `--skip-tests`: Skip test execution

  ### Environment Variables

  - `NUGET_TOKEN`: NuGet API key (CI only)
  - `CONFIGURATION`: Build configuration override
  - `MORPHIR_EXECUTABLE_PATH`: E2E test executable path
  ```

- [ ] Add section: "Changelog-Driven Versioning"
  ```markdown
  ## Changelog-Driven Versioning

  Morphir uses CHANGELOG.md as the single source of truth for versions.

  ### Version Format

  Follows [Semantic Versioning](https://semver.org/):
  - `MAJOR.MINOR.PATCH` for releases (e.g., `0.2.1`)
  - `MAJOR.MINOR.PATCH-TYPE.NUMBER` for pre-releases (e.g., `0.2.1-beta.2`)

  Supported pre-release types: alpha, beta, preview, rc

  ### Release Preparation Workflow

  1. During development, add changes to `[Unreleased]` section
  2. When ready to release, run: `./build.sh PrepareRelease --version X.Y.Z`
  3. Review staged changes: `git diff --staged`
  4. Commit: `git commit -m "chore: prepare release X.Y.Z"`
  5. Create release branch: `git checkout -b release/X.Y.Z`
  6. Push and create PR to main
  7. After PR merge, create tag: `git tag -a vX.Y.Z -m "Release X.Y.Z"`
  8. Push tag: `git push origin vX.Y.Z` (triggers deployment)
  ```

- [ ] Add section: "Dual Distribution Strategy"
  ```markdown
  ## Dual Distribution Strategy

  Morphir provides two distribution channels:

  ### NuGet Tool Package (Morphir.Tool)

  **For**: .NET developers with SDK installed
  **Install**: `dotnet tool install -g Morphir.Tool`
  **Update**: `dotnet tool update -g Morphir.Tool`
  **Command**: `morphir`

  ### Platform Executables

  **For**: Shell scripts, containers, non-.NET environments
  **Install**: Use install scripts or download from GitHub releases
  **Platforms**: linux-x64, linux-arm64, win-x64, osx-arm64
  **Command**: `morphir` or `./morphir-{platform}`
  ```

**5.2 Update CLAUDE.md**
- [ ] Add build organization guidance
- [ ] Document PrepareRelease workflow
- [ ] Add testing requirements
- [ ] Update commit message examples

**5.3 Update README.md**
- [ ] Add persona-based installation instructions:
  ```markdown
  ## Installation

  ### For .NET Developers

  If you have the .NET SDK installed:

  ```bash
  dotnet tool install -g Morphir.Tool
  morphir --version
  ```

  ### For Shell Scripts / Containers

  If you don't have .NET SDK or need a standalone executable:

  **Linux/macOS:**
  ```bash
  curl -sSL https://get.morphir.org | bash
  ```

  **Windows:**
  ```powershell
  irm https://get.morphir.org/install.ps1 | iex
  ```

  **Manual Download:**
  Download from [GitHub Releases](https://github.com/finos/morphir-dotnet/releases)
  ```

**5.4 Create DEPLOYMENT.md**
- [ ] Document release process for maintainers
- [ ] Add troubleshooting guide
- [ ] Document rollback procedures
- [ ] Add deployment checklist

**5.5 Write BDD Feature Files**
- [ ] Create `tests/Morphir.E2E.Tests/Features/ToolInstallation.feature`:
  ```gherkin
  Feature: Morphir Tool Installation
    As a .NET developer
    I want to install Morphir as a dotnet tool
    So that I can use it in my development workflow

    Scenario: Install from NuGet
      Given I am a .NET developer with SDK installed
      When I run "dotnet tool install -g Morphir.Tool"
      Then the tool should install successfully
      And I should be able to run "morphir --version"
      And the version should match CHANGELOG.md

    Scenario: Update tool
      Given Morphir.Tool is already installed
      When I run "dotnet tool update -g Morphir.Tool"
      Then the tool should update successfully
      And the new version should be active
  ```

- [ ] Create `tests/Morphir.E2E.Tests/Features/ExecutableDownload.feature`:
  ```gherkin
  Feature: Morphir Executable Download
    As a shell script user
    I want to download a standalone executable
    So that I can use Morphir without installing .NET SDK

    Scenario: Download from GitHub releases
      Given I am using a minimal container
      When I download morphir-linux-x64 from GitHub releases
      Then I should be able to run "./morphir-linux-x64 --version"
      And the version should match CHANGELOG.md

    Scenario: Install via script
      Given I have curl available
      When I run the install script
      Then morphir should be installed to /usr/local/bin
      And morphir command should be in PATH
  ```

**BDD Tests**:
```gherkin
Feature: Documentation completeness
  Scenario: All distribution methods documented
    Given README.md exists
    When I read installation instructions
    Then I should see dotnet tool installation
    And I should see executable download instructions
    And I should see persona-based recommendations

  Scenario: Release process documented
    Given AGENTS.md exists
    When I read the release preparation section
    Then I should see PrepareRelease workflow
    And I should see tag creation steps
    And I should see deployment trigger explanation
```

---

## BDD Acceptance Criteria

### Epic-Level Scenarios

```gherkin
Feature: Morphir Deployment Architecture
  As a Morphir maintainer
  I want a robust deployment architecture
  So that releases are reliable and users can install easily

Background:
  Given the morphir-dotnet repository is up to date
  And all dependencies are installed

Scenario: Successful deployment to NuGet and GitHub
  Given CHANGELOG.md has [0.2.1] - 2025-12-20
  And all changes are committed
  When I create and push tag v0.2.1
  Then deployment workflow should complete successfully
  And Morphir.Tool.0.2.1.nupkg should be published to NuGet.org
  And Morphir.Core.0.2.1.nupkg should be published to NuGet.org
  And Morphir.Tooling.0.2.1.nupkg should be published to NuGet.org
  And morphir-linux-x64 should be in GitHub release v0.2.1
  And morphir-win-x64 should be in GitHub release v0.2.1
  And morphir-osx-arm64 should be in GitHub release v0.2.1
  And release notes should match CHANGELOG.md

Scenario: Build tests catch package issues
  Given I modify package structure incorrectly
  When I run "./build.sh TestBuild"
  Then tests should fail
  And I should see clear error message
  And CI deployment should be blocked

Scenario: Version consistency across packages
  Given I prepare release 0.2.1
  When I build all packages
  Then all packages should have version 0.2.1
  And version should match CHANGELOG.md [0.2.1]
  And all package release notes should match

Scenario: .NET developer installation
  Given Morphir.Tool is published to NuGet
  When .NET developer runs "dotnet tool install -g Morphir.Tool"
  Then tool should install successfully
  And "morphir --version" should work
  And version should match published version

Scenario: Container user installation
  Given morphir-linux-x64 is in GitHub releases
  When container user downloads executable
  Then "./morphir-linux-x64 --version" should work
  And version should match release version
  And no .NET SDK should be required
```

### Component-Level Scenarios

See individual phase BDD tests in [Implementation Plan](#implementation-plan) sections.

---

## Testing Strategy

### Test Pyramid

```
         /\
        /E2E\        E2E Tests (Morphir.E2E.Tests)
       /______\      - Full tool installation workflows
      /        \     - Executable download and usage
     / Integration\  - Cross-platform verification
    /______________\
   /                \
  /   Unit Tests     \ Unit Tests (Morphir.Build.Tests)
 /____________________\ - Package structure validation
                        - Metadata correctness
                        - Version extraction
                        - Changelog parsing
```

### Test Categories

#### 1. Build System Tests (tests/Morphir.Build.Tests/)

**Package Structure Tests**:
- Validate tool package contains correct files
- Validate library packages contain correct files
- Validate DotnetToolSettings.xml correctness
- Validate no unnecessary files included

**Package Metadata Tests**:
- Version consistency across packages
- Version matches CHANGELOG.md
- Authors, license, URLs set correctly
- Release notes extracted correctly
- PackageId naming conventions

**Changelog Tests**:
- Parse valid changelog
- Extract version correctly
- Extract release notes correctly
- Validate unreleased content detection
- Test PrepareRelease transformations

**Local Installation Tests (Phase 2)**:
- Install tool from local folder
- Verify command is available
- Run --version and validate output
- Uninstall successfully

#### 2. E2E Tests (tests/Morphir.E2E.Tests/)

**Tool Installation Tests**:
- Install from NuGet feed
- Update tool
- Uninstall tool
- Verify command availability

**Executable Tests**:
- Download from GitHub releases
- Execute on each platform
- Verify version output
- Test basic commands

**Cross-Platform Tests**:
- Linux x64
- Linux ARM64
- Windows x64
- macOS ARM64

#### 3. Integration Tests

**CI Workflow Tests** (manual verification):
- Tag push triggers deployment
- Version validation passes
- Build tests run successfully
- Packages publish to NuGet
- GitHub release created with executables

**Install Script Tests** (manual verification):
- Linux install script works
- macOS install script works
- Windows install script works
- Scripts download correct version

### Coverage Targets

- **Build System**: >= 80% code coverage
- **Unit Tests**: >= 80% code coverage (existing requirement)
- **E2E Tests**: All critical user journeys covered
- **Manual Tests**: Release checklist 100% complete

### CI Integration

```yaml
# .github/workflows/development.yml
jobs:
  test:
    steps:
      - name: Run unit tests
        run: ./build.sh Test

      - name: Run build tests
        run: ./build.sh TestBuild

      - name: Run E2E tests
        run: ./build.sh TestE2E

# .github/workflows/deployment.yml
jobs:
  release:
    steps:
      - name: Run all tests
        run: ./build.sh TestAll  # Blocks deployment if tests fail
```

---

## Risks & Mitigation

### Risk 1: Version Drift Between Packages

**Risk**: Different packages published with different versions

**Impact**: HIGH - User confusion, installation failures

**Probability**: LOW (after mitigation)

**Mitigation**:
- ✅ Single source of truth (CHANGELOG.md)
- ✅ Automated extraction via Ionide.KeepAChangelog
- ✅ Build tests validate version consistency
- ✅ CI blocks if versions don't match

**Detection**: Build tests fail, CI blocks deployment

**Recovery**: Fix CHANGELOG.md, rebuild packages

---

### Risk 2: Breaking Existing Users

**Risk**: Users with current tool/executable can't upgrade

**Impact**: MEDIUM - User frustration, support burden

**Probability**: LOW

**Mitigation**:
- ✅ Keep backward compatibility (command name stays "morphir")
- ✅ Clear migration documentation
- ✅ Test installation on clean machines
- ✅ Announce changes in release notes

**Detection**: User reports, E2E tests

**Recovery**: Hotfix release, update documentation

---

### Risk 3: Build Tests Add CI Time

**Risk**: CI takes longer, slows development

**Impact**: LOW - Developer velocity

**Probability**: MEDIUM

**Mitigation**:
- ✅ Run build tests in parallel with other tests
- ✅ Cache NuGet packages
- ✅ Optimize test execution
- ✅ Phase 2/3 tests are optional (local only)

**Measurement**: Monitor CI duration, target < 10 minutes total

---

### Risk 4: Complex Release Process

**Risk**: Release preparation is error-prone

**Impact**: MEDIUM - Release delays

**Probability**: LOW (after automation)

**Mitigation**:
- ✅ Automated PrepareRelease target
- ✅ Clear documentation and checklists
- ✅ Validation steps prevent mistakes
- ✅ Dry-run capability

**Detection**: PrepareRelease validation failures

**Recovery**: Fix issues, re-run PrepareRelease

---

### Risk 5: Ionide.KeepAChangelog Bugs

**Risk**: Parser fails or extracts incorrect version

**Impact**: HIGH - Deployment failure

**Probability**: VERY LOW (mature library)

**Mitigation**:
- ✅ Comprehensive changelog validation tests
- ✅ Fallback to manual version override
- ✅ CI validation before deployment
- ✅ Monitor parser errors

**Detection**: Build tests, CI validation

**Recovery**: Manual version override, report bug upstream

---

## Success Metrics

### Immediate Success Criteria (Phase 1-2)

- [ ] Zero deployment failures due to package naming
- [ ] All packages have same version from CHANGELOG.md
- [ ] Build tests catch 100% of package structure issues
- [ ] PrepareRelease target works without errors
- [ ] Documentation covers all user personas

### Short-Term Success Criteria (Phase 3-4)

- [ ] CI deployment time < 10 minutes
- [ ] Build tests have >= 80% coverage
- [ ] GitHub releases created automatically
- [ ] Install scripts work on all platforms
- [ ] Zero user-reported installation issues

### Long-Term Success Criteria (6 months)

- [ ] Deployment success rate >= 99%
- [ ] Release preparation time < 5 minutes
- [ ] User satisfaction with installation >= 90%
- [ ] Build system maintainability score >= 8/10
- [ ] Zero security vulnerabilities in packages

### Key Performance Indicators (KPIs)

**Reliability**:
- Deployment success rate
- Build test pass rate
- Package validation pass rate

**Efficiency**:
- Average release preparation time
- CI execution time
- Time to fix deployment issues

**Quality**:
- Package structure defects found
- Version consistency violations
- User-reported installation issues

**Developer Experience**:
- Time to understand release process
- Number of manual steps required
- Documentation completeness score

---

## Timeline

### Gantt Chart

```
Phase 1: Project Structure & Build Organization [3-4 days]
├─ Create Morphir.Tool project           [1 day]
├─ Split Build.cs by domain              [1 day]
├─ Create helper classes                 [0.5 day]
├─ Remove deprecated code                [0.5 day]
└─ Update build targets                  [1 day]

Phase 2: Changelog-Driven Versioning [2-3 days]
├─ Add Ionide.KeepAChangelog             [0.5 day]
├─ Implement version extraction          [0.5 day]
├─ Implement release notes extraction    [0.5 day]
├─ Implement PrepareRelease target       [1 day]
├─ Implement changelog manipulation      [0.5 day]
└─ Implement auto pre-release bumping    [0.5 day]

Phase 3: Build Testing Infrastructure [3-4 days]
├─ Create test project                   [0.5 day]
├─ Package structure tests               [1 day]
├─ Package metadata tests                [1 day]
├─ Local installation tests              [1 day]
├─ Add TestBuild target                  [0.5 day]
└─ Integrate into CI                     [0.5 day]

Phase 4: Deployment & Distribution [2-3 days]
├─ Update deployment workflow            [1 day]
├─ Create GitHub release automation      [1 day]
├─ Update install scripts                [0.5 day]
└─ Validate PublishTool target           [0.5 day]

Phase 5: Documentation [1-2 days]
├─ Update AGENTS.md                      [0.5 day]
├─ Update CLAUDE.md                      [0.5 day]
├─ Update README.md                      [0.5 day]
├─ Create DEPLOYMENT.md                  [0.5 day]
└─ Write BDD feature files               [0.5 day]

Total: 11-16 days
```

### Milestones

**M1: Core Architecture Complete** (Day 4)
- Morphir.Tool project created
- Build.cs split and organized
- Deprecated code removed
- Build targets updated

**M2: Version Management Complete** (Day 7)
- Ionide.KeepAChangelog integrated
- PrepareRelease target working
- CHANGELOG.md is single source of truth
- Pre-release bumping implemented

**M3: Testing Infrastructure Complete** (Day 11)
- Build tests project created
- Package validation tests passing
- Local installation tests passing
- CI integration complete

**M4: Deployment Ready** (Day 14)
- Deployment workflow updated
- GitHub releases automated
- Install scripts validated
- End-to-end flow tested

**M5: Documentation Complete** (Day 16)
- All documentation updated
- BDD scenarios written
- Release process documented
- Ready for production release

---

## Design Decision Rationale

### Why Separate Projects?

**Context**: Single project tried to serve both tool and executable use cases.

**Problem**:
- Mixed concerns (tool packaging + AOT compilation)
- Complex build configuration
- Difficult to optimize for each scenario
- Package naming confusion

**Decision**: Create separate `Morphir.Tool` project

**Reasoning**:
1. **Industry pattern**: Nuke.GlobalTool, GitVersion.Tool, etc.
2. **Clear boundaries**: Tool project knows nothing about AOT
3. **Independent optimization**: Tool package can be small, executable can be trimmed
4. **Easier testing**: Can test tool installation separately from executable behavior
5. **Maintainability**: Each project has single responsibility

**Alternatives Rejected**:
- Keep single project: Doesn't address root cause
- Rename package only: Band-aid solution
- Use complex build conditions: Too fragile

---

### Why Ionide.KeepAChangelog?

**Context**: Need changelog-driven versioning with pre-release support.

**Problem**:
- Manual version management is error-prone
- CHANGELOG.md and versions can drift
- Pre-release versions not standardized
- GitVersion doesn't fit changelog-first workflow

**Decision**: Use Ionide.KeepAChangelog as single source of truth

**Reasoning**:
1. **Respects Keep a Changelog**: Already following this standard
2. **Full SemVer support**: Pre-release versions (alpha, beta, rc) via Semver library
3. **Mature library**: Used in F# ecosystem, well-tested
4. **Single source of truth**: No version.json duplication
5. **Release notes automation**: Automatically extract for packages

**Alternatives Rejected**:
- version.json: Duplication with CHANGELOG.md
- GitVersion: Doesn't fit changelog-driven approach
- Environment variable only: No validation, error-prone
- FAKE.Core.Changelog: Requires FAKE build system

---

### Why Hybrid Testing Strategy?

**Context**: Need to catch packaging issues before CI.

**Problem**:
- No automated package validation
- TestContainers adds complexity
- Want fast feedback loop

**Decision**: Phase 1 (structure/metadata), Phase 2 (local install), Phase 3 (containers)

**Reasoning**:
1. **Pragmatic**: Start simple, add complexity when needed
2. **Fast feedback**: No Docker startup for basic validation
3. **Covers 80%**: Structure/metadata tests catch most issues
4. **Incremental**: Can add TestContainers later
5. **Low barrier**: Easy for contributors to run

**Alternatives Rejected**:
- Folder-based only: Insufficient validation
- Full TestContainers immediately: Overkill, slower tests, complexity
- No tests: Unacceptable, issues found in CI only

---

### Why Split Build.cs by Domain?

**Context**: Build.cs will grow with new features.

**Problem**:
- Single 900+ line file becomes unwieldy
- Vertical slice architecture used in Morphir.Tooling
- Want consistent patterns across codebase

**Decision**: Split by domain (Packaging, Publishing, Testing) + extract helpers

**Reasoning**:
1. **Aligns with architecture**: Matches Morphir.Tooling vertical slices
2. **Clear boundaries**: Related targets grouped together
3. **Scalable**: Easy to add new domains (Documentation, Analysis)
4. **Testable**: Helper classes can be unit tested
5. **Team familiarity**: Same patterns they already use

**Alternatives Rejected**:
- Keep single file: Will become unmaintainable
- Split by technical concern: Doesn't match feature boundaries
- Vertical slice with separate files: Overkill for current size

---

### Why Dual Distribution?

**Context**: Different users have different needs.

**Problem**:
- .NET developers want dotnet tool
- Container users can't install .NET SDK
- Shell scripts need standalone executable

**Decision**: Publish both tool package and executables

**Reasoning**:
1. **Serves all personas**: No user excluded
2. **Industry standard**: How major tools distribute
3. **Optimal for each**: Tool for dev, AOT for production
4. **Flexible**: Users choose what works for them
5. **Already building both**: Just need to organize/document

**Alternatives Rejected**:
- Tool only: Excludes non-SDK users
- Executable only: Not idiomatic for .NET developers
- Force users to choose one: Why limit options?

---

## References

### Internal Documents

- [AGENTS.md](../../AGENTS.md) - Agent guidance and conventions
- [CLAUDE.md](../../CLAUDE.md) - Claude Code specific instructions
- [CHANGELOG.md](../../CHANGELOG.md) - Keep a Changelog format
- [NUKE_MIGRATION.md](../../NUKE_MIGRATION.md) - Nuke migration guide

### External References

**Morphir**:
- [Morphir Homepage](https://morphir.finos.org/)
- [morphir-elm](https://github.com/finos/morphir-elm)
- [morphir-dotnet](https://github.com/finos/morphir-dotnet)

**Standards**:
- [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)
- [Semantic Versioning](https://semver.org/spec/v2.0.0.html)

**Tools**:
- [Ionide.KeepAChangelog](https://www.nuget.org/packages/Ionide.KeepAChangelog/)
- [Nuke Build](https://nuke.build/)
- [TUnit](https://github.com/thomhurst/TUnit)

**Patterns**:
- [Nuke Global Tool](https://www.nuget.org/packages/Nuke.GlobalTool/)
- [GitVersion.Tool](https://www.nuget.org/packages/GitVersion.Tool/)

---

## Appendices

### Appendix A: Current vs Future Package Structure

**Current (Broken)**:
```
artifacts/packages/
├── morphir.0.2.0.nupkg           ← lowercase (breaks glob)
├── Morphir.Core.0.2.0.nupkg
└── Morphir.Tooling.0.2.0.nupkg
```

**Future (Fixed)**:
```
artifacts/packages/
├── Morphir.Tool.0.2.1.nupkg      ← New, capital M
├── Morphir.Core.0.2.1.nupkg
└── Morphir.Tooling.0.2.1.nupkg

artifacts/executables/
├── morphir-linux-x64             ← Standalone
├── morphir-linux-arm64
├── morphir-win-x64
└── morphir-osx-arm64
```

### Appendix B: CHANGELOG.md Format Examples

**Valid pre-release entries**:
```markdown
## [0.2.1-alpha.1] - 2025-12-18
## [0.2.1-alpha.2] - 2025-12-19
## [0.2.1-beta.1] - 2025-12-20
## [0.2.1-beta.2] - 2025-12-21
## [0.2.1-rc.1] - 2025-12-22
## [0.2.1] - 2025-12-23
```

**Invalid entries**:
```markdown
## [0.2.1-SNAPSHOT] - 2025-12-18  ❌ Not SemVer
## [0.2.1-beta] - 2025-12-19      ⚠️ Missing number (but parses)
## 0.2.1 - 2025-12-20              ❌ Missing brackets
## [0.2.1]                         ❌ Missing date
```

### Appendix C: Build Target Dependency Graph

```
CI (full pipeline)
├── Restore
├── Compile
│   └── Restore
├── Test
│   └── Compile
├── TestE2E
│   └── Compile
├── PackAll
│   ├── PackLibs
│   │   └── Compile
│   └── PackTool
│       └── Compile
├── TestBuild
│   └── PackAll
└── PublishAll
    ├── PublishLibs
    │   └── PackLibs
    └── PublishTool
        └── PackTool

PrepareRelease (standalone)
└── (validates CHANGELOG.md)

BumpPreRelease (CI only)
└── (updates CHANGELOG.md)
```

### Appendix D: File Size Estimates

**Tool Package** (~5-10 MB):
- Managed DLLs only
- Dependencies: Morphir.Core, Morphir.Tooling, WolverineFx, etc.
- No native code

**Executables** (~50-80 MB each):
- AOT-compiled native code
- Self-contained (no .NET SDK required)
- Trimmed and optimized
- Platform-specific

**Comparison**:
- NuGet tool: Fast download for developers with SDK
- Executables: Larger but work everywhere

### Appendix E: Version Comparison Matrix

| Scenario | Current | Future |
|----------|---------|--------|
| Version source | `RELEASE_VERSION` env var | CHANGELOG.md |
| Pre-release | Manual string | SemVer (alpha.1, beta.2) |
| Validation | None | Automated (build tests) |
| Consistency | Manual verification | Enforced (single source) |
| Release notes | Manual copy-paste | Auto-extracted |
| Drift risk | HIGH | LOW |

---

## Status Tracking

### Feature Status

| Feature | Status | Notes |
|---------|--------|-------|
| Morphir.Tool project | ⏳ Planned | Phase 1 |
| Build.cs split | ⏳ Planned | Phase 1 |
| Helper classes | ⏳ Planned | Phase 1 |
| Ionide.KeepAChangelog | ⏳ Planned | Phase 2 |
| PrepareRelease target | ⏳ Planned | Phase 2 |
| Build tests project | ⏳ Planned | Phase 3 |
| Package validation | ⏳ Planned | Phase 3 |
| Deployment workflow | ⏳ Planned | Phase 4 |
| GitHub releases | ⏳ Planned | Phase 4 |
| Documentation | ⏳ Planned | Phase 5 |

### Blockers

| Blocker | Impact | Resolution |
|---------|--------|------------|
| None yet | - | - |

### Decisions Pending

| Decision | Options | Status |
|----------|---------|--------|
| None | - | All approved |

---

*PRD Version: 1.0*
*Last Updated: 2025-12-18*
*Status: Approved*
*Owner: @morphir-maintainers*
