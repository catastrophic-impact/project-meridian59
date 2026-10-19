#!/bin/bash
# Build the Godot client into a portable folder.
#
#   tools/build_client.sh [release-dir]          default: release
#
# Produces   <release-dir>/client/   (the Godot project incl. the compiled C# assemblies; assets are NOT included, see tools/extract_assets.py)
#            <release-dir>/runtime/  (Godot + the .NET runtime, so nothing has to be installed)
#            <release-dir>/start-client.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REL="$(realpath -m "${1:-$ROOT/release}")"
bash "$ROOT/tools/fetch_tools.sh"
source "$ROOT/env.sh"

echo "== compiling the client"
( cd "$ROOT/client" && dotnet build -v q -nologo 2>&1 | grep -E " error |Warn|Error" | grep -v " 0 " || true )
[ -f "$ROOT/client/.godot/mono/temp/bin/Debug/M59Client.dll" ] || { echo "client build failed"; exit 1; }

echo "== assembling $REL"
mkdir -p "$REL"
rm -rf "$REL/client" "$REL/runtime"
mkdir -p "$REL/client/.godot/mono/temp/bin" "$REL/runtime"
# the project (sources are open anyway; Godot needs the .cs files next to the scene) ...
( cd "$ROOT/client" && tar cf - --exclude=assets --exclude=bin --exclude=obj --exclude='.godot/mono' --exclude='.godot/editor' . ) | tar xf - -C "$REL/client"
# ... and the compiled assemblies
cp -a "$ROOT/client/.godot/mono/temp/bin/Debug" "$REL/client/.godot/mono/temp/bin/Debug"
mkdir -p "$REL/client/assets"
echo "Game assets (not part of this project) go here: run tools/extract_assets.py, see docs/ASSETS.md" > "$REL/client/assets/README.txt"

# Godot with the C# support files, and only the .NET runtime (no SDK)
GD="$(dirname "$GODOT_BIN")"
mkdir -p "$REL/runtime/godot" "$REL/runtime/dotnet/shared/Microsoft.NETCore.App"
cp -a "$GD/." "$REL/runtime/godot/"
touch "$REL/runtime/godot/._sc_"                       # Godot "self-contained" mode: its settings stay in this folder
cp -a "$DOTNET_ROOT/dotnet" "$DOTNET_ROOT/host" "$REL/runtime/dotnet/"
cp -a "$DOTNET_ROOT"/shared/Microsoft.NETCore.App/. "$REL/runtime/dotnet/shared/Microsoft.NETCore.App/"

cat > "$REL/start-client.sh" <<'EOS'
#!/bin/bash
# Start the Meridian 59 client (portable: uses the Godot and .NET runtime in ./runtime).  Extra arguments go to the game.
here="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
export DOTNET_ROOT="$here/runtime/dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$here"
exec "$here/runtime/godot/$(ls "$here/runtime/godot" | grep -E '^Godot_v.*_mono_linux\.x86_64$' | head -1)" --path "$here/client" -- "$@"
EOS
chmod +x "$REL/start-client.sh"
echo "client built: $REL/client   (run $REL/start-client.sh)"
