#!/bin/bash
# Build the game server (blakserv + compiled game code) with our modifications and assemble it into a folder.
#
#   tools/build_server.sh [output-dir] [--incremental]          default output: release/server
#
# Run it inside the Ubuntu 22.04 build box (see docs/BUILDING.md and tools/setup_distrobox.sh) or on any Ubuntu 22.04 with
# build-essential bison flex git python3.  Steps:
#   1. clone upstream Meridian59/Meridian59 into build/upstream at the commit in server/mods/UPSTREAM_COMMIT (pristine)
#   2. copy server/mods/overlay over it (our changed + new kod/C files)
#   3. build the kod compiler, the utilities, blakserv and the kod (about 3 minutes)
#   4. assemble: binaries, compiled kod, rooms, string table, admin tools, config
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/release/server"; INCR=0
for a in "$@"; do case "$a" in --incremental) INCR=1 ;; *) OUT="$(realpath -m "$a")" ;; esac; done
UP_URL="${M59_UPSTREAM_URL:-https://github.com/Meridian59/Meridian59.git}"
COMMIT="$(cat "$ROOT/server/mods/UPSTREAM_COMMIT")"
UP="$ROOT/build/upstream"

echo "== 1/4 upstream source ($COMMIT)"
mkdir -p "$ROOT/build"
[ -d "$UP/.git" ] || git clone --quiet "$UP_URL" "$UP"
cd "$UP"
git cat-file -e "$COMMIT^{commit}" 2>/dev/null || git fetch --quiet origin
git checkout --quiet -f "$COMMIT"
[ "$INCR" = 1 ] || git clean -fdxq -e 'run/*'

echo "== 2/4 our modifications"
cp -a "$ROOT/server/mods/overlay/." "$UP/"

echo "== 3/4 build"
mkdir -p run/server/loadkod run/server/rsc run/server/memmap run/server/channel run/server/savegame
[ "$INCR" = 1 ] || rm -f run/server/loadkod/* run/server/rsc/* run/server/memmap/*
JOBS="$(nproc 2>/dev/null || echo 2)"
make -C blakcomp -f makefile.linux -s
make -C util -f makefile.linux -s
make -C blakserv -f makefile.linux -s -j"$JOBS" RELEASE=1
( cd kod && make -f makefile.linux -s )          # serial: the kod makefiles recurse into each other
ls run/server/loadkod/*.bof >/dev/null          # fail loudly if the kod build produced nothing

echo "== 4/4 assemble -> $OUT"
mkdir -p "$OUT"
rm -rf "$OUT/loadkod" "$OUT/rsc" "$OUT/memmap" "$OUT/rooms"
cp -a run/server/blakserv run/server/kodbase.txt "$OUT/"
cp -a run/server/loadkod run/server/rsc run/server/memmap "$OUT/"
mkdir -p "$OUT/channel" "$OUT/savegame" "$OUT/rooms"
cp kod/include/blakston.khd kod/include/protocol.khd "$OUT/"
[ -f run/server/packages.txt ] && cp run/server/packages.txt "$OUT/" || cp "$ROOT/server/packages.txt" "$OUT/"
cp run/server/motd.txt "$OUT/" 2>/dev/null || true

# rooms: upstream ships them as lowercase .roo; the kod asks for some with other capitalisation (the server runs on a case-sensitive file system)
cp resource/rooms/*.roo "$OUT/rooms/"
python3 - "$UP" "$OUT/rooms" <<'PY'
import os, re, shutil, sys
up, rooms = sys.argv[1], sys.argv[2]
have = {f.lower(): f for f in os.listdir(rooms)}
want = set()
for d, _, files in os.walk(os.path.join(up, "kod")):
    for f in files:
        if f.endswith(".kod"):
            want.update(re.findall(r'=\s*([A-Za-z0-9_]+\.roo)\b', open(os.path.join(d, f), errors="replace").read(), re.I))
made = 0
for name in sorted(want):
    if not os.path.exists(os.path.join(rooms, name)) and name.lower() in have:
        shutil.copy(os.path.join(rooms, have[name.lower()]), os.path.join(rooms, name)); made += 1
print(f"rooms: {len(os.listdir(rooms))} files ({made} copied under the capitalisation the kod expects)")
PY

# client string table (matches the compiled kod)
( cd "$OUT/rsc" && "$UP/bin/rscmerge" -o ../rsc0000.rsb *.rsc >/dev/null )

# lists for the admin tools / client admin window
python3 "$ROOT/tools/make_roomlist.py" "$UP" "$OUT/rooms.json"
python3 "$ROOT/tools/make_itemlist.py" "$UP" "$OUT/items.json"

# our server module: admin console / TUI, settings list, config, start script
for f in admin.py admin-tui.py admin-tui.sh gamesettings.py start.sh blakserv.cfg; do cp "$ROOT/server/$f" "$OUT/$f"; done
chmod +x "$OUT"/start.sh "$OUT"/admin-tui.sh "$OUT"/blakserv
echo "server built: $OUT"
