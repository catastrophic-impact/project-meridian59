#!/bin/bash
# Build and run the M59 client. Extra args go to the game, e.g. ./run.sh --room f2
cd "$(dirname "$0")" && source ./env.sh && cd client && dotnet build -v q 2>&1 | grep -E " error " ; "$GODOT_BIN" --path . -- "$@"
