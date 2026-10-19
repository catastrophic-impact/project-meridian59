#!/usr/bin/env python3
"""Create bot accounts + empty character slots on the local server (admin port 9998).
Usage: provision.py COUNT [PREFIX] [PASSWORD]   -> accounts PREFIX001.. ; characters are created by the bots on first login.
Skips accounts that already exist. Writes bots/accounts.txt (user:password per line)."""
import re, socket, sys, time, os
count = int(sys.argv[1]); prefix = sys.argv[2] if len(sys.argv) > 2 else "bot"; pw = sys.argv[3] if len(sys.argv) > 3 else "botpw"
s = socket.create_connection(('127.0.0.1', 9998), timeout=5)
def rd(t=0.4):
    s.settimeout(t); out = b''
    try:
        while True:
            d = s.recv(4096)
            if not d: break
            out += d
    except Exception: pass
    return out.decode('latin-1')
def cmd(c):
    s.sendall((c + '\r').encode()); return rd()
rd(0.3)
existing = {}
for m in re.finditer(r'^\s*(\d+)A?\s+(\S+)', cmd("show accounts"), re.M): existing[m.group(2)] = int(m.group(1))
rows = []
for i in range(1, count + 1):
    name = f"{prefix}{i:03d}"
    if name not in existing:
        m = re.search(r'Created ACCOUNT (\d+)', cmd(f"create account user {name} {pw}"))
        if not m: print("failed", name); continue
        existing[name] = int(m.group(1))
        cmd(f"create user {existing[name]}")      # one empty (new) character slot
    rows.append(f"{name}:{pw}")
cmd("save game")
open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "accounts.txt"), "w").write("\n".join(rows) + "\n")
print(f"{len(rows)} bot accounts ready")
