#!/usr/bin/env bash
# Re-renders docs/CCReimagined-Developer-Guide.pdf from doc.html.
# Needs only Chromium — no LaTeX, no pandoc.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="$here/../CCReimagined-Developer-Guide.pdf"

chrome="$(command -v chromium || command -v chromium-browser || command -v google-chrome || true)"
if [[ -z "$chrome" ]]; then
    echo "Chromium or Chrome is required to render the PDF." >&2
    exit 1
fi

"$chrome" --headless --disable-gpu --no-sandbox \
    --print-to-pdf="$out" --no-pdf-header-footer \
    --virtual-time-budget=10000 "file://$here/doc.html" >/dev/null 2>&1

echo "Wrote $(cd "$(dirname "$out")" && pwd)/$(basename "$out")"
