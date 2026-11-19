#!/usr/bin/env bash
# site.sh — assemble the deployable site: dist/site/  (static files only; nothing runs on the server)
#
#   bash build/build.sh && bash build/site.sh
#
# index.html, the page's CSS, the local Bootstrap, the JavaScript glue of lib/runtime, the compiled module and the headers file.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/site"
[[ -f "$ROOT/dist/NewsletterDesigner.wasm" ]] || { echo "dist/NewsletterDesigner.wasm is missing: run build/build.sh first" >&2; exit 1; }
rm -rf "$OUT"
mkdir -p "$OUT/runtime" "$OUT/vendor"
cp "$ROOT/web/index.html" "$ROOT/web/designer.css" "$OUT/"
cp -r "$ROOT/web/vendor/bootstrap" "$OUT/vendor/"
cp "$ROOT"/lib/runtime/runtime/*.js "$OUT/runtime/"
cp "$ROOT/dist/NewsletterDesigner.wasm" "$OUT/"
cp "$ROOT/site/_headers" "$OUT/_headers"
# beside index.html, not beside a checkout
cat > "$OUT/paths.js" <<'JS'
// written by build/site.sh
export const runtime = "./runtime/";
export const wasm = "./NewsletterDesigner.wasm";
JS
echo "site: $OUT ($(find "$OUT" -type f | wc -l) files, $(du -sk "$OUT" | cut -f1) KB)"
