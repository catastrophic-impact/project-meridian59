# Project Meridian 59

A portable Linux server and a modern 3D client for **Meridian 59**, the 1996 first-person online RPG, with our own
game-play changes on top (a DAoC / WoW style direction).

* **Server** — the open-source Meridian 59 server (`blakserv` + the Blakod game code) built for Linux, plus our modifications and a
  terminal admin console (TUI) with a settings tab.  Everything is configurable at run time: death rules, reagent rules,
  mob aggro, group size, companion bots and more.
* **Client** — a new client written with the Godot 4 engine (C#), re-using the protocol / file-format library of
  [meridian59-dotnet](https://github.com/OpenMeridian/meridian59-dotnet).  Real-time lighting, lifts, doors, water, weather, group window, buff bar,
  floating combat text, shops and trading, mail, a full-screen map and more.
* **Companion bots** — temporary mage / healer / tank / fighter helpers that join your group (server side, part of the server module).

> **The game assets (graphics, rooms, sounds) are not in this repository.**  Install the original game client and run
> `tools/extract_assets.py` — see [docs/ASSETS.md](docs/ASSETS.md).

## Highlights

### Server (everything below is a run-time setting in the admin console's *Game settings* tab unless noted)

* **Death rules** — choose separately for PvP / PvE / always: drop items on death, lose max health and skills; optional "restart at 1/1/1" after death.
* **Reagents** — mandatory (original), free, or **no-reagent casting at a set effectiveness (10 / 25 / 50 / 75 %)**: damage, healing and durations
  scale down, cast time and utility mana cost scale up, summons weaker; Jala songs and reagent-less spells are unaffected.
* **Spell karma** requirement can be switched off; **unlimited eating**; **bow rules** (damage without arrows, auto-equip ammo, ammo use).
* **Logoff ghost** modes (off / die and go to the underworld / normal).  **Brax arena** pillar puzzle solves itself on a timer.
* **Mob aggro** — WoW / DAoC style proximity aggro with a radius and line of sight, any number of mobs at once (original mode still selectable).
* **Pathfinding for every monster** — breadth-first path over the room's walk grid when a mob is blocked, so mobs stop rubbing along walls.
  The grid is the accurate one by default (setting *Walls / line of sight accuracy*), so mobs and bots take real doorways and corners.
* **Weather** — rain in five strengths, thunderstorms, per-zone weather (deserts, caves, mines, sewers and the underworld never get any; the sandstorm spell is as in the original);
  chances are settings. Sent to our client only; other clients just see the original rain / clear effects.
* **Groups** (up to 5) with invites, a shared health/mana window and target health.
* **Companion bots** — summon a **mage, healer, tank or fighter** that joins your group and never friendly-fires:
  strength from your "effective level"; spells limited by what *your* character could actually learn (the game's own learning rules);
  their own mana (they rest with you); healers heal and keep the group buffed; tanks tank and taunt; fighters shoot a bow and fight in melee up close; bots sprint, path around walls, react the moment a
  mob aggros you, and take **attack / passive / assist** orders (draggable to the hotbar).  They get gender-matched first names.
* **Extension protocol** (optional, ignored by other clients): group state, buff timers, target health, damage and heal numbers, weather, the wielded weapon, detailed character numbers.

### Admin console (terminal UI, mouse supported — `start-admin.sh`)

Tabs: **Console** (run any admin command, quick buttons for status / who / users) · **Accounts** (create accounts and characters, suspend, kick,
delete, details) · **Online** (who is connected, hang up sessions) · **Server** (starts the server for you if it is down and offers to stop it when you quit; start / stop / restart with save, save game, garbage collect, lock the game,
reload, memory / timers / usage) · **Game settings** (every rule above, with descriptions; changes apply at once and are saved) ·
**Teleport** (move a player to any room) · **Bots** (start and stop load-test clients).

### Client

* Godot 4 renderer for the original room, sprite and sound formats, with **real-time lighting ported from the original** — time of day,
  sector light, torches and lamps, sun and moon, fog; wading in water; moving **lifts**; **doors and gates** the server opens and closes, walls and floors it re-textures;
  coloured and layered avatars; weapons, projectiles, effects; **rain, thunderstorms and sandstorms** with an overcast sky.
* **Buff / debuff bar** with time remaining, **floating combat text** and combat lines in the chat, **group window and a Bots window**, hotbar for
  spells, items, skills and companion orders (the Attack button shows your wielded weapon), **full-screen map** with the original look and your own notes (saved per character),
  stamina rules for sprinting, character creation, server list, volume settings, admin tools window, optional debug log.
* **Shops and trading** — right-click a merchant, banker or player in the Look window: buy, sell, make and answer offers, deposit and withdraw; **mail**, a **Who** list and a **Guild** window on the top bar.
* **Growth bar** — shows how close the character is to getting tougher (the server's hidden max-health gain counter).
* **Stats window** — attributes with effect bonuses, offense / defense ratings, hit chances against your target, resistances (server numbers).
* **Third-person camera** that stays inside the room, **key remapping** (Interface window), windows you drag from any empty spot (no title bars), resize and lock, UI scale, **layout templates**
  (ready-made and your own), starts maximized with F11 for full screen; name tags hide behind walls.
* Works with our server's extras; against other servers it still plays the base game (group, buff timers and combat numbers stay empty).

## Quick start

```bash
git clone https://github.com/catastrophic-impact/project-meridian59
cd project-meridian59
tools/build_release.sh --setup-box                      # builds the Ubuntu 22.04 box, the server and the client into release/
tools/extract_assets.py "/path/to/Meridian 59" --dest release/client/assets   # once, from the original client install
release/start-server.sh                                  # terminal 1
release/start-admin.sh                                   # terminal 2 (optional admin console / settings)
release/start-client.sh                                  # the game
```

Details, manual build steps and requirements: [docs/BUILDING.md](docs/BUILDING.md).  How the pieces fit together:
[docs/MODULES.md](docs/MODULES.md).

## Repository layout

| Path | What it is |
|---|---|
| `server/` | **Server module**: admin console + TUI, settings list, config, start script, and `mods/` (our changes to the upstream server source) |
| `client/` | **Client module**: the Godot project (C#) |
| `lib/` | the meridian59-dotnet protocol / file format library used by the client (GPLv3) |
| `tools/` | build scripts (`build_release.sh`, `build_server.sh`, `build_client.sh`, `setup_distrobox.sh`), `extract_assets.py`, developer helpers |
| `docs/` | documentation, including [docs/TESTING.md](docs/TESTING.md) (test flags and variables) |

## Licence

GNU GPL v3 (see `LICENSE` and `NOTICE.md`).  Meridian 59 is © Andrew and Chris Kirmse; the server source is the open-source release
maintained at <https://github.com/Meridian59/Meridian59>.  This project is not affiliated with the Meridian 59 teams.
