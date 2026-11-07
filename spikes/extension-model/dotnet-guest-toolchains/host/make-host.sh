#!/bin/sh
# Usage: make-host.sh '<json request>' out.wasm
# Writes a host module that serves the request to a morphir-bytes-v1 guest.
set -eu
req=$1; out=$2
here=$(cd "$(dirname "$0")" && pwd)
len=$(printf '%s' "$req" | wc -c | tr -d ' ')
hex=$(printf '%s' "$req" | od -An -v -tx1 | tr -d ' \n' | sed 's/\(..\)/\\\\\1/g')
sed -e "s|@LEN@|$len|" -e "s|@REQUEST@|$hex|" "$here/host.wat.in" > "$out.wat"
"${WASM_TOOLS:-wasm-tools}" parse "$out.wat" -o "$out"
