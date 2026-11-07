#!/bin/sh
# Builds a NativeAOT-LLVM guest in a linux/arm64 container, because the package
# runtime.osx-arm64.Microsoft.DotNet.ILCompiler.LLVM does not exist
# (dotnet/runtimelab#3279). Then closes the module for morphir-bytes-v1 and
# checks it on the host.
#
# Usage, from this directory:
#   ./build-in-linux.sh csharp-naot-llvm [extra dotnet publish arguments]
#   ./build-in-linux.sh fsharp-naot-llvm -p:ProbeWasip2=true
# Needs: docker, wasm-tools, binaryen (wasm-merge) and wasmtime on the host.
# Set WASM_TOOLS, BINARYEN_BIN and WASMTIME when they are not on PATH.
set -eu
proj=$1; shift
here=$(cd "$(dirname "$0")" && pwd)
export WASM_TOOLS=${WASM_TOOLS:-wasm-tools}
export BINARYEN_BIN=${BINARYEN_BIN:-$(dirname "$(command -v wasm-merge)")}
WASMTIME=${WASMTIME:-wasmtime}

docker run --rm --platform linux/arm64 \
  -v "$here":/work -w /work \
  -e NUGET_PACKAGES=/work/.nuget-linux -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 sh -euc '
    if [ ! -d /work/.wasi-sdk-29 ]; then
      curl -sSL https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-29/wasi-sdk-29.0-arm64-linux.tar.gz | tar -xz -C /work
      mv /work/wasi-sdk-29.0-arm64-linux /work/.wasi-sdk-29
    fi
    export WASI_SDK_PATH=/work/.wasi-sdk-29
    dotnet --version
    cd "/work/$0" && dotnet publish -c Release "$@"' "$proj" "$@"

wasm=$(find "$here/$proj/bin/Release" -path '*publish*' -name '*.wasm' | head -n 1)
echo "raw module: $wasm ($(wc -c < "$wasm") bytes)"
"$WASM_TOOLS" print "$wasm" | grep -E '^\s*\((import|export|start)' || true

closed="${wasm%.wasm}.closed.wasm"
"$here/postlink/close-guest.sh" "$wasm" "$closed"
echo "closed module: $closed ($(wc -c < "$closed") bytes)"
"$WASM_TOOLS" print "$closed" | grep -E '^\s*\((import|export|start)'

"$here/host/make-host.sh" '{"jsonrpc":"2.0","id":7,"method":"initialize","params":{}}' "$here/host/req.wasm"
"$here/host/make-host.sh" '{"jsonrpc":"2.0","method":"exit"}' "$here/host/exit.wasm"
echo "request:"; "$WASMTIME" run --preload morphir_bytes_v1="$here/host/req.wasm" --invoke handle "$closed"
echo "exit:"; "$WASMTIME" run --preload morphir_bytes_v1="$here/host/exit.wasm" --invoke handle "$closed"
