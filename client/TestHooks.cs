using System;
using Godot;
using Meridian59.Data.Models;

/// <summary>Everything the command line self-tests (--gotest, --attacktest, --spelltest, ...) and some M59_* variables do while the game runs. Nothing here runs in normal play.
/// The list of flags and variables is in docs/TESTING.md. To remove a test completely, delete its block here, its option field in Main.cs and its line in the argument parser.</summary>
public partial class Main
{
    void RunTestHooks()
    {
        if (autoSay != null && sayStage >= 0 && roomShown) DriveTypingTest();
        if (optGoTest && roomShown)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (goTestAt == 0) goTestAt = t;
            if (goTestAt > 0 && t - goTestAt > 1.5)
            {
                goTestAt = -1; GD.Print("[test] pressing Space");
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
            }
        }
        if (optRestTest && roomShown && Time.GetTicksMsec() > 6000) { optRestTest = false; SetResting(true); GD.Print($"[test] resting={resting}"); restStandAt = Time.GetTicksMsec() + 8000; }
        if (restStandAt > 0 && Time.GetTicksMsec() > restStandAt && System.Environment.GetEnvironmentVariable("M59_RESTSTAND") == "1") { restStandAt = 0; SetResting(false); GD.Print($"[test] resting={resting}"); }
        if (System.Environment.GetEnvironmentVariable("M59_WADETEST") != null && roomShown && walker != null && Time.GetTicksMsec() > wadeNext && !wadePlaced)
        {   // test hook: stand in the deepest wading sector of this room (local only), M59_WADETEST=1 (any) or the depth number
            wadeNext = Time.GetTicksMsec() + 1500; int want = int.TryParse(System.Environment.GetEnvironmentVariable("M59_WADETEST"), out var wd) ? wd : 1;
            for (float x = 0; x < currentRoom.Width; x += 128)
                for (float y = 0; y < currentRoom.Height; y += 128)
                {
                    int sn = currentRoom.FindSector(x, y);
                    if (sn > 0 && (currentRoom.Sectors[sn - 1].Flags & 3) == want && walker.Place(x, y)) { wadePlaced = true; GD.Print($"[test] wading at {x},{y} sector {sn} depth {want} z {walker.Z}"); y = currentRoom.Height; x = currentRoom.Width; }
                }
        }
        if (optProjTest && roomShown && Time.GetTicksMsec() > 5000 && Time.GetTicksMsec() / 1000.0 > projTestAt)
        {   // test hook: fire a slow synthetic fireball from us to the nearest other object
            projTestAt = Time.GetTicksMsec() / 1000.0 + 100;
            uint rid = 0; foreach (var kv in client.Strings) if (string.Equals(kv.Value, "fireball.bgf", StringComparison.OrdinalIgnoreCase)) { rid = kv.Key; break; }
            uint near = 0; float best = float.MaxValue;
            foreach (var o in client.Objects) if (o.ID != client.PlayerId && objPos.TryGetValue(o.ID, out var op) && walker != null)
            {
                float ddx = op.x - walker.X, ddy = op.y - walker.Y, dd = ddx * ddx + ddy * ddy;
                float fwdDot = (ddx * -Mathf.Sin(yaw) + ddy * -Mathf.Cos(yaw)) / Mathf.Sqrt(Mathf.Max(dd, 1f));   // 1 = straight ahead
                if (dd > 1024f * 1024f * 9 && fwdDot > 0.92f && dd < best) { best = dd; near = o.ID; }
            }
            GD.Print($"[test] projectile rid {rid} -> object {near}");
            if (rid != 0 && near != 0)
                OnProjectile(new Projectile { OverlayFileRID = rid, Animation = new AnimationCycle(100, 1, 1), Source = new ObjectID(client.PlayerId), Target = new ObjectID(near), Speed = 1 });
        }
        if (optTargetTest && roomShown)
        {
            double tt = Time.GetTicksMsec() / 1000.0;
            if (targetTestAt == 0) targetTestAt = tt + 3;
            else if (targetTestAt > 0 && tt > targetTestAt) { targetTestAt = -1; CycleTarget(); GD.Print($"[test] Tab -> target {targetId}"); }
        }
        if (optAttackTest && roomShown)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (attackTestAt == 0) attackTestAt = t;
            if ((attackTestStage < 3 || System.Environment.GetEnvironmentVariable("M59_ATTACKLOOP") == "1") && t - attackTestAt > 1.5)
            {
                attackTestAt = t; attackTestStage++;
                foreach (var o in client.Objects) if (o.Flags.IsAttackable) GD.Print($"[test] attackable {o.ID} '{client.Resolve(o.NameRID)}' at {o.Position2D}");
                RoomObject tgt = null; float best = float.MaxValue;
                foreach (var o in client.Objects)
                    if (o.Flags.IsAttackable && o.ID != client.PlayerId && objPos.TryGetValue(o.ID, out var op) && walker != null)
                    { float dd = (op.x - walker.X) * (op.x - walker.X) + (op.y - walker.Y) * (op.y - walker.Y); if (dd < best) { best = dd; tgt = o; } }
                if (tgt != null) targetId = tgt.ID;
                GD.Print($"[test] pressing F (target {targetId})");
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, PhysicalKeycode = Key.F, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, PhysicalKeycode = Key.F, Pressed = false });
            }
        }
        if (optEquip != null && roomShown && client.Inventory.Count > 0 && (equipReadyAt == 0 ? (equipReadyAt = Time.GetTicksMsec() / 1000.0 + 2.0) < 0 : Time.GetTicksMsec() / 1000.0 > equipReadyAt))
        {
            bool forceToggle = optEquip.StartsWith("!");      // "!name" toggles even if already equipped
            var it = client.Inventory.Find(x => (client.Resolve(x.NameRID) ?? "") == optEquip.TrimStart('!'));
            optEquip = null;
            GD.Print($"[test] equipped set: {string.Join(",", client.Equipped)}; item {it?.ID}");
            if (it != null && (forceToggle || !client.Equipped.Contains(it.ID))) { GD.Print("[test] toggling " + it.ID); if (client.Equipped.Contains(it.ID)) client.UnuseItem(it.ID); else client.UseItem(it.ID); }
        }
        if (optEquipTwo != null && roomShown && client.Inventory.Count > 0 && Time.GetTicksMsec() / 1000.0 > (equipTwoAt == 0 ? (equipTwoAt = Time.GetTicksMsec() / 1000.0 + 2.5) : equipTwoAt))
        {   // test hook: use the FIRST item with this name, then 2 s later the LAST one (two maces)
            var all = client.Inventory.FindAll(x => (client.Resolve(x.NameRID) ?? "") == optEquipTwo);
            if (equipTwoStage == 0 && all.Count > 0) { GD.Print($"[test] use first #{all[0].ID}"); client.UseItem(all[0].ID); equipTwoStage = 1; equipTwoAt = Time.GetTicksMsec() / 1000.0 + 2.0; }
            else if (equipTwoStage == 1 && all.Count > 1) { GD.Print($"[test] use last #{all[^1].ID}; equipped now: {string.Join(",", client.Equipped)}"); client.UseItem(all[^1].ID); equipTwoStage = 2; }
        }
        if (optLookAll != null && roomShown && client.Inventory.Count > 0 && Time.GetTicksMsec() / 1000.0 > (lookAllAt == 0 ? (lookAllAt = Time.GetTicksMsec() / 1000.0 + 2.5) : lookAllAt))
        {   // test hook: ask the server to Look at every inventory item with this name and print the answers
            foreach (var it in client.Inventory.FindAll(x => (client.Resolve(x.NameRID) ?? "") == optLookAll)) { GD.Print($"[test] look at #{it.ID} flags=0x{it.Flags.Value:X}"); client.LookAt(it.ID); }
            optLookAll = null;
        }
        if (optSpellTest && roomShown && client.Spells.Count > 0)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (spellTestAt == 0) spellTestAt = t;
            bool loop = System.Environment.GetEnvironmentVariable("M59_SPELLLOOP") == "1";
            if ((spellTestStage == 0 || (loop && spellTestStage < 8 && t - spellTestAt > 4.0)) && t - spellTestAt > 1.0)
            {
                string want = System.Environment.GetEnvironmentVariable("M59_SPELLNAME");
                var sp = (want != null ? client.Spells.Find(s => (client.Resolve(s.NameRID) ?? "").ToLowerInvariant().Contains(want.ToLowerInvariant())) : null) ?? client.Spells[0];
                spellTestStage++; spellTestAt = t;
                GD.Print($"[test] assign '{client.Resolve(sp.NameRID)}' (targets {sp.TargetsCount}) to slot 2");
                AssignHotbar(1, new Godot.Collections.Dictionary { { "kind", "spell" }, { "id", (long)sp.ID }, { "name", client.Resolve(sp.NameRID) ?? "" } });
                targetId = client.PlayerId;
                if (System.Environment.GetEnvironmentVariable("M59_SPELLTARGET") == "nearest" && walker != null)
                {
                    float best = float.MaxValue;
                    foreach (var o in client.Objects)
                        if (o.Flags.IsAttackable && o.ID != client.PlayerId && objPos.TryGetValue(o.ID, out var op))
                        { float dd = (op.x - walker.X) * (op.x - walker.X) + (op.y - walker.Y) * (op.y - walker.Y); if (dd < best) { best = dd; targetId = o.ID; } }
                }
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Key2, PhysicalKeycode = Key.Key2, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Key2, PhysicalKeycode = Key.Key2, Pressed = false });
            }
        }
        if (optTestDrag && roomShown) DriveDragTest();
        if (optPickTest && roomShown) DrivePickTest();
        if (optItemTest && roomShown && invGrid != null && invGrid.GetChildCount() > 1)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (itemTestAt == 0) itemTestAt = t;
            if (itemTestStage == 0 && t - itemTestAt > 1.0)
            {   // select the 2nd item (torch) with a click, toggle it with the real Space key, then right-click the apple
                var torch = (InvSlot)invGrid.GetChild(1); torch.Clicked?.Invoke(torch);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
                itemTestStage = 1; itemTestAt = t; GD.Print($"[test] selected '{torch.ItemName}' and pressed Space");
            }
            else if (itemTestStage == 1 && t - itemTestAt > 1.0)
            {
                var apple = (InvSlot)invGrid.GetChild(invGrid.GetChildCount() - 2); apple.RightClicked?.Invoke(apple);
                itemTestStage = 2; GD.Print($"[test] right-clicked '{apple.ItemName}'");
            }
        }
        if (optInvTest && roomShown && invGrid != null && invGrid.GetChildCount() > 0)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (invTestAt == 0) invTestAt = t;
            if (invTestStage == 0 && t - invTestAt > 1.0)
            {   // equip the first item (double-click handler) and drop the last one (what dropping onto the world does)
                var first = (InvSlot)invGrid.GetChild(0);
                var last = (InvSlot)invGrid.GetChild(invGrid.GetChildCount() - 1);
                GD.Print($"[test] use '{first.ItemName}', drop '{last.ItemName}'");
                first.Activated?.Invoke(first);
                client.DropItem(last.ItemId, (uint)last.Count);
                invTestStage = 1; invTestAt = t;
            }
            else if (invTestStage == 1 && t - invTestAt > 1.5)
            {   // pick the dropped stack back up
                foreach (var o in client.Objects)
                    if ((client.Resolve(o.NameRID) ?? "") == "mushroom") { GD.Print($"[test] picking up dropped object {o.ID} count={o.Count}"); client.PickUp(o.ID, o.Count); break; }
                invTestStage = 2;
            }
        }
    }
}
