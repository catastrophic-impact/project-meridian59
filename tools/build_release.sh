#!/bin/bash
# Build the whole project into  release/ :
#     release/server/          the game server + admin console (built inside the Ubuntu 22.04 box)
#     release/client/          the portable Godot client      release/runtime/   Godot + .NET runtime
#     release/start-server.sh  release/start-admin.sh  release/start-client.sh
#
#   tools/build_release.sh [--setup-box] [--incremental]
#     --setup-box     create the m59-box distrobox first when it does not exist (tools/setup_distrobox.sh)
#     --incremental   keep the build/ folder between runs (faster, only for development)
#
# The game assets are not included: after building run  tools/extract_assets.py "<original client folder>" --dest release/client/assets
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REL="$ROOT/release"
SETUP=0; INCR=""
for a in "$@"; do case "$a" in --setup-box) SETUP=1 ;; --incremental) INCR="--incremental" ;; *) echo "unknown option $a"; exit 1 ;; esac; done
BOX="${M59_BOX_NAME:-m59-box}"

echo "######## server"
if [ -n "${CONTAINER_ID:-}" ] || ! command -v distrobox >/dev/null; then
  # already inside a build container (or no distrobox): build here (needs build-essential bison flex git python3)
  "$ROOT/tools/build_server.sh" "$REL/server" $INCR
else
  if ! distrobox list 2>/dev/null | grep -qw "$BOX"; then
    if [ "$SETUP" = 1 ]; then "$ROOT/tools/setup_distrobox.sh"; else echo "build box '$BOX' not found - run tools/setup_distrobox.sh (or add --setup-box)"; exit 1; fi
  fi
  distrobox enter "$BOX" -- "$ROOT/tools/build_server.sh" "$REL/server" $INCR
fi

echo "######## client"
"$ROOT/tools/build_client.sh" "$REL"

echo "######## launch scripts"
cat > "$REL/start-server.sh" <<'EOS'
#!/bin/bash
exec "$(cd "$(dirname "$(readlink -f "$0")")" && pwd)/server/start.sh" "$@"
EOS
cat > "$REL/start-admin.sh" <<'EOS'
#!/bin/bash
exec "$(cd "$(dirname "$(readlink -f "$0")")" && pwd)/server/admin-tui.sh" "$@"
EOS
cp "$ROOT/docs/RELEASE_README.txt" "$REL/README.txt"
chmod +x "$REL"/start-*.sh
echo
echo "Done.  release/ is ready:"
echo "  1. tools/extract_assets.py \"<original client folder>\" --dest release/client/assets      (once; see docs/ASSETS.md)"
echo "  2. release/start-server.sh        3. release/start-admin.sh (optional)        4. release/start-client.sh"
