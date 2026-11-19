#!/usr/bin/env bash
# compile.sh <project-dir> <out-dir> — compile one project to WebAssembly GC with fsharp-wasm.
#
# The compiler is not part of this repository; first hit wins:
#   FSHARP_WASM_BIN   the self-contained `fsharp-wasm` executable of a release (it carries FSharp.Wasm.Core beside it)
#   FSHARP_WASM_DIR   a checkout of fsharp-wasm: its CLI runs through `dotnet run`, and needs FSHARP_COMPILER = the patched
#                     fsc.dll built from the checkout's vendor/fsharp (the compiler's README has the details)
# `wasm-tools` must be on the PATH.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
source "$ROOT/build/env.sh"
PROJECT="$(cd "$1" && pwd)"; mkdir -p "$2"; OUT="$(cd "$2" && pwd)"
command -v wasm-tools >/dev/null || { echo "wasm-tools not found (cargo install wasm-tools)" >&2; exit 1; }
# the compiler wants the reference assemblies of the project references on disk
dotnet build "$FSHARP_WASM_CORE_DIR/FSharp.Wasm.Core/FSharp.Wasm.Core.fsproj" -c Release --nologo -v q >/dev/null
if [[ -n "${FSHARP_WASM_BIN:-}" ]]; then
  "$FSHARP_WASM_BIN" "$PROJECT" --outDir "$OUT" --noCache
elif [[ -n "${FSHARP_WASM_DIR:-}" ]]; then
  dotnet run -c Release --project "$FSHARP_WASM_DIR/tools/FSharp.Wasm.Cli" -- "$PROJECT" --outDir "$OUT" --noCache
else
  echo "no compiler: set FSHARP_WASM_BIN (a release binary) or FSHARP_WASM_DIR (a fsharp-wasm checkout)" >&2
  exit 1
fi
for f in "$OUT"/*.wasm; do wasm-tools validate "$f" --features all; echo "built: $f ($(wc -c < "$f") bytes)"; done
