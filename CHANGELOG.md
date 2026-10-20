# Changelog

Newest first. Each push to GitHub gets an entry.

## Unreleased

### Mail, Who and Guild windows
- New top bar buttons: **Mail** (the original mailbox icon), **Who** and **Guild**.
- **Mail**: read, reply, delete and write letters. Like the original client the letters are handed over by the server and kept on your side (`userdata/mail_<character>.json`). New mail is fetched when you log in and when the server says you have some, unread letters are marked and the Mail button turns yellow.
- **Who**: the list of players who are online, kept up to date as people come and go. Double-click a name (or "Send mail") to write to them.
- **Guild**: your guild's name, your rank and its members, with Invite target, Exile, Leave, Hand over and Disband (greyed out when your rank may not do it). When the server offers to found a guild you can pick the name and the five rank titles.

### Camera
- The third-person camera no longer ends up behind walls, above ceilings or below floors when you zoom out: it now stops short of the real room surfaces, with a bit of clearance round the camera so a wall seen at a shallow angle does not poke through (idea from the meridian-shards project, written our own way).

### Room changes from the server
- Walls, floors and ceilings that the server re-textures (for example the lit and unlit spires in the Brax arena) now change on screen (official BP_CHANGE_TEXTURE).

### Testing
- Shop buying, selling to a merchant and player-to-player offers were tested end to end and work. New test hooks: `M59_TRADEITEM`, `M59_TRADEPLAYER`, and `M59_TRADEAUTO` now also answers offers.

### Weather
- **Rain** comes in five strengths, from a light sprinkle to a downpour, and sometimes a moderate or heavy shower is a **thunderstorm** with rare lightning flashes and rolling thunder (strikes about every 30 to 120 seconds, a little more often in a downpour).
- **Overcast**: when it rains outdoors the sky turns grey and dark and the light dims, more the harder it rains. Indoors you stay dry and it fades smoothly as you walk through a door.
- Tiny splashes where drops hit the ground near you, and a rain sound (and thunder) that is louder in heavier rain and muffled indoors. Sounds are made in code for now.
- **Weather zones**: each room is in a zone and each zone has its own weather. Mainland and Ko'catan have rain; deserts, badlands, lava, caves, mines, sewers and the underworld never do. A room class can set its own zone (zone 3 is reserved for a colder north, where snow will come later).
- The sandstorm spell is drawn as in the original. No seasons.
- New admin settings: rain on/off, chance to start and to go on per game hour (5 real minutes), chance of a thunderstorm. New Interface option "Weather (rain, snow, sand)".

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
