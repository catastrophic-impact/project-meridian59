# Building

## The short way (scripts)

Requirements on your computer: Linux with **git**, **curl**, **unzip**, **python3**, and **distrobox + podman (or docker)** for the server build.
(The server is built on Ubuntu 22.04 inside a distrobox so it builds the same on every distribution.)

```bash
git clone https://github.com/catastrophic-impact/project-meridian59
cd project-meridian59
tools/build_release.sh --setup-box
```

That script

1. creates the distrobox **`m59-box`** (Ubuntu 22.04) with its own home folder **`~/boxes/homes/m59-box`** and installs the build tools in it
   (`tools/setup_distrobox.sh`; you can run it on its own, `M59_BOX_NAME` / `M59_BOX_HOME` change the name and home folder),
2. builds the server inside the box (`tools/build_server.sh`, about 3 minutes) into **`release/server`**,
3. downloads the .NET 8 SDK and Godot 4.7.2 (Mono) into `tools/` (project local, nothing is installed on your system) and builds the client into
   **`release/client`**, with a copy of Godot and the .NET runtime in **`release/runtime`** (`tools/build_client.sh`),
4. writes `release/start-server.sh`, `release/start-admin.sh`, `release/start-client.sh`.

Then add the game assets once ([ASSETS.md](ASSETS.md)):

```bash
tools/extract_assets.py "/path/to/Meridian 59" --dest release/client/assets
```

and play:

```bash
release/start-server.sh        # terminal 1
release/start-admin.sh         # terminal 2, optional
release/start-client.sh
```

The `release/` folder is portable: copy it anywhere (the server needs glibc 2.35 or newer — Ubuntu 22.04+, or run it with the box).

## Without the distrobox script (manual)

### Server (Ubuntu 22.04 or similar)

```bash
sudo apt install build-essential bison flex git python3 python3-venv
git clone https://github.com/catastrophic-impact/project-meridian59 && cd project-meridian59
tools/build_server.sh release/server          # does everything below
```

What `build_server.sh` does, if you want to do it by hand:

```bash
git clone https://github.com/Meridian59/Meridian59.git build/upstream
cd build/upstream && git checkout $(cat ../../server/mods/UPSTREAM_COMMIT)
cp -a ../../server/mods/overlay/. .                  # our changes
mkdir -p run/server/{loadkod,rsc,memmap,channel,savegame}
make -C blakcomp -f makefile.linux                   # the Blakod compiler (bin/bc)
make -C util -f makefile.linux                       # rscmerge etc.
make -C blakserv -f makefile.linux RELEASE=1         # the server, run/server/blakserv
(cd kod && make -f makefile.linux)                   # compiles ~1200 .kod files (serial)
(cd run/server/rsc && ../../../bin/rscmerge -o ../rsc0000.rsb *.rsc)      # the client string table
# copy run/server/{blakserv,loadkod,rsc,memmap,kodbase.txt,rsc0000.rsb} and resource/rooms/*.roo (as rooms/) next to
# server/{admin.py,admin-tui.py,admin-tui.sh,gamesettings.py,start.sh,blakserv.cfg}
```

### Client

Needs the .NET 8 SDK and Godot 4.7.2 **Mono** (C#) — or let the script fetch both into `tools/`:

```bash
tools/build_client.sh release            # downloads the tools if needed
```

By hand: `cd client && dotnet build`, then run `godot --path client` (or open the project in the Godot editor).
The client looks for assets in `client/assets/resource` ([ASSETS.md](ASSETS.md)).

## Developer workflow

* Edit server code in a Meridian59 clone (the dev box has one in `~/src/Meridian59`), build with `make -f makefile.linux` in `kod/` or `blakserv/`,
  run `tools/deploy_kod.sh` to restart the dev server, and `tools/sync_mods.sh` to copy the changed files into `server/mods/overlay`.
* When you add a property to a parent kod class, touch the subclass `.kod` files before building (stale `.bof` files break property layouts).
* Client: `./run.sh [game arguments]` builds and starts the client from the checkout.
* Update the pinned upstream commit: run `tools/sync_mods.sh <clone>` from a clone at the new commit after merging our changes into it.

## Ports

Game `5959`, admin console `9998` (localhost only).  Change them in `server/blakserv.cfg` (`[Socket] Port`, `MaintenancePort`).
