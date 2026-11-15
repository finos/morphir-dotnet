#!/usr/bin/env bash
# Removes the Roslyn probe from the host. Run from the repository root.
set -euo pipefail
git checkout -- src/Morphir/Morphir.csproj src/Morphir/Program.cs
rm -f src/Morphir/RoslynProbe.cs
git status --short src/Morphir
