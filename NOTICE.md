# Notices and credits

This project builds on the work of others.

* **Meridian 59** — game, server source (`blakserv`, Blakod, compiler) © 1996-2012 Andrew Kirmse and Chris Kirmse, released under the GNU
  General Public License; maintained at <https://github.com/Meridian59/Meridian59>.  Our server build downloads that repository
  (pinned to the commit in `server/mods/UPSTREAM_COMMIT`) and copies our changed files from `server/mods/overlay/` over it.  Those changed
  files are modified versions of the upstream files and remain under the upstream licence.
* **meridian59-dotnet** — © Clint Banzhaf, GPLv3, <https://github.com/OpenMeridian/meridian59-dotnet>; `lib/` contains the parts of it used by our client.
* **Godot Engine** (MIT) — downloaded by `tools/fetch_tools.sh`, not included.  **.NET** (MIT) — downloaded by the same script.
* **Textual** (MIT) — the terminal UI toolkit of the admin console, installed with pip on first run.
* The `tools/dotnet-install.sh` script is © .NET Foundation, MIT.

**Game assets** (graphics `.bgf`, rooms `.roo`, sounds `.ogg`, sky `.bsf`, string tables `.rsb`) are © their owners and are **not**
distributed here; `tools/extract_assets.py` copies them from a client installation you obtained yourself.
