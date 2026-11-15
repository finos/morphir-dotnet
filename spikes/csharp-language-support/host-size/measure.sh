#!/usr/bin/env bash
# Publishes the Native AOT host and prints its size, its IL warnings and the start time of `--version`.
# Usage, from the repository root: spikes/csharp-language-support/host-size/measure.sh <name> [rid]
# Run it one time as `base`, then apply.py, then one time as `roslyn`, then revert.sh.
set -euo pipefail
name="$1"
rid="${2:-osx-arm64}"
out="${MORPHIR_SPIKE_OUT:-$(mktemp -d)}/$name/$rid"
log="$out.log"
mkdir -p "$out"
dotnet publish src/Morphir/Morphir.csproj --configuration Release --runtime "$rid" \
  --property:CopyOutputSymbolsToPublishDirectory=false --property:DebugSymbols=false --property:DebugType=none \
  --property:MorphirDeployment=native-aot --property:MorphirRidSpecificPublish=true \
  --property:MorphirWasmtimeStaticLink=true --property:PublishAot=true \
  --property:PublishDocumentationFile=false --property:PublishReferencesDocumentationFiles=false \
  --property:PublishSingleFile=true --property:PublishTrimmed=true --self-contained true \
  --output "$out" > "$log" 2>&1 || { grep -E ' error ' "$log" | sed -E 's/ \[.*//' | sort -u | head -20; exit 1; }
echo "size: $(wc -c < "$out/morphir") bytes ($out/morphir)"
echo "IL warnings:"; grep -oE 'warning IL[0-9]+' "$log" | sort | uniq -c || true
python3 - "$out/morphir" <<'PY'
import statistics, subprocess, sys, time
cmd = [sys.argv[1], "--version"]
subprocess.run(cmd, capture_output=True)
runs = []
for _ in range(15):
    start = time.perf_counter(); subprocess.run(cmd, capture_output=True); runs.append((time.perf_counter() - start) * 1000)
print(f"--version median: {statistics.median(runs):.1f} ms")
PY
if [ "$name" = "roslyn" ]; then
  refs="$(dirname "$(command -v dotnet)")"
  refs="$(ls -d "${DOTNET_ROOT:-$refs}"/packs/Microsoft.NETCore.App.Ref/*/ref/net* | tail -1)"
  echo "probe output (references: $refs):"
  MORPHIR_ROSLYN_PROBE="$refs" "$out/morphir"
fi
