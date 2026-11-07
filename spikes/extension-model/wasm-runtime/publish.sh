#!/usr/bin/env bash
# Builds every probe variant of the WASM runtime spike outside the repository.
#
# Usage: publish.sh <work-dir>
# Then:  run.sh <work-dir> [runs]
#
# The script copies the probe sources to <work-dir>, downloads the static
# libraries, publishes each variant to <work-dir>/out/<variant> and writes
# the build log to <work-dir>/out/<variant>.log. It needs the .NET 10 SDK on
# PATH, curl, and osx-arm64 (the static libraries are for aarch64 macOS).
set -euo pipefail

SRC="$(cd "$(dirname "$0")" && pwd)"
WORK="${1:?usage: publish.sh <work-dir>}"
mkdir -p "$WORK"
WORK="$(cd "$WORK" && pwd)"
OUT="$WORK/out"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

rsync -a --exclude bin --exclude obj --exclude out --exclude download "$SRC/" "$WORK/"
# Run from the work folder, so that its NuGet.config and global.json apply.
cd "$WORK"
mkdir -p "$OUT" "$WORK/download/capi" "$WORK/download/extism"

WASMTIME_A="$WORK/download/capi/wasmtime-v50.0.1-aarch64-macos-c-api/lib/libwasmtime.a"
if [[ ! -f "$WASMTIME_A" ]]; then
  curl -sSL -o "$WORK/download/wasmtime-c-api.tar.xz" \
    https://github.com/bytecodealliance/wasmtime/releases/download/v50.0.1/wasmtime-v50.0.1-aarch64-macos-c-api.tar.xz
  tar -xJf "$WORK/download/wasmtime-c-api.tar.xz" -C "$WORK/download/capi"
fi
EXTISM_A="$WORK/download/extism/libextism.a"
if [[ ! -f "$EXTISM_A" ]]; then
  curl -sSL -o "$WORK/download/libextism.tar.gz" \
    https://github.com/extism/extism/releases/download/v1.13.0/libextism-aarch64-apple-darwin-v1.13.0.tar.gz
  tar -xzf "$WORK/download/libextism.tar.gz" -C "$WORK/download/extism"
fi

# publish <variant> <project> [msbuild properties...]
publish() {
  local variant="$1" project="$2"
  shift 2
  rm -rf "$WORK/$project/bin" "$WORK/$project/obj" "$OUT/$variant"
  if dotnet publish "$WORK/$project" -c Release -p:TrimmerSingleWarn=false "$@" \
      -o "$OUT/$variant" >"$OUT/$variant.log" 2>&1; then
    local warnings
    warnings=$(grep -E "warning (IL|AOT)[0-9]+" "$OUT/$variant.log" | sed 's/ \[\/.*//' | sort -u |
      grep -oE "(IL|AOT)[0-9]+" | sort | uniq -c | awk '{printf "%s%sx%s", sep, $2, $1; sep=","}' || true)
    printf '%-28s published  warnings=[%s]\n' "$variant" "${warnings:-none}"
  else
    printf '%-28s FAILED (see %s)\n' "$variant" "$OUT/$variant.log"
  fi
}

AOT=(-p:PublishAot=true)
# A self-contained JIT single file that extracts native libraries at start.
SINGLE=(-p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true)

publish baseline-aot        BaselineProbe "${AOT[@]}"
publish wasmtime-aot        WasmtimeProbe "${AOT[@]}"
publish wasmtime-aot-typed-imports WasmtimeProbe "${AOT[@]}" -p:DefineConstants=WASMTIME_TYPED_IMPORTS
publish wasmtime-aot-typed  WasmtimeProbe "${AOT[@]}" -p:DefineConstants=WASMTIME_TYPED
publish wasmtime-aot-static WasmtimeProbe "${AOT[@]}" -p:WasmtimeStatic=true "-p:WasmtimeStaticLib=$WASMTIME_A"
publish extism-aot          ExtismProbe   "${AOT[@]}"
publish extism-aot-static   ExtismProbe   "${AOT[@]}" -p:ExtismStatic=true "-p:ExtismStaticLib=$EXTISM_A"
publish wacs-aot            WacsProbe     "${AOT[@]}"
publish wasmtime-jit-single WasmtimeProbe "${SINGLE[@]}"
publish extism-jit-single   ExtismProbe   "${SINGLE[@]}"
publish wacs-jit-single     WacsProbe     "${SINGLE[@]}"
# A framework-dependent RID-specific publish: the layout of a RID-specific dotnet tool.
publish wasmtime-jit-fdd    WasmtimeProbe -p:SelfContained=false
publish extism-jit-fdd      ExtismProbe   -p:SelfContained=false
publish wacs-jit-fdd        WacsProbe     -p:SelfContained=false

# dotnet tool packages: a portable tool and a RID-specific tool (.NET 10 SDK).
rm -rf "$WORK/feed" "$WORK/tools"
for t in ToolPortable ToolRid; do
  dotnet pack "$WORK/$t" -c Release -o "$WORK/feed" >"$OUT/$t.pack.log" 2>&1 || echo "$t pack FAILED"
done
ls -l "$WORK/feed"
dotnet tool install Spike.WasmProbe.Portable --version 0.0.1 --tool-path "$WORK/tools/portable" --add-source "$WORK/feed"
dotnet tool install Spike.WasmProbe.Rid --version 0.0.1 --tool-path "$WORK/tools/rid" --add-source "$WORK/feed"
du -sk "$WORK/tools/portable/.store" "$WORK/tools/rid/.store"
