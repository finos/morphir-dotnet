#!/usr/bin/env bash
# Builds the generator one time with the SDK of the repository, then builds and runs the consumer
# with each .NET SDK that you give. Each SDK has its own Roslyn, so this shows in which compilers
# the generator loads.
# Usage: ./sdk-matrix.sh <roslyn-version-of-the-generator> <sdk-version>...
# Example: ./sdk-matrix.sh 5.0.0 10.0.100 10.0.403 11.0.100
# Each SDK version must be installed. A global.json in a copy of the consumer selects it.
# The copy is outside the repository. If `dotnet` is a version manager shim that needs a project
# folder (mise, asdf), set DOTNET_ROOT to the folder of the real `dotnet`.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
roslyn="$1"; shift
"$here/prepare.sh" > /dev/null
(cd "$here/Generator" && rm -rf bin obj ../Core/bin && dotnet build -c Release -p:RoslynVersion="$roslyn" 2>&1 | grep -E 'error|Build succeeded' | sort -u)
(cd "$here/../../../src/Morphir.SDK.CSharp" && dotnet build -c Release -f net10.0 2>&1 | grep -E ' error |Build succeeded' | sort -u)
sdk="$(find "$here/../../.." -path '*Release*net10.0*' -name Morphir.SDK.CSharp.dll -not -path '*/obj/*' -not -path '*/ref/*' | head -1)"
work="$(mktemp -d)"
if [ -n "${DOTNET_ROOT:-}" ]; then consumer_path="$DOTNET_ROOT:$PATH"; else consumer_path="$PATH"; fi
cp "$sdk" "$work/"
for version in "$@"; do
  rm -rf "$work/Consumer"
  mkdir -p "$work/Consumer"
  cp "$here/Consumer/Consumer.csproj" "$here/Consumer/Program.cs" "$here/Consumer/car-rental.morphir.json" "$work/Consumer/"
  printf '{ "sdk": { "version": "%s", "rollForward": "disable" } }\n' "$version" > "$work/Consumer/global.json"
  echo "== SDK $(cd "$work/Consumer" && PATH="$consumer_path" dotnet --version) (generator compiled against Roslyn $roslyn)"
  (cd "$work/Consumer" && PATH="$consumer_path" dotnet run -c Release \
      -p:GeneratorDir="$here/Generator/bin/Release/netstandard2.0" -p:MorphirSdkDll="$work/Morphir.SDK.CSharp.dll" 2>&1 |
    grep -E 'daily rate|compiler Roslyn|warning CS8032|error ' | sed -E 's/ \[.*//' | cut -c1-240 | sort -u) || true
done
