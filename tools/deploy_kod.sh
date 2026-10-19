#!/bin/bash
# (developer tool for the author's flatpak + distrobox setup)  Deploy a freshly built kod (built in the box: ~/src/Meridian59/kod, make -f makefile.linux) to server/ and restart the server.
# Usage: tools/deploy_kod.sh        (saves the game, stops the server, copies loadkod/rsc/memmap/kodbase, regenerates rsc0000.rsb, starts the server in the background)
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC=~/boxes/m59-box/src/Meridian59/run/server
cd "$ROOT/server"
python3 admin.py "terminate save" >/dev/null 2>&1 || true
for i in $(seq 1 60); do (exec 3<>/dev/tcp/127.0.0.1/5959) 2>/dev/null || break; sleep 1; done; sleep 2
cp "$SRC/blakserv" blakserv
for d in loadkod rsc memmap; do rm -rf "$d"; cp -a "$SRC/$d" "$d"; done
cp "$SRC/kodbase.txt" kodbase.txt
flatpak-spawn --host distrobox enter m59-box -- bash -c "cd '$ROOT/server/rsc' && ~/src/Meridian59/bin/rscmerge -o ../rsc0000.rsb *.rsc" >/dev/null 2>&1 || echo "rscmerge failed"
(setsid nohup flatpak-spawn --host distrobox enter m59-box -- bash -c "cd '$ROOT/server' && exec ./blakserv </dev/null >console.out 2>&1" >/dev/null 2>&1 &)
echo "server restarting"
