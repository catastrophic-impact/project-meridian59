# Test hooks of the client

Everything here is for developers; none of it does anything unless you set the variable or flag. Run the client like this:

    cd client && source ../env.sh && dotnet build
    "$GODOT_BIN" --path . -- --connect <account> <password> [flags]

Flags (parsed at the top of `client/Main.cs`): `--connect user pw`, `--host`, `--shotdelay N --shot file.png` (screenshot after N seconds), `--third`, `--camdist N`, `--yaw`, `--pitch`, `--autowalk`, `--say "text"`, `--equip "item name"` (`!name` toggles even when worn), `--tab N`, `--uiscale N`, `--mute`, and the self-tests `--gotest --attacktest --targettest --spelltest --resttest --projtest --itemtest --invtest --picktest --testdrag --createtest --resettest`.

## Logging (all off by default)

| Variable | What it does |
| --- | --- |
| `M59_TRACE=1` | prints every network message type, stats and unhandled messages |
| `M59_DEBUGLOG=companions,net,move` | writes `userdata/client_debug.log` (also in the Interface window; chat lines are marked in the log) |
| `M59_ANIMLOG=1` | room animation, doors, lifts, texture changes, light, weapon, group state |
| `M59_AUDIOLOG=1`, `M59_LAGLOG=1`, `M59_ANIMDUMP=<dir>` | sound, lag and animation dumps |

## Things to do automatically

| Variable | What it does |
| --- | --- |
| `M59_OPENWIN=who,mail,guild,details,interface,keys,...` | open these windows at start |
| `M59_TRADETEST=buy\|offer\|withdraw\|deposit\|look` | start that trade with the nearest suitable object (`M59_TRADEAUTO=1` picks and accepts by itself, `M59_TRADEITEM=<name>`, `M59_TRADEPLAYER=1` targets players, `M59_TRADELOOP=1` repeats) |
| `M59_MAILTEST="to\|subject\|text"` | send a letter a few seconds after login |
| `M59_GUILDTEST=<name>` | found a guild when the server offers to |
| `M59_GROUPTEST=invite\|accept\|companion:mage\|healer\|tank\|fighter`, `M59_COMPCMD=comp_attack\|comp_passive\|comp_assist` | group and companion tests |
| `M59_WEATHER=rain\|snow\|sand\|all` (`M59_WXINT`, `M59_WXSTORM`) | draw weather without the server |
| `M59_WALKSTART`, `M59_TURNAT`, `M59_TURNYAW` | walk and turn at set times (with `--autowalk`) |
| `M59_SPELLNAME`, `M59_SPELLTARGET=nearest`, `M59_SPELLLOOP=1`, `M59_ATTACKLOOP=1`, `M59_COMBATTEST=1`, `M59_SHOTONPROJ=1`, `M59_RESTSTAND=1`, `M59_WADETEST` | combat, spell and rest tests |
| `M59_MAPMAX=1`, `M59_MAPALL=1`, `M59_TESTLIGHT=1`, `M59_DRAGDELTA=x,y`, `M59_LEAD=<seconds>` | map, lighting and drag tweaks |
| `M59_AVATAR_SHEET=<dir>`, `M59_AVATAR_OBJ`, `M59_AVATAR_MOTION`, `M59_FACE_DEBUG=<file>` | dump avatar / face composites |
| `M59_RESOURCE=<dir>` | use another resource folder |
| `M59_WINDOWED=1` | do not maximize the window at start (screenshots keep a fixed size) |
| `M59_HIDEHUD=1`, `M59_POVHOLD=1` | hide all windows for a clean screenshot; keep first-person hand animations on their first frame |

Server side (`server/`): `M59_NO_AUTOSTART=1` stops the admin TUI from starting the server itself; `M59_ADMIN_HOST/PORT`, `M59_GAME_PORT`, `M59_BOX_NAME`, `M59_BOX_HOME`, `M59_UPSTREAM_URL`, `M59_ADMIN_VENV` are described in the scripts.

## Server-side test messages (admin console, `server/admin.py "<command>"`)

- find your character: `who` (object id in brackets); object ids change with every server restart; the System object is 0
- `send object <user> TeleportTo RID int <rid>` (rooms in `server/rooms.json`)
- `send object 0 SetWeather iZone int 1 iIntensity int 4 bStorm int 1`
- `send object <room> ToggleCenterLight` (Brax arena, RID 579: texture change), `AnimateWall` (doors, RID 708)
- `send object <user> SendCreateGuild`, `send object <user> SendWeapon`, `send object <user> SendStatsDetail`
- fake effects: `set object <user> piMightMod int 10` (and the other `pi<Stat>Mod`), `set object <user> piFlags3 int 20` (universal bonus %), `send object <user> AddResistance what int -2 value int 30` (negative type = spell type, here fire)
- give things: `create object Money number int 5000` then `send object <user> NewHold what object <id>`
- never change the `dad` / `dadtest` password, and refill dadtest's stamina after tests on that character
