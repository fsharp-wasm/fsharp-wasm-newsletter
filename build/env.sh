# source build/env.sh — the environment of a build in this repository.
#
#   git submodule update --init            # lib/runtime (JS glue + HTML/Elmish library), lib/fsharp-wasm-core, lib/wasmecto
#
# FSHARP_WASM_CORE_DIR: the F# base library every program compiled by fsharp-wasm is built with (the `lib/fsharp-wasm-core` submodule).
# The compiler is not a submodule: build/build.sh takes a release binary (FSHARP_WASM_BIN) or a checkout (FSHARP_WASM_DIR).
_nl_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export FSHARP_WASM_CORE_DIR="${FSHARP_WASM_CORE_DIR:-$_nl_root/lib/fsharp-wasm-core}"
unset _nl_root
