#!/usr/bin/env python3
"""Generate server/items.json (class, display name, category, stackable) from the official Kod source.
Usage: make_itemlist.py [path-to-Meridian59-clone [output.json]]   (default ~/boxes/m59-box/src/Meridian59)"""
import json, os, re, sys
src = os.path.expanduser(sys.argv[1] if len(sys.argv) > 1 else "~/boxes/m59-box/src/Meridian59")
root = f"{src}/kod/object/item"

# reagents = classes that any spell lists in plReagents
reagents = set()
for d, _, files in os.walk(f"{src}/kod/object/passive/spell"):
    for f in files:
        if f.endswith(".kod"):
            for m in re.finditer(r"Cons\(\s*\[\s*&(\w+)\s*,\s*\d+\s*\]\s*,\s*plReagents", open(os.path.join(d, f), errors="replace").read()):
                reagents.add(m.group(1))

classes = {}   # name -> dict(parent, path, text)
for d, _, files in os.walk(root):
    for f in files:
        if not f.endswith(".kod"):
            continue
        t = open(os.path.join(d, f), errors="replace").read()
        m = re.search(r"^(\w+)\s+is\s+(\w+)\s*$", t, re.M)
        if m:
            classes[m.group(1)] = {"parent": m.group(2), "path": os.path.relpath(d, root), "text": t}

def ancestors(c):
    out = []
    while c in classes:
        c = classes[c]["parent"]; out.append(c)
    return out

def category(name, c):
    p = re.sub(r"^(passitem|actitem)/?", "", c["path"])      # e.g. passitem/weapon/ranged -> weapon/ranged
    if name in reagents: return "Reagent"
    if name == "Money": return "Money"
    for key, cat in (("weapon/ranged", "Ranged weapon"), ("weapon", "Weapon"), ("defmod/armor", "Armor"), ("defmod/shield", "Shield"),
                     ("defmod/helmet", "Helmet"), ("defmod/pants", "Pants"), ("defmod/shirt", "Shirt"), ("numbitem/ammo", "Ammo"),
                     ("numbitem/food", "Food"), ("spelitem/potion", "Potion"), ("spelitem/scroll", "Scroll"), ("spelitem/wand", "Wand"),
                     ("ring", "Jewelry"), ("necklace", "Jewelry"), ("attmod", "Jewelry"), ("instrum", "Instrument"), ("totem", "Totem")):
        if p.startswith(key): return cat
    return "Other"

items = []
for name, c in classes.items():
    m = re.search(r"^\s*vrName\s*=\s*(\w+)", c["text"], re.M)          # only classes that define their own name (skips abstract bases)
    if not m:
        continue
    s = re.search(r"^\s*" + re.escape(m.group(1)) + r'\s*=\s*"([^"]*)"', c["text"], re.M)
    nice = s.group(1) if s else name
    items.append({"class": name, "name": nice, "category": category(name, c), "stack": "NumberItem" in ancestors(name)})
items.sort(key=lambda i: (i["category"], i["name"].lower()))
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "../server/items.json")
json.dump(items, open(out, "w"), indent=0)
from collections import Counter
print(len(items), "items ->", os.path.abspath(out)); print(dict(Counter(i["category"] for i in items)))
