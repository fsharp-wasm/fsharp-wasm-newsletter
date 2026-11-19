#!/usr/bin/env bash
# test.sh — the suites, twice: under .NET, and compiled to WebAssembly GC and run in Node.
#
#   bash build/test.sh            both
#   bash build/test.sh dotnet     only the .NET run
#   bash build/test.sh wasm       only the WasmGC run  (needs a compiler, wasm-tools and a checkout of fsharp-wasm for the runner)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
source "$ROOT/build/env.sh"
MODE="${1:-both}"
if [[ $MODE == both || $MODE == dotnet ]]; then
  echo "==> .NET"
  dotnet run --project "$ROOT/tests/Newsletter.Tests/Newsletter.Tests.fsproj" -c Release --nologo -v q
fi
if [[ $MODE == both || $MODE == wasm ]]; then
  echo "==> WebAssembly GC"
  bash "$ROOT/build/compile.sh" "$ROOT/tests/Newsletter.WasmTests" "$ROOT/dist/tests"
  # the runner and the host imports are the compiler's (the same ABI every module is tested with)
  RUNNER="${FSHARP_WASM_DIR:?FSHARP_WASM_DIR (a checkout of fsharp-wasm) provides tests/Wasm/run.mjs}/tests/Wasm/run.mjs"
  node "$RUNNER" "$ROOT/dist/tests/WasmTests.wasm"
fi
