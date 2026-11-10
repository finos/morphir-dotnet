#!/usr/bin/env bash
# Measures the Native AOT `morphir` for the default-CLI PRD (add-fsharp-compile task 10.2): the size of the
# single executable and the median wall time of `morphir compile` over 11 runs, without a frontend (the
# settings, sources and frontend-selection stages) and with the installed F# MEP guest.
#
# Usage: scripts/measure-compile-aot.sh <rid> [guest-command]
#   rid            the runtime identifier to publish, for example osx-arm64 or linux-x64
#   guest-command  the installed morphir-fsharp-frontend (default: morphir-fsharp-frontend on PATH)
set -euo pipefail
rid=${1:?usage: measure-compile-aot.sh <rid> [guest-command]}
guest=${2:-morphir-fsharp-frontend}
root=$(cd "$(dirname "$0")/.." && pwd)
work=$(mktemp -d)
python=$(command -v python3 || command -v python)
trap 'rm -rf "$work"' EXIT

# The native-aot release profile (TargetSettings.PublishExecutable): no debug symbols and no documentation files,
# so the size is the size that PublishExecutable and PackRelease ship.
dotnet publish "$root/src/Morphir/Morphir.csproj" -c Release -r "$rid" --self-contained true -p:PublishAot=true -p:PublishTrimmed=true \
  -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false \
  -p:PublishDocumentationFile=false -p:PublishReferencesDocumentationFiles=false -p:MorphirRidSpecificPublish=true \
  -o "$work/out" > "$work/publish.log" 2>&1
exe="$work/out/morphir"
[ -f "$exe" ] || exe="$work/out/morphir.exe"

mkdir -p "$work/demo/src"
printf 'namespace Acme.Demo\nmodule Rates =\n    let rate (n: int) : int = n * 2\n' > "$work/demo/src/Rates.fs"
cd "$work/demo"

median() {
  local times=()
  for _ in $(seq 11); do
    local start end
    start=$("$python" -c 'import time; print(time.time_ns())')
    "$@" > /dev/null 2>&1 || true
    end=$("$python" -c 'import time; print(time.time_ns())')
    times+=($(( (end - start) / 1000000 )))
  done
  printf '%s\n' "${times[@]}" | sort -n | sed -n 6p
}

echo "rid: $rid"
echo "size_bytes: $(wc -c < "$exe" | tr -d ' ')"
echo "compile_no_frontend_ms: $(median "$exe" compile --language fsharp --input src/Rates.fs --package-name acme/demo --json)"
echo "compile_with_guest_ms: $(median "$exe" compile --language fsharp --input src/Rates.fs --package-name acme/demo --extension "$guest" --json)"
