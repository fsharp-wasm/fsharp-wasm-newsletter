#!/usr/bin/env bash
# run.sh [path to a checkout of foundation/foundation-emails]
#   Inlines the same page with our F# inliner and with juice + Foundation's compiled CSS, and compares every element's effective styles.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
FOUNDATION="${1:-$HERE/../../../foundation/foundation-emails}"
OUT="$(mktemp -d)"
[ -f "$FOUNDATION/dist/foundation-emails.css" ] || { echo "no checkout of foundation-emails at $FOUNDATION (git clone --depth 1 https://github.com/foundation/foundation-emails)" >&2; exit 2; }
dotnet run -c Release --project "$HERE/Fidelity.fsproj" -- "$OUT"
(cd "$HERE" && npm install --no-audit --no-fund --silent && node inline-juice.mjs "$FOUNDATION/dist/foundation-emails.css" "$OUT/raw.html" "$OUT/theirs-inlined.html")
python3 "$HERE/compare.py" "$OUT"
