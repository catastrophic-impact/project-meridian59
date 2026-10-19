#!/bin/bash
# Launch the TUI admin console. Creates a private Python venv (with 'textual') on first run.
here="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
venv="${M59_ADMIN_VENV:-$here/.venv}"
[ -x "$here/../tools/admin-venv/bin/python" ] && venv="${M59_ADMIN_VENV:-$here/../tools/admin-venv}"   # developer checkout
if [ ! -x "$venv/bin/python" ]; then
  echo "First run: creating $venv ..." >&2
  python3 -m venv "$venv" && "$venv/bin/pip" install -q textual || exit 1
fi
exec "$venv/bin/python" "$here/admin-tui.py" "$@"
