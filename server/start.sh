#!/bin/bash
# Start the Meridian 59 server (foreground).  Ctrl+C to stop.  (Uses the Ubuntu 22.04 distrobox m59-box when it exists.)
cd "$(dirname "$(readlink -f "$0")")"
# The server is built on Ubuntu 22.04.  When it is started from another system and the m59-box distrobox exists, run it inside the box.
if [ -z "$CONTAINER_ID" ] && command -v distrobox >/dev/null 2>&1 && distrobox list 2>/dev/null | grep -q "m59-box"; then
  exec distrobox enter m59-box -- "$PWD/start.sh"
fi

echo "Starting Meridian 59 server..."
./blakserv < /dev/null > console.out 2>&1 &
pid=$!
trap 'echo; echo "Stopping server..."; kill $pid 2>/dev/null; wait $pid 2>/dev/null; echo "Server stopped."; exit 0' INT TERM

# wait (up to 60s) until the game port accepts connections
for i in $(seq 1 120); do
  kill -0 $pid 2>/dev/null || { echo "Server exited during start-up. See console.out and channel/error-*.txt"; exit 1; }
  if (exec 3<>/dev/tcp/127.0.0.1/5959) 2>/dev/null; then
    echo "Server started.  (game port 5959, admin port 9998 - use ./admin-tui.sh)"
    break
  fi
  sleep 0.5
done
wait $pid
echo "Server stopped."
