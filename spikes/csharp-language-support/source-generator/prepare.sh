#!/usr/bin/env bash
# Copies the Morphir.Models and C# backend sources of the repository to Core/obj/src-copy,
# and adds `open Morphir.Compat` after the namespace line of the ten files that need a polyfill.
# The repository sources stay as they are.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../../.." && pwd)"
copy="$here/Core/obj/src-copy"
rm -rf "$copy"
mkdir -p "$copy"
for project in Morphir.Models Morphir.CodeGeneration.CSharp; do
  mkdir -p "$copy/$project"
  (cd "$repo/src/$project" && find . -name '*.fs' -not -path './obj/*' -not -path './bin/*' -print0) |
    while IFS= read -r -d '' file; do
      mkdir -p "$copy/$project/$(dirname "$file")"
      cp "$repo/src/$project/$file" "$copy/$project/$file"
    done
done
for file in \
  Morphir.Models/IR/FQName.fs \
  Morphir.Models/IR/V4/OrderedMap.fs \
  Morphir.Models/IR/V4/Literal.fs \
  Morphir.Models/Json/Classic/ClassicJsonWriter.fs \
  Morphir.Models/Json/Classic/ClassicJson.fs \
  Morphir.Models/Json/V4/V4JsonDecoding.fs \
  Morphir.Models/Json/V4/V4JsonDistributions.fs \
  Morphir.Models/Json/V4/V4JsonWriter.fs \
  Morphir.CodeGeneration.CSharp/Types.fs \
  Morphir.CodeGeneration.CSharp/TypeEmission.fs; do
  # Insert the line after the first line that starts with `namespace `.
  awk 'done != 1 && /^namespace / { print; print "open Morphir.Compat"; done = 1; next } { print }' \
    "$copy/$file" > "$copy/$file.tmp"
  mv "$copy/$file.tmp" "$copy/$file"
  grep -q '^open Morphir.Compat$' "$copy/$file" || { echo "no namespace line in $file" >&2; exit 1; }
done
echo "sources copied to $copy"
