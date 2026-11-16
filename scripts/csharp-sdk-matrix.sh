#!/usr/bin/env bash
# The SDK matrix check of the C# build integration (design.html, BLD-1; tasks.md 8.3). It comes from
# spikes/csharp-language-support/source-generator/sdk-matrix.sh, and it uses the packed package, as a user does.
#
# For each .NET SDK, a net10.0 consumer of the packed Morphir.Lang.CSharp package builds with global.json set to that SDK:
#   - SDK 11.0.100 or later: the consumer builds, the source generator writes the code, and the program prints the daily
#     rate of the model.
#   - An older SDK: the build stops with the error MORPHIR2010, which names SDK 11.0.100.
# Then a second consumer takes the assemblies of analyzers/dotnet/cs of the package as analyzers with no build/ file of
# the package, as the spike did. The generator compiles against Roslyn 5.12, the compiler of SDK 11.0.100:
#   - SDK 11.0.100 or later: it loads and generates.
#   - An older SDK: it does not load (CS9057 or CS8032), so the program does not compile (CS0103). This is the check
#     that finds a generator compiled against a Roslyn newer than the compiler of the SDK.
#
# Usage: scripts/csharp-sdk-matrix.sh <packages-folder> [<sdk-version>...]
# Default SDKs: 10.0.100 10.0.403 11.0.100. Each SDK must be installed beside the dotnet on PATH.
# The packages folder must have the packages of PackAll: Morphir.Lang.CSharp, Morphir.Models, Morphir.Compilation,
# Morphir.MiddleEnd and Morphir.SDK.CSharp. The consumers are in a temporary folder outside the repository, with their
# own NuGet packages folder. The script writes one line for each check and exits with 1 when a check fails.
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "usage: $0 <packages-folder> [<sdk-version>...]" >&2
  exit 2
fi
packages="$(cd "$1" && pwd)"
shift
if [ $# -eq 0 ]; then set -- 10.0.100 10.0.403 11.0.100; fi

here="$(cd "$(dirname "$0")" && pwd)"
model="$here/../tests/Fixtures/MorphirIR/daily-rate-v4.json"
package="$(find "$packages" -maxdepth 1 -name 'Morphir.Lang.CSharp.[0-9]*.nupkg' | sort | tail -1)"
if [ -z "$package" ]; then
  echo "no Morphir.Lang.CSharp package in $packages: run ./build.sh --target PackAll first" >&2
  exit 2
fi
version="$(basename "$package" .nupkg)"
version="${version#Morphir.Lang.CSharp.}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/analyzers"
unzip -q -j "$package" 'analyzers/dotnet/cs/*' -d "$work/analyzers"
export NUGET_PACKAGES="$work/nuget-packages"
failures=0

# Writes a consumer in $1. $2 is "package" (a package reference) or "analyzers" (the analyzer assemblies only).
write_consumer() {
  local folder="$1" kind="$2" sdk="$3" reference
  mkdir -p "$folder"
  if [ "$kind" = package ]; then
    reference="<PackageReference Include=\"Morphir.Lang.CSharp\" Version=\"[$version]\" />"
  else
    reference="<Analyzer Include=\"$work/analyzers/*.dll\" />"
  fi
  cat > "$folder/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    $reference
    <PackageReference Include="Morphir.SDK.CSharp" Version="[$version]" />
    <AdditionalFiles Include="car-rental.morphir.json" />
  </ItemGroup>
</Project>
EOF
  cp "$model" "$folder/car-rental.morphir.json"
  echo 'System.Console.WriteLine($"daily rate: {Car.Rental.Inventory.DailyRate()}");' > "$folder/Program.cs"
  printf '{ "sdk": { "version": "%s", "rollForward": "disable" } }\n' "$sdk" > "$folder/global.json"
  cat > "$folder/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="packages" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
}

# check <name> <expected text> <output>: one line, and a failure when the output does not have the text.
check() {
  if grep -q -- "$2" <<<"$3"; then
    echo "  pass: $1"
  else
    echo "  FAIL: $1 (no '$2' in the output)"
    sed 's/^/    | /' <<<"$3" | grep -E 'error|warning|daily rate' | cut -c1-300 | sort -u | head -20
    failures=$((failures + 1))
  fi
}

for sdk in "$@"; do
  folder="$work/$sdk"
  write_consumer "$folder/package" package "$sdk"
  write_consumer "$folder/analyzers" analyzers "$sdk"
  actual="$(cd "$folder/package" && dotnet --version)"
  echo "== SDK $actual (Morphir.Lang.CSharp $version)"
  if [ "$actual" != "$sdk" ]; then
    echo "  FAIL: global.json selects $sdk, but dotnet runs $actual"
    failures=$((failures + 1))
    continue
  fi
  major="${sdk%%.*}"
  package_output="$(cd "$folder/package" && dotnet run -c Release 2>&1 || true)"
  analyzer_output="$(cd "$folder/analyzers" && dotnet run -c Release 2>&1 || true)"
  if [ "$major" -ge 11 ]; then
    check "the package generates the code and the program runs" "daily rate: 5000" "$package_output"
    check "the generator loads in the compiler of the SDK" "daily rate: 5000" "$analyzer_output"
  else
    check "the package stops the build with the SDK error" "error MORPHIR2010.*11.0.100" "$package_output"
    check "the generator does not load in an older compiler" "CS9057: Analyzer assembly .*Morphir.Lang.CSharp.Generator.dll\|CS8032" "$analyzer_output"
    check "so the generated code is not there" "error CS0103" "$analyzer_output"
  fi
done

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed"
  exit 1
fi
echo "all checks passed"
