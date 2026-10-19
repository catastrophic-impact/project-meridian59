#!/bin/bash
# Developer tool: copy every file we changed or added in the (git-tracked) upstream Meridian59 source tree into server/mods/overlay/,
# keeping the same relative paths.  The release build clones pristine upstream and copies this overlay over it (see tools/build_server.sh).
#
# Usage: tools/sync_mods.sh [path-to-upstream-clone]      (default: the dev box clone  ~/boxes/m59-box/src/Meridian59)
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="${1:-$HOME/boxes/m59-box/src/Meridian59}"
DEST="$ROOT/server/mods/overlay"
[ -d "$SRC/.git" ] || { echo "$SRC is not a git clone of Meridian59/Meridian59"; exit 1; }

rm -rf "$DEST"; mkdir -p "$DEST"
cd "$SRC"
# modified tracked files + new untracked source files; skip build products (bin/, run/, *.bof ...)
{ git ls-files -m; git ls-files -o --exclude-standard; } | grep -v -E '^(bin|run|lib)/' | sort -u | while read -r f; do
  [ -f "$f" ] || continue                      # deleted files are not handled (we delete nothing)
  mkdir -p "$DEST/$(dirname "$f")"; cp -p "$f" "$DEST/$f"
done
git rev-parse HEAD > "$ROOT/server/mods/UPSTREAM_COMMIT"
echo "overlay: $(find "$DEST" -type f | wc -l) files, upstream commit $(cat "$ROOT/server/mods/UPSTREAM_COMMIT")"
