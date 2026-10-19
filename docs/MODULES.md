# Modules

```
            +--------------------------- release/ ----------------------------+
            |                                                                  |
  client/  -+->  start-client.sh  --- Godot + C#, needs the assets ----+       |
  (Godot)   |                                                           | TCP   |
            |                                                           v 5959  |
  server/  -+->  start-server.sh  --- blakserv + compiled Blakod  <-----+       |
  (+ mods)  |          ^                                                        |
            |          | admin port 9998 (localhost)                            |
            |   start-admin.sh  --- terminal admin console (TUI)               |
            +------------------------------------------------------------------+
```

## Server module (`server/`)

* **Upstream** Meridian59 server source is not copied into this repository.  `tools/build_server.sh` clones it at the commit named in
  `server/mods/UPSTREAM_COMMIT`, copies `server/mods/overlay/` (our changed and new files, same paths as upstream) over it, builds and assembles
  `release/server/`.
* **Our game-play changes** live in the overlay: death / reagent / bow / eating rules, spell-karma toggle, mob aggro and pathfinding, groups,
  the Brax arena auto-solve, the extension protocol (`UC_EXT_GAME` / `BP_EXT_GAME`) and the **companion bots** (`companion*.kod`, `party.kod`).
  The companion bots are part of the server — they run inside the game server, no extra process.
* **Settings** are properties of the `Settings` object (`kod/util/settings.kod`).  The admin console's *Game settings* tab (list in
  `server/gamesettings.py`) edits them at run time; they are saved with the game.
* **Admin console**: `server/admin-tui.py` (Textual) and the simple `server/admin.py "command"` talk to the server's admin port 9998
  (localhost only).
* **Developers**: after changing a file in a Meridian59 clone, `tools/sync_mods.sh` refreshes the overlay.

## Client module (`client/`)

Godot 4 (Mono / C#).  `lib/` (meridian59-dotnet) provides the network protocol and the file formats (`.bgf`, `.roo`, `.rsb`).  Everything the client
shows is built at run time from the original assets; no asset is imported into the project.  User data (hotbars, window layout, map notes,
server list, debug log) is written to `userdata/` next to the `client/` folder.

* Our server's extension messages are optional: against another server the client simply does not get them (group window, buff timers, combat
  numbers stay empty).

## Load-test bots (`tools/loadtest/`)

A separate C# tool that logs in dozens of simple test clients to measure server load.  Not needed to play.  (Not to be confused with the
*companion bots*, which are in the server.)
