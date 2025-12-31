#!/usr/bin/env bash
# Create GitHub issues for F# Backend implementation
# Usage: ./scripts/create-fsharp-backend-issues.sh

set -euo pipefail

REPO="finos/morphir-dotnet"
MILESTONE="v1.0.0"

echo "Creating GitHub issues for F# Backend implementation..."
echo "Repository: $REPO"
echo ""

# Check if gh CLI is installed
if ! command -v gh &> /dev/null; then
    echo "Error: GitHub CLI (gh) is not installed."
    echo "Install it from: https://cli.github.com/"
    exit 1
fi

# Check if authenticated
if ! gh auth status &> /dev/null; then
    echo "Error: Not authenticated with GitHub CLI."
    echo "Run: gh auth login"
    exit 1
fi

echo "Creating Epic issue..."

# Issue #1: Epic
EPIC_BODY=$(cat <<'EOF'
## Vision

Enable developers to generate production-ready F# code from Morphir IR, eliminating manual translation and maintaining type safety throughout the business logic transformation.

## Goals

- ✅ Generate idiomatic F# code from Morphir IR v3
- ✅ Support all Morphir type and value constructs
- ✅ Map Morphir SDK types to F# built-in types
- ✅ Integrate with `morphir gen fsharp` CLI command
- ✅ Auto-format output using Fantomas
- ✅ Support JSON codecs and lens generation (optional)
- ✅ Achieve ≥80% test coverage
- ✅ AOT-compatible generated code

## Success Metrics

- All Morphir IR v3 constructs generate valid F# code
- Generated code compiles without errors
- Performance: < 5s for 1000 types
- Test coverage ≥ 80%
- At least 3 example projects using the backend

## Related Documents

- [PRD: F# Backend](https://github.com/finos/morphir-dotnet/blob/main/docs/design/PRD-fsharp-backend.md)
- [GitHub Issues Template](https://github.com/finos/morphir-dotnet/blob/main/docs/design/github-issues-fsharp-backend.md)

## Child Issues

- [ ] #TBD: Foundation: Project Setup and Fabulous.AST Exploration
- [ ] #TBD: Type Mapping: Morphir IR Types → F# Types
- [ ] #TBD: Value Mapping: Morphir IR Values → F# Functions
- [ ] #TBD: CLI Integration: `morphir gen fsharp` Command
- [ ] #TBD: SDK Translation: Morphir SDK → F# Standard Library
- [ ] #TBD: Advanced Features: JSON Codecs and Lenses
- [ ] #TBD: Testing and Documentation
- [ ] #TBD: Release Preparation

## Dependencies

- Fabulous.AST v1.9.0+ (already in Directory.Packages.props)
- Fantomas.Core v7.0.5+ (already in Directory.Packages.props)
- Morphir.IR.Pipeline (transformation infrastructure)

## Timeline

**Estimated Duration**: 12 weeks (8 implementation phases)
EOF
)

EPIC_NUMBER=$(gh issue create \
  --repo "$REPO" \
  --title "[EPIC] F# Code Generation Backend" \
  --body "$EPIC_BODY" \
  --label "epic,feature,backend,code-generation" \
  --milestone "$MILESTONE" \
  | grep -oP '(?<=/issues/)\d+')

echo "✅ Created Epic #$EPIC_NUMBER"
echo ""

# Issue #2: Foundation
echo "Creating Issue #2: Foundation..."

ISSUE_2_BODY=$(cat <<EOF
## Description

Set up the \`Morphir.Backends.FSharp\` project and create foundational infrastructure including Morphir-specific helpers and complete SDK type/function mappings.

## Acceptance Criteria

**Project Structure**:
- [ ] Create \`src/Morphir.Backends.FSharp/Morphir.Backends.FSharp.fsproj\`
- [ ] Create \`tests/Morphir.Backends.FSharp.Tests/Morphir.Backends.FSharp.Tests.fsproj\`
- [ ] Add projects to \`morphir-dotnet.sln\`
- [ ] Configure dependencies: Fabulous.AST, Fantomas.Core, Morphir.Models, Morphir.IR.Pipeline

**Core Files**:
- [ ] \`Helpers.fs\`: Morphir-specific DSL helpers
- [ ] \`SDK.fs\`: Complete SDK type/function mappings
- [ ] README.md with project overview

**Testing**:
- [ ] Exploratory tests with Fabulous.AST
- [ ] SDK mapping validation tests
- [ ] Code coverage ≥ 80%

## Tasks

1. Create project structure
2. Implement \`Helpers.fs\` with DSL helpers
3. Implement \`SDK.fs\` with mappings
4. Write exploratory tests
5. Document findings

## Success Criteria

- All tests pass
- Can create simple F# code using Fabulous.AST
- All Morphir SDK types mapped
- Code coverage ≥ 80%

## Epic

Part of #$EPIC_NUMBER

## Estimated Effort

2 weeks
EOF
)

ISSUE_2=$(gh issue create \
  --repo "$REPO" \
  --title "Foundation: Project Setup and Fabulous.AST Exploration" \
  --body "$ISSUE_2_BODY" \
  --label "feature,backend,fsharp,phase-1" \
  --milestone "$MILESTONE" \
  | grep -oP '(?<=/issues/)\d+')

echo "✅ Created Issue #$ISSUE_2"
echo ""

# Issue #3: Type Mapping
echo "Creating Issue #3: Type Mapping..."

ISSUE_3_BODY=$(cat <<EOF
## Description

Implement complete mapping from Morphir IR type definitions and type expressions to Fabulous.AST F# type constructs.

## Acceptance Criteria

**Mapper.fs Implementation**:
- [ ] \`mapTypeDefinition\`: Handle all Type.Definition variants
- [ ] \`mapTypeExpr\`: Handle all Type constructors
- [ ] SDK type integration
- [ ] FQName resolution

**Testing**:
- [ ] Unit tests for all Type variants
- [ ] Snapshot tests with morphir-elm examples
- [ ] Compilation tests
- [ ] Code coverage ≥ 80%

## Tasks

1. Create \`Mapper.fs\`
2. Implement type mapping functions
3. Write comprehensive unit tests (TDD)
4. Create snapshot tests
5. Validate compilation

## Success Criteria

- All Type variants mapped correctly
- All tests pass
- Generated types compile successfully
- Code coverage ≥ 80%

## Dependencies

Depends on #$ISSUE_2

## Epic

Part of #$EPIC_NUMBER

## Estimated Effort

2 weeks
EOF
)

ISSUE_3=$(gh issue create \
  --repo "$REPO" \
  --title "Type Mapping: Morphir IR Types → F# Types" \
  --body "$ISSUE_3_BODY" \
  --label "feature,backend,fsharp,phase-2" \
  --milestone "$MILESTONE" \
  | grep -oP '(?<=/issues/)\d+')

echo "✅ Created Issue #$ISSUE_3"
echo ""

# Issue #4: Value Mapping
echo "Creating Issue #4: Value Mapping..."

ISSUE_4_BODY=$(cat <<EOF
## Description

Implement complete mapping from Morphir IR value definitions and value expressions to Fabulous.AST F# function and expression constructs.

## Acceptance Criteria

**Value Mapping**:
- [ ] \`mapValueDefinition\`: Convert to F# functions
- [ ] \`mapValueExpr\`: Handle all Value expressions (16 variants)
- [ ] \`mapPattern\`: Handle all Pattern variants (8 variants)
- [ ] \`mapLiteral\`: Handle all Literal variants (6 variants)

**Testing**:
- [ ] Unit tests for all variants
- [ ] Snapshot tests
- [ ] Pattern matching exhaustiveness tests
- [ ] Curried function tests
- [ ] Code coverage ≥ 80%

## Tasks

1. Implement value definition mapping
2. Implement expression mapping
3. Implement pattern mapping
4. Implement literal mapping
5. Write comprehensive tests
6. Validate compilation and type-checking

## Success Criteria

- All Value variants mapped correctly
- Pattern matching is exhaustive
- Curried functions correct
- All tests pass
- Code coverage ≥ 80%

## Dependencies

Depends on #$ISSUE_3

## Epic

Part of #$EPIC_NUMBER

## Estimated Effort

2 weeks
EOF
)

ISSUE_4=$(gh issue create \
  --repo "$REPO" \
  --title "Value Mapping: Morphir IR Values → F# Functions" \
  --body "$ISSUE_4_BODY" \
  --label "feature,backend,fsharp,phase-3" \
  --milestone "$MILESTONE" \
  | grep -oP '(?<=/issues/)\d+')

echo "✅ Created Issue #$ISSUE_4"
echo ""

# Issue #5: CLI Integration
echo "Creating Issue #5: CLI Integration..."

ISSUE_5_BODY=$(cat <<EOF
## Description

Integrate the F# backend into the morphir CLI as a \`morphir gen fsharp\` command with full option support and pipeline integration.

## Acceptance Criteria

**CLI Implementation**:
- [ ] \`Plugin.fs\`: Pipeline plugin
- [ ] \`Generator.fs\`: Rendering functions
- [ ] \`Commands.Gen.FSharp.fs\`: CLI command handler
- [ ] All command-line options implemented
- [ ] File writing with directory creation
- [ ] Progress logging (stderr)

**Testing**:
- [ ] E2E tests for all CLI options
- [ ] File writing tests
- [ ] Error handling tests

## Example Usage

\`\`\`bash
morphir gen fsharp --namespace MyApp.Domain --output src/Generated
morphir gen fsharp --codecs --lenses
\`\`\`

## Success Criteria

- \`morphir gen fsharp\` works end-to-end
- All options functional
- Files written correctly
- Logging to stderr only

## Dependencies

Depends on #$ISSUE_4

## Epic

Part of #$EPIC_NUMBER

## Estimated Effort

1 week
EOF
)

ISSUE_5=$(gh issue create \
  --repo "$REPO" \
  --title "CLI Integration: morphir gen fsharp Command" \
  --body "$ISSUE_5_BODY" \
  --label "feature,backend,fsharp,cli,phase-4" \
  --milestone "$MILESTONE" \
  | grep -oP '(?<=/issues/)\d+')

echo "✅ Created Issue #$ISSUE_5"
echo ""

# Issue #6: SDK Translation
echo "Creating Issue #6: SDK Translation..."

ISSUE_6_BODY=$(cat <<EOF
## Description

Translate Morphir SDK function calls and operators to their F# standard library equivalents.

## Acceptance Criteria

**Function Translation**:
- [ ] Extend \`SDK.sdkFunctionMap\` with all SDK functions
- [ ] Translate List functions (map, filter, fold, etc.)
- [ ] Translate Maybe/Option functions
- [ ] Translate Result functions
- [ ] Translate String functions

**Operator Translation**:
- [ ] Arithmetic operators
- [ ] Comparison operators
- [ ] Logical operators
- [ ] List cons (\`::\`)
- [ ] String concat (\`++\`)
- [ ] Composition/pipeline operators

**Testing**:
- [ ] Unit tests for each translation
- [ ] Integration tests with morphir-elm examples
- [ ] Compilation tests

## Success Criteria

- All SDK functions translate correctly
- All operators translate correctly
- Generated code uses F# stdlib
- All tests pass

## Dependencies

Depends on #$ISSUE_5

## Epic

Part of #$EPIC_NUMBER

## Estimated Effort

1 week
EOF
)

ISSUE_6=$(gh issue create \
  --repo "$REPO" \
  --title "SDK Translation: Morphir SDK → F# Standard Library" \
  --body "$ISSUE_6_BODY" \
  --label "feature,backend,fsharp,sdk,phase-5" \
  --milestone "$MILESTONE" \
  | grep -oP '(?<=/issues/)\d+')

echo "✅ Created Issue #$ISSUE_6"
echo ""

# Issue #7: Advanced Features
echo "Creating Issue #7: Advanced Features..."

ISSUE_7_BODY=$(cat <<EOF
## Description

Implement JSON encoders/decoders (using Thoth.Json) and lens functions for nested record updates.

## Acceptance Criteria

**JSON Codecs (\`--codecs\`)**:
- [ ] Generate Thoth.Json encoders for all types
- [ ] Generate Thoth.Json decoders for all types
- [ ] Handle recursive types
- [ ] Handle polymorphic types
- [ ] Handle discriminated unions
- [ ] Place in \`Codecs\` module

**Lenses (\`--lenses\`)**:
- [ ] Generate lens type definition
- [ ] Generate field lenses for records
- [ ] Generate composition operator
- [ ] Place in \`Lenses\` module

**Testing**:
- [ ] Codec round-trip tests
- [ ] Lens law tests
- [ ] Integration tests

## Success Criteria

- Codecs handle all types correctly
- Codec round-trips pass
- Lenses satisfy laws
- All tests pass

## Dependencies

Depends on #$ISSUE_6

## Epic

Part of #$EPIC_NUMBER

## Estimated Effort

2 weeks
EOF
)

ISSUE_7=$(gh issue create \
  --repo "$REPO" \
  --title "Advanced Features: JSON Codecs and Lenses" \
  --body "$ISSUE_7_BODY" \
  --label "feature,backend,fsharp,codecs,lenses,phase-6" \
  --milestone "$MILESTONE" \
  | grep -oP '(?<=/issues/)\d+')

echo "✅ Created Issue #$ISSUE_7"
echo ""

# Issue #8: Testing and Documentation
echo "Creating Issue #8: Testing and Documentation..."

ISSUE_8_BODY=$(cat <<EOF
## Description

Create comprehensive test suite and user-facing documentation for the F# backend.

## Acceptance Criteria

**E2E Testing**:
- [ ] E2E tests with morphir-elm examples
- [ ] Full pipeline tests
- [ ] Performance benchmarks (< 5s for 1000 types)
- [ ] Memory profiling (< 500 MB)

**Documentation**:
- [ ] Getting Started Guide
- [ ] Command Reference
- [ ] Type Mapping Reference
- [ ] SDK Mapping Reference
- [ ] Advanced Features Guide
- [ ] Troubleshooting Guide

**Examples**:
- [ ] 3+ working example projects
- [ ] All examples build and run

**Coverage**:
- [ ] Code coverage ≥ 80%

## Success Criteria

- All E2E tests pass
- Documentation complete
- Examples functional
- Coverage ≥ 80%

## Dependencies

Depends on #$ISSUE_7

## Epic

Part of #$EPIC_NUMBER

## Estimated Effort

1 week
EOF
)

ISSUE_8=$(gh issue create \
  --repo "$REPO" \
  --title "Testing and Documentation" \
  --body "$ISSUE_8_BODY" \
  --label "testing,documentation,phase-7" \
  --milestone "$MILESTONE" \
  | grep -oP '(?<=/issues/)\d+')

echo "✅ Created Issue #$ISSUE_8"
echo ""

# Issue #9: Release Preparation
echo "Creating Issue #9: Release Preparation..."

ISSUE_9_BODY=$(cat <<EOF
## Description

Final polish, code review, CI/CD integration, and preparation for v1.0.0 release.

## Acceptance Criteria

**Code Quality**:
- [ ] Code review completed
- [ ] All review comments addressed
- [ ] Refactoring complete

**CI/CD**:
- [ ] Tests added to CI pipeline
- [ ] Multi-platform validation
- [ ] Coverage reporting

**AOT Compatibility**:
- [ ] Native AOT tested
- [ ] No reflection warnings
- [ ] Trimming validated
- [ ] AOT Guru review

**Release**:
- [ ] CHANGELOG updated
- [ ] Release notes drafted
- [ ] NuGet packages ready
- [ ] Documentation site updated
- [ ] Announcement prepared

## Success Criteria

- CI/CD passes on all platforms
- AOT compatibility validated
- v1.0.0 released successfully
- Announcement published

## Dependencies

Depends on #$ISSUE_8

## Epic

Part of #$EPIC_NUMBER

## Estimated Effort

1 week
EOF
)

ISSUE_9=$(gh issue create \
  --repo "$REPO" \
  --title "Release Preparation" \
  --body "$ISSUE_9_BODY" \
  --label "release,phase-8" \
  --milestone "$MILESTONE" \
  | grep -oP '(?<=/issues/)\d+')

echo "✅ Created Issue #$ISSUE_9"
echo ""

# Update Epic with child issue numbers
echo "Updating Epic #$EPIC_NUMBER with child issue numbers..."

UPDATED_EPIC_BODY=$(cat <<EOF
## Vision

Enable developers to generate production-ready F# code from Morphir IR, eliminating manual translation and maintaining type safety throughout the business logic transformation.

## Goals

- ✅ Generate idiomatic F# code from Morphir IR v3
- ✅ Support all Morphir type and value constructs
- ✅ Map Morphir SDK types to F# built-in types
- ✅ Integrate with \`morphir gen fsharp\` CLI command
- ✅ Auto-format output using Fantomas
- ✅ Support JSON codecs and lens generation (optional)
- ✅ Achieve ≥80% test coverage
- ✅ AOT-compatible generated code

## Success Metrics

- All Morphir IR v3 constructs generate valid F# code
- Generated code compiles without errors
- Performance: < 5s for 1000 types
- Test coverage ≥ 80%
- At least 3 example projects using the backend

## Related Documents

- [PRD: F# Backend](https://github.com/finos/morphir-dotnet/blob/main/docs/design/PRD-fsharp-backend.md)
- [GitHub Issues Template](https://github.com/finos/morphir-dotnet/blob/main/docs/design/github-issues-fsharp-backend.md)

## Child Issues

- [ ] #$ISSUE_2: Foundation: Project Setup and Fabulous.AST Exploration
- [ ] #$ISSUE_3: Type Mapping: Morphir IR Types → F# Types
- [ ] #$ISSUE_4: Value Mapping: Morphir IR Values → F# Functions
- [ ] #$ISSUE_5: CLI Integration: \`morphir gen fsharp\` Command
- [ ] #$ISSUE_6: SDK Translation: Morphir SDK → F# Standard Library
- [ ] #$ISSUE_7: Advanced Features: JSON Codecs and Lenses
- [ ] #$ISSUE_8: Testing and Documentation
- [ ] #$ISSUE_9: Release Preparation

## Dependencies

- Fabulous.AST v1.9.0+ (already in Directory.Packages.props)
- Fantomas.Core v7.0.5+ (already in Directory.Packages.props)
- Morphir.IR.Pipeline (transformation infrastructure)

## Timeline

**Estimated Duration**: 12 weeks (8 implementation phases)
EOF
)

echo "$UPDATED_EPIC_BODY" | gh issue edit "$EPIC_NUMBER" --repo "$REPO" --body-file -

echo "✅ Updated Epic #$EPIC_NUMBER with child issues"
echo ""

echo "========================================="
echo "✅ All GitHub issues created successfully!"
echo "========================================="
echo ""
echo "Epic: #$EPIC_NUMBER"
echo "Issues: #$ISSUE_2, #$ISSUE_3, #$ISSUE_4, #$ISSUE_5, #$ISSUE_6, #$ISSUE_7, #$ISSUE_8, #$ISSUE_9"
echo ""
echo "View Epic: https://github.com/$REPO/issues/$EPIC_NUMBER"
echo "View all issues: https://github.com/$REPO/issues?q=is%3Aissue+label%3Abackend+label%3Afsharp"
echo ""
echo "Next steps:"
echo "1. Review issues in GitHub"
echo "2. Assign issues to team members"
echo "3. Create GitHub Projects board for tracking"
echo "4. Begin Phase 1 implementation (Issue #$ISSUE_2)"
