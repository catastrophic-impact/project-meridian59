#!/usr/bin/env python3
"""Copy the game assets our Godot client needs out of an original Meridian 59 client installation.

The assets (graphics .bgf, rooms .roo, sounds/music .ogg, sky .bsf, string tables .rsb) belong to the original game and are NOT part of
this repository.  Install the original client (for example the Meridian 59 / Open Meridian 103-105 client, under Wine or Proton on Linux),
then run

    tools/extract_assets.py "/path/to/Meridian 59"            # the folder that contains Meridian.exe and the resource folder
    tools/extract_assets.py "/path/to/Meridian 59/resource"   # also fine

By default the files are copied to  client/assets/resource  (next to the Godot project, where the client looks first).  With a release build use
    tools/extract_assets.py "<install>" --dest release/client/assets

Other options:  --link  (symlink instead of copy; saves disk space on the same machine)   --check  (only report what would be copied)
"""
import argparse, os, shutil, sys

WANT = {".bgf", ".roo", ".ogg", ".bsf", ".rsb", ".wav", ".mp3"}
EXTRA = {"heidelb1.ttf"}          # fonts shipped with the original client


def find_resource_dir(path):
    path = os.path.abspath(os.path.expanduser(path))
    for cand in (path, os.path.join(path, "resource"), os.path.join(path, "Resource")):
        if os.path.isdir(cand) and any(f.lower().endswith(".bgf") for f in os.listdir(cand)):
            return cand
    return None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("source", help="original client folder (with a resource subfolder) or the resource folder itself")
    ap.add_argument("--dest", help="destination assets folder (default: client/assets next to this script's repository)")
    ap.add_argument("--link", action="store_true", help="symlink the files instead of copying them")
    ap.add_argument("--check", action="store_true", help="only list what would be copied")
    a = ap.parse_args()

    src = find_resource_dir(a.source)
    if not src:
        sys.exit("No resource folder with .bgf files found in: " + a.source)
    root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
    dest = os.path.abspath(a.dest) if a.dest else os.path.join(root, "client", "assets")
    out = os.path.join(dest, "resource")

    files = [f for f in sorted(os.listdir(src))
             if os.path.isfile(os.path.join(src, f)) and (os.path.splitext(f)[1].lower() in WANT or f.lower() in EXTRA)]
    counts = {}
    size = 0
    for f in files:
        ext = os.path.splitext(f)[1].lower()
        counts[ext] = counts.get(ext, 0) + 1
        size += os.path.getsize(os.path.join(src, f))
    print(f"source : {src}")
    print(f"target : {out}")
    print("found  : " + ", ".join(f"{n} {e}" for e, n in sorted(counts.items())) + f"  ({size / 1e6:.0f} MB)")
    for need, label in ((".bgf", "graphics"), (".roo", "rooms"), (".ogg", "sounds/music")):
        if not counts.get(need):
            print(f"WARNING: no {label} ({need}) files found - is this a complete client install?")
    if a.check:
        return
    os.makedirs(out, exist_ok=True)
    for f in files:
        s, d = os.path.join(src, f), os.path.join(out, f)
        if os.path.lexists(d):
            os.remove(d)
        if a.link:
            os.symlink(s, d)
        else:
            shutil.copy2(s, d)
    print(f"done: {len(files)} files {'linked' if a.link else 'copied'}.")
    print("Server string tables of other servers (rsc0000-101.rsb ...) come from the same folder; the local server uses its own rsc0000.rsb.")


if __name__ == "__main__":
    main()
