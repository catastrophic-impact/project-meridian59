#!/usr/bin/env python3
"""Generate server/rooms.json (room number, display name, map file) from the official Kod source.
Usage: make_roomlist.py [path-to-Meridian59-clone [output.json]]   (default ~/boxes/m59-box/src/Meridian59)"""
import json, os, re, sys
src = os.path.expanduser(sys.argv[1] if len(sys.argv) > 1 else "~/boxes/m59-box/src/Meridian59")
rid = {m.group(1): int(m.group(2)) for m in re.finditer(r"^\s*(RID_[A-Z0-9_]+)\s*=\s*(\d+)", open(f"{src}/kod/include/blakston.khd", errors="replace").read(), re.M)}
rooms = []
for root, _, files in os.walk(f"{src}/kod/object/active/holder/room"):
    for f in files:
        if not f.endswith(".kod"):
            continue
        t = open(os.path.join(root, f), errors="replace").read()
        m = re.search(r"piRoom_num\s*=\s*(RID_[A-Z0-9_]+)", t)
        if not m or m.group(1) not in rid:
            continue
        nm = re.search(r"vrName\s*=\s*(\w+)", t)
        name = None
        if nm:
            s = re.search(r"^\s*" + re.escape(nm.group(1)) + r'\s*=\s*"([^"]*)"', t, re.M)
            name = s.group(1) if s else None
        pr = re.search(r"prRoom\s*=\s*(\w+)", t)
        roo = None
        if pr:
            s = re.search(r"^\s*" + re.escape(pr.group(1)) + r"\s*=\s*([\w.\-]+\.roo)", t, re.M | re.I)
            roo = s.group(1) if s else None
        rooms.append({"rid": rid[m.group(1)], "const": m.group(1), "name": name or m.group(1), "file": roo})
# some room numbers are used by more than one kod file (variants): keep one entry per number, preferring a real name and a map file
best = {}
for r in rooms:
    k = r["rid"]
    score = (r["name"] != r["const"], bool(r["file"]))
    if k not in best or score > best[k][0]:
        best[k] = (score, r)
print(len(rooms) - len(best), "duplicate room numbers dropped")
rooms = [v[1] for v in best.values()]
rooms.sort(key=lambda r: r["rid"])
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "../server/rooms.json")
json.dump(rooms, open(out, "w"), indent=0)
print(len(rooms), "rooms ->", os.path.abspath(out))
