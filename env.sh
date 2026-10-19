# source this: sets up the project-local Godot + .NET (see tools/fetch_tools.sh)
export M59_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DOTNET_ROOT="$M59_ROOT/tools/dotnet"
export DOTNET_CLI_HOME="$M59_ROOT/tools/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$M59_ROOT/tools/nuget"
export PATH="$DOTNET_ROOT:$PATH"
GODOT_VER="${GODOT_VER:-4.7.2-stable}"
export GODOT_BIN="$M59_ROOT/tools/godot/Godot_v${GODOT_VER}_mono_linux_x86_64/Godot_v${GODOT_VER}_mono_linux.x86_64"
godot() { "$GODOT_BIN" "$@"; }
