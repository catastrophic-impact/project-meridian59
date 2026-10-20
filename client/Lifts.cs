using System.Collections.Generic;
using Godot;
using Meridian59.Files.Room;

/// <summary>Lifts / moving floors and ceilings (BP_SECTOR_MOVE, official roomanim.c MoveSector): sectors that move are built as their own mesh, rebuilt while they move.
/// Heights are written into the room data (RoomSector.FloorHeight / CeilingHeight, kod units), so walking and sprite heights follow automatically.</summary>
public partial class Main
{
    class Lift { public int Sec; public bool Ceil; public float Src, Dst, Inc, Progress; }
    readonly List<Lift> lifts = new List<Lift>();
    readonly HashSet<int> dynSectors = new HashSet<int>();
    Node3D dynNode; bool dynDirty; double dynRebuildAt;

    void ClearLifts() { ClearDoors(); lifts.Clear(); dynSectors.Clear(); dynNode?.QueueFree(); dynNode = null; dynDirty = false; }

    void OnSectorMoved(byte type, ushort id, ushort height, byte speed)
    {
        if (currentRoom == null || !roomShown) return;
        bool ceil = type == 5, grew = false;
        for (int i = 0; i < currentRoom.Sectors.Count; i++)
        {
            var sec = currentRoom.Sectors[i];
            if (sec.ServerId != id) continue;
            int num = i + 1;
            if (dynSectors.Add(num)) grew = true;
            float cur = ceil ? sec.CeilingHeight : sec.FloorHeight;
            lifts.RemoveAll(l => l.Sec == num && l.Ceil == ceil);
            if (speed == 0 || cur == height) { SetSectorHeight(sec, ceil, height); dynDirty = true; continue; }
            lifts.Add(new Lift { Sec = num, Ceil = ceil, Src = cur, Dst = height, Inc = (speed / 1000f) / Mathf.Abs(height - cur) });   // progress per ms: speed kod units per second
        }
        if (AnimLog) GD.Print($"[lift] sector {id} {(ceil ? "ceiling" : "floor")} -> {height} speed {speed} ({lifts.Count} moving)");
        if (grew) RebuildStaticRoom();
        dynDirty = true;
    }

    static void SetSectorHeight(RoomSector s, bool ceil, float kod)
    {
        ushort h = (ushort)Mathf.Clamp(Mathf.RoundToInt(kod), 0, 65535);
        if (ceil) s.CeilingHeight = h; else s.FloorHeight = h;
    }

    void RebuildStaticRoom()
    {
        var b = MakeBuilder(currentRoom);
        var node = b.Build(dynSectors, false);
        current?.QueueFree();
        current = node; roomAnims = b.Anims;
        AddChild(current);
    }

    void UpdateLifts(float dt)
    {
        UpdateDoors(dt);
        if (lifts.Count == 0 && !dynDirty) return;
        for (int i = lifts.Count - 1; i >= 0; i--)
        {
            var l = lifts[i];
            l.Progress += l.Inc * dt * 1000f;
            float z = l.Progress >= 1f ? l.Dst : l.Src + l.Progress * (l.Dst - l.Src);
            SetSectorHeight(currentRoom.Sectors[l.Sec - 1], l.Ceil, z);
            dynDirty = true;
            if (l.Progress >= 1f) lifts.RemoveAt(i);
        }
        double now = Time.GetTicksMsec();
        if (dynDirty && now >= dynRebuildAt)
        {
            dynDirty = false; dynRebuildAt = now + 50;      // at most 20 rebuilds per second
            var node = MakeBuilder(currentRoom).Build(dynSectors, true);
            dynNode?.QueueFree();
            dynNode = node;
            AddChild(dynNode);
            foreach (var id in new List<uint>(objSprites.Keys)) PlaceSprite(id);
            if (lifts.Count > 0) dynDirty = true;
        }
    }
}
