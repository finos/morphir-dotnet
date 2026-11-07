#!/bin/sh
# Post-link step for a wasi-libc guest (the shape that NativeAOT-LLVM emits
# for wasm32-wasip1 with NativeLib=Shared):
#   1. merge WASI preview 1 stubs, so only morphir_bytes_v1 imports stay;
#   2. keep only the exports "handle" and "memory";
#   3. run _initialize from the start section, because the morphir-bytes-v1
#      host calls only handle.
# Usage: close-guest.sh in.wasm out.wasm   (WASM_TOOLS and BINARYEN_BIN set)
set -eu
in=$1; out=$2; here=$(cd "$(dirname "$0")" && pwd); tmp=$(mktemp -d)
"$WASM_TOOLS" parse "$here/wasi-stubs.wat" -o "$tmp/stubs.wasm"
"$BINARYEN_BIN/wasm-merge" --enable-bulk-memory --enable-exception-handling --enable-reference-types \
  "$in" guest "$tmp/stubs.wasm" wasi_snapshot_preview1 -o "$tmp/merged.wasm"
"$WASM_TOOLS" print "$tmp/merged.wasm" > "$tmp/merged.wat"
init=$(grep -E '^\s*\(export "_initialize" \(func [0-9]+\)\)' "$tmp/merged.wat" | sed -E 's/.*\(func ([0-9]+)\).*/\1/' || true)
grep -v -E '^\s*\(export "[^"]+" \((func|memory|global|table) ' "$tmp/merged.wat" > "$tmp/body.wat" || true
handle=$(grep -E '^\s*\(export "handle" \(func [0-9]+\)\)' "$tmp/merged.wat")
memory=$(grep -E '^\s*\(export "memory" \(memory [0-9]+\)\)' "$tmp/merged.wat")
# Put the kept exports, and the start section, before the last closing parenthesis.
sed '$d' "$tmp/body.wat" > "$tmp/final.wat"
{ echo "$handle"; echo "$memory"; [ -n "$init" ] && echo "  (start $init)"; echo ")"; } >> "$tmp/final.wat"
"$WASM_TOOLS" parse "$tmp/final.wat" -o "$out"
"$WASM_TOOLS" validate "$out"
rm -rf "$tmp"
