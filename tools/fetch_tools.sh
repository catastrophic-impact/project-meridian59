#!/bin/bash
# Download the project-local build tools into tools/ (nothing is installed system-wide):
#   tools/dotnet   .NET 8 SDK          tools/godot   Godot 4.7.2 (Mono / C# build)
# Needs curl and unzip.  Safe to run repeatedly.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GODOT_VER="${GODOT_VER:-4.7.2-stable}"
GODOT_DIR="$ROOT/tools/godot/Godot_v${GODOT_VER}_mono_linux_x86_64"

if [ ! -x "$ROOT/tools/dotnet/dotnet" ]; then
  echo "== downloading the .NET 8 SDK"
  bash "$ROOT/tools/dotnet-install.sh" --channel 8.0 --install-dir "$ROOT/tools/dotnet" --no-path
fi
if [ ! -x "$GODOT_DIR/Godot_v${GODOT_VER}_mono_linux.x86_64" ]; then
  echo "== downloading Godot $GODOT_VER (mono)"
  mkdir -p "$ROOT/tools/godot"
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  curl -fL --progress-bar -o "$tmp/godot.zip" "https://github.com/godotengine/godot/releases/download/${GODOT_VER}/Godot_v${GODOT_VER}_mono_linux_x86_64.zip"
  unzip -q "$tmp/godot.zip" -d "$ROOT/tools/godot"
  chmod +x "$GODOT_DIR"/Godot_v*_mono_linux.x86_64
fi
echo "tools ready"
