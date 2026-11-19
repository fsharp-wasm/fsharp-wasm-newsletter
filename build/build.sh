#!/usr/bin/env bash
# build.sh — compile the designer to WebAssembly GC:  dist/NewsletterDesigner.wasm
#
#   bash build/build.sh
#
# Needs a compiler (FSHARP_WASM_BIN or FSHARP_WASM_DIR, see build/compile.sh) and wasm-tools.  The page that runs the module is
# web/index.html (`bash build/serve.sh`).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
bash "$ROOT/build/compile.sh" "$ROOT/src/Newsletter.Designer" "$ROOT/dist"
