# Changelog

Newest first. Each push to GitHub gets an entry.

## Unreleased

### Companion bots
- New **Fighter** bot: shoots a bow from up to 9 squares (no archery skill needed) and fights in melee when the enemy is close.
- **Tank taunt**: enemies attacking other group members switch to the tank.
- Bots only fight enemies that are attacking the group (or that you are fighting). Melee bots wait until the enemy is within 7 squares; the mage and fighter fire from up to 12 squares with line of sight.
- Bots ignore enemies they cannot walk to instead of standing still.
- The Passive order now holds even after the bot receives a buff.
- Bots only teleport to you as a last resort (more than 25 squares away, or stuck for about 11 seconds). New admin setting to turn this off.

### Movement
- The server now uses the accurate room walk map for creatures and players (admin setting "Walls / line of sight accuracy"). Mobs and bots no longer get stuck where players can walk.
- Route following no longer gives up early, which stops bots pacing back and forth at corners.
- The client moves other creatures through every position the server reports, and teleports appear instantly instead of sliding through walls.

### Client
- **Water and other scrolling floors** now flow at the original speed. They were moving 16 times too fast, which made ponds like the one in Raza look like rivers.
- **Shops and trading**: right-click a shop keeper, banker or player and the Look window shows the trade buttons that apply (Buy, Sell / offer, and Withdraw / Deposit for bankers). Buying shows the seller's list with prices and amounts; offers to players or shop keepers open an offer window where the other side's answer appears and you accept or cancel.
- The Look window text is smaller and scrolls when a description is long.
- **Doors and animated walls**: the client now follows the server's wall changes (guild hall doors, secret walls, clock faces): texture changes, one-shot and looping animations, and walls that become passable, impassable or invisible.
- Gates and lifts now show the state the server sets when you enter a room (before, early messages were dropped and gates flickered between open and closed). New Interface option "Flashing wall textures" for walls that animate on their own.
- Top bar has separate **Bots** and **Group** buttons.
- Group state is cleared when you log in with another account.
- Interface window is scrollable and resizable.
- Debug log has a movement topic, and chat messages become markers in the log.

### Admin console
- Starts the server automatically when it is not running, and asks whether to stop it when you quit.
- New settings: bot teleport, wall accuracy. The death setting is now called "Lose HP / skills on death".

## 2026-10-19 - Initial public import
- Server (with admin console), client, tools and docs. Game assets are not included.
