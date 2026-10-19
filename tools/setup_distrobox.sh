#!/bin/bash
# Create the Ubuntu 22.04 build box for the server (a distrobox) and install the build tools inside it.
#
#   tools/setup_distrobox.sh
#
# Box name : m59-box                       (override: M59_BOX_NAME)
# Box home : ~/boxes/homes/m59-box         (override: M59_BOX_HOME)  - the box gets its own private home folder, your real home is not touched
# Needs distrobox + podman (or docker) on the host:  https://distrobox.it
set -euo pipefail
BOX="${M59_BOX_NAME:-m59-box}"
BOXHOME="${M59_BOX_HOME:-$HOME/boxes/homes/m59-box}"

command -v distrobox >/dev/null || { echo "distrobox is not installed (see https://distrobox.it/#installation)"; exit 1; }
if distrobox list 2>/dev/null | grep -qw "$BOX"; then
  echo "box '$BOX' already exists - installing/updating the build tools in it"
else
  mkdir -p "$BOXHOME"
  echo "creating box '$BOX' (ubuntu:22.04, home $BOXHOME) ..."
  distrobox create --name "$BOX" --image ubuntu:22.04 --home "$BOXHOME" --yes
fi
echo "installing build tools in the box (asks for nothing; uses sudo inside the box) ..."
distrobox enter "$BOX" -- bash -c '
  set -e
  sudo apt-get update -y
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y build-essential bison flex git python3 python3-venv curl unzip ca-certificates
  gcc --version | head -1; bison --version | head -1; flex --version | head -1'
echo
echo "Box '$BOX' is ready.  Build everything with:  tools/build_release.sh"
