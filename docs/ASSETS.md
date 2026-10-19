# Game assets

The client needs the artwork, room files and sounds of the original game.  They are not part of this repository (they belong to the
original game's owners), so every user takes them from their own copy of the original client.

## What you need

An installed original Meridian 59 client — for example the one from <https://www.meridian59.com> or the Open Meridian clients
(servers 103 / 105).  On Linux install it with Wine or Proton (Steam "Add a non-Steam game"); you only need its folder, you never have to run it.
The folder contains `Meridian.exe` and a `resource` folder with thousands of `.bgf`, `.roo` and `.ogg` files.

## Extract

```bash
tools/extract_assets.py "/path/to/Meridian 59"                          # developer checkout  -> client/assets/resource
tools/extract_assets.py "/path/to/Meridian 59" --dest release/client/assets   # portable release   -> release/client/assets/resource
```

* `--check` only lists what would be copied.  `--link` creates symlinks instead of copies (saves 300 MB on the same machine).
* The tool copies only what the client uses: `.bgf` (graphics), `.roo` (rooms), `.ogg` (sound / music), `.bsf` (skies), `.rsb` (string tables).
* The client looks for assets in this order: environment variable `M59_RESOURCE`, then `client/assets/resource`, then (developer checkout
  only) `../client_files/resource`.

If the assets are missing, the client's login screen says so.

## Local server and other servers

* Our own server builds its own string table (`release/server/rsc0000.rsb`) and uses the room files of the upstream source, so the **server needs no
  assets from you**.
* To connect the client to an official / other server it needs that server's string table (`rsc0000.rsb` of its version).  Put it next to the others in
  `assets/resource` and enter its path in the server list (the built-in entries look for `rsc0000-101.rsb`, `rsc0000-103.rsb`, `rsc0000-105.rsb`).
