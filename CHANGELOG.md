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
- Top bar has separate **Bots** and **Group** buttons.
- Group state is cleared when you log in with another account.
- Interface window is scrollable and resizable.
- Debug log has a movement topic, and chat messages become markers in the log.

### Admin console
- Starts the server automatically when it is not running, and asks whether to stop it when you quit.
- New settings: bot teleport, wall accuracy. The death setting is now called "Lose HP / skills on death".

## 2026-10-19 - Initial public import
- Server (with admin console), client, tools and docs. Game assets are not included.
