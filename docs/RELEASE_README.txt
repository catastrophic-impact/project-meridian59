Project Meridian 59 - release folder
====================================

server/        the game server and admin console
client/        the portable Godot client (put the game assets into client/assets/resource, see below)
runtime/       Godot and the .NET runtime used by start-client.sh - nothing has to be installed

1. Assets (once): the game artwork is not included.  From an installed original Meridian 59 client run
       python3 <project>/tools/extract_assets.py "/path/to/Meridian 59" --dest client/assets
   (the script is in the project's tools folder; see docs/ASSETS.md in the project)
2. ./start-server.sh        starts the game server (Ctrl+C stops it).  Game port 5959.
3. ./start-admin.sh         the terminal admin console (accounts, settings, server control).  Optional.
4. ./start-client.sh        the game client.  Log in with an account you created in the admin console.

Create the first account in the admin console, or with:   server/admin.py "create account user <name> <password>"
Make an account an administrator:                        server/admin.py "create account admin <name> <password>"
The server keeps the world in server/savegame; delete that folder for a fresh world.
