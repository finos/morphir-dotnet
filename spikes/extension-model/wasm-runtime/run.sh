#!/usr/bin/env bash
# Runs the probes that publish.sh made and prints sizes, checks and cold start.
#
# Usage: run.sh <work-dir> [runs]
set -euo pipefail
WORK="$(cd "${1:?usage: run.sh <work-dir> [runs]}" && pwd)"
OUT="$WORK/out"
RUNS="${2:-20}"

echo "## Sizes (bytes)"
for d in "$OUT"/*/; do
  v=$(basename "$d")
  exe=$(find "$d" -maxdepth 1 -type f -perm +111 ! -name '*.dylib' | head -1)
  native=$(find "$d" -maxdepth 3 -name '*.dylib' -type f | xargs stat -f %z 2>/dev/null | paste -sd+ - | bc || true)
  total=$(find "$d" -type f ! -path '*.dSYM*' | xargs stat -f %z | paste -sd+ - | bc)
  printf '%-28s exe=%-10s native=%-10s folder=%s\n' "$v" "$(stat -f %z "$exe")" "${native:-0}" "$total"
done

run() { # run <variant> <command...>
  local v="$1"; shift
  echo "## $v"
  "$@" || true
  # Cold start: median over RUNS fresh processes of one echo call.
  local loads=() firsts=()
  for _ in $(seq "$RUNS"); do
    line=$(SPIKE_ONLY_ECHO=1 "$@" | grep $'\techo\t')
    loads+=("$(sed -E 's/.*load\+call=([0-9.]+)ms.*/\1/' <<<"$line")")
    firsts+=("$(sed -E 's/.*processStart->firstResponse=([0-9.]+)ms.*/\1/' <<<"$line")")
  done
  med() { printf '%s\n' "$@" | sort -n | awk '{a[NR]=$1} END {print a[int((NR+1)/2)]}'; }
  echo "cold start over $RUNS runs: median load+call=$(med "${loads[@]}")ms median processStart->firstResponse=$(med "${firsts[@]}")ms"
}

for v in wasmtime-aot wasmtime-aot-typed-imports wasmtime-aot-typed wasmtime-aot-static extism-aot extism-aot-static wacs-aot; do
  exe=$(find "$OUT/$v" -maxdepth 1 -type f -perm +111 ! -name '*.dylib' | head -1)
  run "$v" "$exe"
done
for v in wasmtime-jit-single extism-jit-single wacs-jit-single; do
  exe=$(find "$OUT/$v" -maxdepth 1 -type f -perm +111 ! -name '*.dylib' | head -1)
  run "$v" "$exe"
done
for v in wasmtime-jit-fdd extism-jit-fdd wacs-jit-fdd; do
  dll=$(find "$OUT/$v" -maxdepth 1 -name '*Probe.dll' | head -1)
  run "$v" dotnet "$dll"
done
for t in portable rid; do
  run "tool-$t" "$WORK/tools/$t/probe-$t" "$WORK/guest/guest.wasm"
done
