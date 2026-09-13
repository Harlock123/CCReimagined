#!/usr/bin/env bash
# Builds the SQLite sample database. No container needed — SQLite is a file.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target="${1:-$here/ccrsample.db}"

if ! command -v sqlite3 >/dev/null 2>&1; then
    echo "sqlite3 is not installed. On Arch/Omarchy: sudo pacman -S sqlite" >&2
    exit 1
fi

rm -f "$target"
sqlite3 "$target" < "$here/seed/sqlite/01-schema.sql"

echo "Created $target"
echo "Point CCReimagined at it: engine SQLite, Database file = $target"
