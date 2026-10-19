#!/usr/bin/env python3
"""Give the shared test character everything needed for spell / effect tests: all spells, lots of mana, every reagent, gold.
Account: tester / tester (account name "tester"), character "Tester".  Run with the server running:  python3 tools/tester_setup.py
Re-run any time (e.g. after the character was wiped and re-created). Options: --wipe = delete the character and make a fresh empty slot (then log in once with
the client to create it again: client --connect tester tester --createtest Tester), --acct NAME (default tester)."""
import json, os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ADMIN = os.path.join(ROOT, "server", "admin.py")
KHD = os.path.expanduser("~/boxes/m59-box/src/Meridian59/kod/include/blakston.khd")
acct = "tester"
if "--acct" in sys.argv: acct = sys.argv[sys.argv.index("--acct") + 1]

def admin(*cmds):
    out = subprocess.run([sys.executable, ADMIN, *cmds], capture_output=True, text=True, timeout=120).stdout
    return out

def account_number():
    for line in admin("show accounts").splitlines():
        p = line.split()
        if len(p) >= 2 and p[1] == acct and p[0].rstrip("ADM").isdigit(): return int(p[0].rstrip("ADM"))
    raise SystemExit(f"account {acct} not found (create it: admin.py 'create account user {acct} {acct}')")

def user_object(num):
    out = admin(f"show account {num}")
    rows = [l.split() for l in out.splitlines() if re.match(r"\s*%d\s+\d+\s+User" % num, l)]
    return [int(r[1]) for r in rows]

num = account_number()
if "--wipe" in sys.argv:
    for o in user_object(num): admin(f"delete user {o}")
    admin(f"create user {num}")
    print("wiped; log in with the client once to create the character again (client --connect tester tester --createtest Tester), then run this script again")
    sys.exit(0)

objs = user_object(num)
if not objs: raise SystemExit("no character yet: log in once with the client to create it")
me = objs[-1]
print(f"account {acct} = #{num}, character object #{me}")

# spells
sids = [int(m.group(1)) for m in re.finditer(r"^\s*SID_[A-Z_0-9]+\s*=\s*(\d+)", open(KHD).read(), re.M)]
sids = sorted(set(sids) - {48, 15, 16, 69})   # 48 = Trance: an internal spell with no name of its own (the client showed it as "something")
added = 0
for i in range(0, len(sids), 25):
    out = admin(*[f"send object {me} AddSpell num int {s} iability int 99" for s in sids[i:i+25]])
    added += out.count("return from OBJECT")
print(f"AddSpell sent for {len(sids)} spell ids")

# mana + stats
print(admin(f"set object {me} piMax_mana int 999", f"set object {me} piMana int 999", f"set object {me} piIntellect int 50", f"set object {me} piMysticism int 50").count("\n"), "lines")

# reagents (every reagent class, 50 each) + gold
items = json.load(open(os.path.join(ROOT, "server", "items.json")))
items = items if isinstance(items, list) else items.get("items", items)
classes = [i["class"] for i in items if i.get("category", "").lower().startswith("reag")]
for c in classes:
    out = admin(f"create object {c} number int 50")
    m = re.search(r"Created object (\d+)", out)
    if m: admin(f"send object {me} NewHold what object {m.group(1)}")
print(f"{len(classes)} reagent stacks given")
# ranged weapons + ammo
for c, n in (("Bow", 0), ("Longbow", 0), ("Arrow", 300), ("SilverArrow", 100), ("FireArrow", 100)):
    out = admin(f"create object {c}" + (f" number int {n}" if n else "")); m = re.search(r"Created object (\d+)", out)
    if m: admin(f"send object {me} NewHold what object {m.group(1)}")
print("bows and arrows given")
out = admin("create object Money number int 5000"); m = re.search(r"Created object (\d+)", out)
if m: admin(f"send object {me} NewHold what object {m.group(1)}"); print("5000 gold given")
print("done. Mana refills over time; re-run this script for a full refill.")
